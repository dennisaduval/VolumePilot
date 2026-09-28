using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PilotCapture.Application;
using PilotCapture.Domain;

namespace PilotCapture.Infrastructure.Persistence;

public sealed class ImageAssociationExportService(
    PilotCaptureDbContext dbContext,
    IImageAssetStore assetStore) : IImageAssociationExportService
{
    private static readonly string[] Headers =
    [
        "Event", "Session ID", "Session Started UTC", "Photographer", "Station",
        "Capture Set ID", "Subject ID", "Subject", "Group", "Membership ID",
        "Roster Number", "Role", "Capture Image ID", "Sequence", "Captured UTC",
        "Review State", "Primary", "Banner", "Original Filename", "Managed Relative Path",
        "SHA-256", "Bytes", "Asset State", "File Exists"
    ];

    public async Task<int> ExportEventAsync(
        string eventId,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException("The export destination must be writable.", nameof(destination));

        var records = await dbContext.CaptureImages.AsNoTracking()
            .Where(image => image.CaptureSet!.CaptureSession!.EventId == eventId)
            .Select(image => new ExportRecord(
                image.CaptureSet!.CaptureSession!.Event!.Name,
                image.CaptureSet.CaptureSession.Id,
                image.CaptureSet.CaptureSession.StartedAtUtc,
                image.CaptureSet.StartedAtUtc,
                image.CaptureSet.CaptureSession.Photographer!.DisplayName,
                image.CaptureSet.CaptureSession.StationCode,
                image.CaptureSet.Id,
                image.CaptureSet.Subject!.Id,
                image.CaptureSet.Subject.DisplayName,
                image.CaptureSet.Membership!.Group!.Name,
                image.CaptureSet.MembershipId,
                image.CaptureSet.Membership!.RosterNumber,
                image.CaptureSet.Membership!.Role,
                image.Id,
                image.SequenceNumber,
                image.CapturedAtUtc,
                (int)image.ReviewState,
                image.IsPrimary,
                image.IsBanner,
                image.ImageAsset!.OriginalFileName,
                image.ImageAsset.RelativePath,
                image.ImageAsset.Sha256,
                image.ImageAsset.ByteLength,
                (int)image.ImageAsset.State))
            .ToListAsync(cancellationToken);
        records = records
            .OrderBy(record => record.SessionStartedAtUtc)
            .ThenBy(record => record.CaptureSetStartedAtUtc)
            .ThenBy(record => record.SequenceNumber)
            .ToList();

        await using var writer = new StreamWriter(destination, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 64 * 1024, leaveOpen: true);
        await writer.WriteLineAsync(string.Join(",", Headers.Select(Escape)));
        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = new string?[]
            {
                record.EventName,
                record.SessionId,
                record.SessionStartedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                record.PhotographerName,
                record.StationCode,
                record.CaptureSetId,
                record.SubjectId,
                record.SubjectName,
                record.GroupName,
                record.MembershipId,
                record.RosterNumber,
                record.Role,
                record.CaptureImageId,
                record.SequenceNumber.ToString(CultureInfo.InvariantCulture),
                record.CapturedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                Enum.GetName(typeof(CaptureImageReviewState), record.ReviewState),
                record.IsPrimary.ToString(CultureInfo.InvariantCulture),
                record.IsBanner.ToString(CultureInfo.InvariantCulture),
                record.OriginalFileName,
                record.RelativePath,
                record.Sha256,
                record.ByteLength.ToString(CultureInfo.InvariantCulture),
                Enum.GetName(typeof(ImageAssetState), record.AssetState),
                assetStore.Exists(record.RelativePath).ToString(CultureInfo.InvariantCulture)
            };
            await writer.WriteLineAsync(string.Join(",", values.Select(Escape)));
        }

        await writer.FlushAsync(cancellationToken);
        return records.Count;
    }

    private static string Escape(string? value)
    {
        value ??= string.Empty;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
    }

    private sealed record ExportRecord(
        string EventName,
        string SessionId,
        DateTimeOffset SessionStartedAtUtc,
        DateTimeOffset CaptureSetStartedAtUtc,
        string PhotographerName,
        string StationCode,
        string CaptureSetId,
        string SubjectId,
        string SubjectName,
        string? GroupName,
        string? MembershipId,
        string? RosterNumber,
        string? Role,
        string CaptureImageId,
        int SequenceNumber,
        DateTimeOffset CapturedAtUtc,
        int ReviewState,
        bool IsPrimary,
        bool IsBanner,
        string OriginalFileName,
        string RelativePath,
        string? Sha256,
        long ByteLength,
        int AssetState);
}
