using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using PilotCapture.Application;
using PilotCapture.Domain;

namespace PilotCapture.Infrastructure.Persistence;

public sealed class ImageIngestService(
    PilotCaptureDbContext dbContext,
    IImageAssetStore assetStore,
    ICaptureDataStore captureDataStore) : IImageIngestService
{
    public async Task<ImageIngestResult> ImportJpegAsync(
        string captureSessionId,
        string captureSetId,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var sourceLastWriteUtc = new DateTimeOffset(File.GetLastWriteTimeUtc(fullSourcePath), TimeSpan.Zero);
        var existingAsset = await dbContext.ImageAssets.AsNoTracking()
            .SingleOrDefaultAsync(asset => asset.SourcePath == fullSourcePath
                && asset.SourceLastWriteUtc == sourceLastWriteUtc, cancellationToken);
        if (existingAsset is not null)
        {
            var existingImage = await dbContext.CaptureImages.AsNoTracking()
                .SingleOrDefaultAsync(image => image.ImageAssetId == existingAsset.Id, cancellationToken)
                ?? throw new InvalidOperationException("The previously imported image has no capture record.");
            return new ImageIngestResult(
                existingImage.Id,
                existingAsset.OriginalFileName,
                existingAsset.Sha256 ?? string.Empty,
                existingImage.SequenceNumber,
                true);
        }

        var session = await dbContext.CaptureSessions.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == captureSessionId && item.EndedAtUtc == null,
            cancellationToken)
            ?? throw new InvalidOperationException("A JPEG can only be ingested into an active capture session.");
        var captureSet = await dbContext.CaptureSets.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == captureSetId && item.CaptureSessionId == session.Id,
            cancellationToken)
            ?? throw new InvalidOperationException("The selected capture set does not belong to this session.");

        var installation = await dbContext.LocalInstallations.AsNoTracking().SingleAsync(cancellationToken);
        var outputFolder = installation.SmartShooterOutputPath;
        if (string.IsNullOrWhiteSpace(outputFolder)
            || !IsWithinFolder(fullSourcePath, outputFolder))
            throw new InvalidOperationException("The JPEG is outside the configured Smart Shooter output folder.");
        if (sourceLastWriteUtc < captureSet.StartedAtUtc)
            throw new InvalidOperationException("This file predates the selected subject's capture set and was skipped.");

        var stored = await assetStore.StoreJpegAsync(
            session.EventId,
            session.Id,
            captureSet.Id,
            fullSourcePath,
            cancellationToken);
        // StoreJpegAsync waits for Smart Shooter to finish writing the file. Use
        // the settled timestamp as the idempotency key, not the timestamp seen
        // when the folder scan first noticed a still-growing file.
        var settledSourceLastWriteUtc = new DateTimeOffset(File.GetLastWriteTimeUtc(fullSourcePath), TimeSpan.Zero);
        if (settledSourceLastWriteUtc != sourceLastWriteUtc)
        {
            var settledDuplicate = await FindExistingImageAsync(
                fullSourcePath,
                settledSourceLastWriteUtc,
                cancellationToken);
            if (settledDuplicate is not null)
            {
                await assetStore.DeleteAsync(stored.RelativePath, CancellationToken.None);
                return settledDuplicate;
            }
        }

        if (settledSourceLastWriteUtc < captureSet.StartedAtUtc)
        {
            await assetStore.DeleteAsync(stored.RelativePath, CancellationToken.None);
            throw new InvalidOperationException("This file predates the selected subject's capture set and was skipped.");
        }

        var sequence = ((await dbContext.CaptureImages.AsNoTracking()
            .Where(image => image.CaptureSetId == captureSet.Id)
            .OrderByDescending(image => image.SequenceNumber)
            .Select(image => (int?)image.SequenceNumber)
            .FirstOrDefaultAsync(cancellationToken)) ?? -1) + 1;
        var asset = new ImageAsset
        {
            Id = stored.ImageAssetId,
            RelativePath = stored.RelativePath,
            SourcePath = fullSourcePath,
            SourceLastWriteUtc = settledSourceLastWriteUtc,
            OriginalFileName = stored.OriginalFileName,
            MediaType = "image/jpeg",
            ByteLength = stored.ByteLength,
            Sha256 = stored.Sha256,
            PixelWidth = stored.PixelWidth,
            PixelHeight = stored.PixelHeight,
            ImportedAtUtc = DateTimeOffset.UtcNow,
            State = ImageAssetState.Available
        };
        var image = new CaptureImage
        {
            CaptureSetId = captureSet.Id,
            ImageAssetId = asset.Id,
            SequenceNumber = sequence,
            CapturedAtUtc = sourceLastWriteUtc
        };
        var auditEntry = new AuditEntry
        {
            EntityType = "capture_image",
            EntityId = image.Id,
            Action = "image.ingested",
            InstallationId = installation.InstallationId,
            StationCode = session.StationCode,
            CorrelationId = session.Id,
            DetailsJson = JsonSerializer.Serialize(new
            {
                sessionId = session.Id,
                captureSetId = captureSet.Id,
                eventId = session.EventId,
                originalFileName = asset.OriginalFileName,
                assetId = asset.Id,
                sha256 = asset.Sha256,
                byteLength = asset.ByteLength
            }),
            OccurredAtUtc = DateTimeOffset.UtcNow
        };

        try
        {
            await captureDataStore.AddImageAsync(image, asset, auditEntry, cancellationToken);
        }
        catch
        {
            await assetStore.DeleteAsync(stored.RelativePath, CancellationToken.None);
            throw;
        }

        return new ImageIngestResult(image.Id, stored.OriginalFileName, stored.Sha256, sequence, false);
    }

    private static bool IsWithinFolder(string filePath, string folderPath)
    {
        var fullFolderPath = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return filePath.StartsWith(fullFolderPath, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ImageIngestResult?> FindExistingImageAsync(
        string sourcePath,
        DateTimeOffset sourceLastWriteUtc,
        CancellationToken cancellationToken)
    {
        var existingAsset = await dbContext.ImageAssets.AsNoTracking()
            .SingleOrDefaultAsync(asset => asset.SourcePath == sourcePath
                && asset.SourceLastWriteUtc == sourceLastWriteUtc, cancellationToken);
        if (existingAsset is null)
            return null;

        var existingImage = await dbContext.CaptureImages.AsNoTracking()
            .SingleOrDefaultAsync(image => image.ImageAssetId == existingAsset.Id, cancellationToken)
            ?? throw new InvalidOperationException("The previously imported image has no capture record.");
        return new ImageIngestResult(
            existingImage.Id,
            existingAsset.OriginalFileName,
            existingAsset.Sha256 ?? string.Empty,
            existingImage.SequenceNumber,
            true);
    }
}
