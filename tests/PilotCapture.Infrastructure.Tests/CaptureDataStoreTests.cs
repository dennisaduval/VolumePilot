using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PilotCapture.Application;
using PilotCapture.Domain;
using PilotCapture.Infrastructure.Files;
using PilotCapture.Infrastructure.Persistence;
using Xunit;

namespace PilotCapture.Infrastructure.Tests;

public sealed class CaptureDataStoreTests
{
    [Fact]
    public async Task Smart_shooter_ingest_is_idempotent_and_leaves_the_source_file_untouched()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<PilotCaptureDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new PilotCaptureDbContext(options);
        await new DatabaseInitializer(dbContext).InitializeAsync(cancellationToken);

        var captureSet = await CreateCaptureSetAsync(dbContext, cancellationToken);
        captureSet.StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-10);
        var session = await dbContext.CaptureSessions.SingleAsync(cancellationToken);
        var installation = await dbContext.LocalInstallations.SingleAsync(cancellationToken);

        var root = Path.Combine(Path.GetTempPath(), $"pilot-capture-ingest-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(root, "SmartShooter");
        var mediaDirectory = Path.Combine(root, "ManagedMedia");
        Directory.CreateDirectory(sourceDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        var sourceBytes = new byte[]
        {
            0xFF, 0xD8,
            0xFF, 0xE0, 0x00, 0x10,
            0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
            0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x0E, 0x10, 0x0A, 0x28, 0x01, 0x01, 0x11, 0x00,
            0xFF, 0xD9
        };
        await File.WriteAllBytesAsync(sourcePath, sourceBytes, cancellationToken);
        installation.SmartShooterOutputPath = sourceDirectory;
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var assetStore = new FileSystemImageAssetStore(mediaDirectory);
            var ingest = new ImageIngestService(dbContext, assetStore, new CaptureDataStore(dbContext));

            var first = await ingest.ImportJpegAsync(session.Id, captureSet.Id, sourcePath, cancellationToken);
            var repeated = await ingest.ImportJpegAsync(session.Id, captureSet.Id, sourcePath, cancellationToken);

            Assert.False(first.AlreadyImported);
            Assert.True(repeated.AlreadyImported);
            Assert.Equal(first.CaptureImageId, repeated.CaptureImageId);
            Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(sourcePath, cancellationToken));

            var image = await dbContext.CaptureImages.Include(item => item.ImageAsset)
                .SingleAsync(cancellationToken);
            Assert.Equal(captureSet.Id, image.CaptureSetId);
            Assert.Equal("IMG_0001.JPG", image.ImageAsset!.OriginalFileName);
            Assert.Equal(2600, image.ImageAsset.PixelWidth);
            Assert.Equal(3600, image.ImageAsset.PixelHeight);
            Assert.True(assetStore.Exists(image.ImageAsset.RelativePath));
            Assert.Equal(1, await dbContext.AuditEntries.CountAsync(cancellationToken));

            var reviewItems = await new ImageReviewService(dbContext, new CaptureDataStore(dbContext))
                .GetImagesAsync(captureSet.Id, cancellationToken);
            var reviewItem = Assert.Single(reviewItems);
            Assert.Equal(2600, reviewItem.PixelWidth);
            Assert.Equal(3600, reviewItem.PixelHeight);
            Assert.Equal((long)sourceBytes.Length, reviewItem.ByteLength);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Smart_shooter_ingest_skips_files_that_predate_the_selected_capture_set()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<PilotCaptureDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new PilotCaptureDbContext(options);
        await new DatabaseInitializer(dbContext).InitializeAsync(cancellationToken);

        var captureSet = await CreateCaptureSetAsync(dbContext, cancellationToken);
        captureSet.StartedAtUtc = DateTimeOffset.UtcNow;
        var session = await dbContext.CaptureSessions.SingleAsync(cancellationToken);
        var installation = await dbContext.LocalInstallations.SingleAsync(cancellationToken);
        var root = Path.Combine(Path.GetTempPath(), $"pilot-capture-stale-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(root, "SmartShooter");
        Directory.CreateDirectory(sourceDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "IMG_0000.JPG");
        await File.WriteAllBytesAsync(sourcePath, [0xFF, 0xD8, 0xFF, 0xE0], cancellationToken);
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-1));
        installation.SmartShooterOutputPath = sourceDirectory;
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var ingest = new ImageIngestService(
                dbContext,
                new FileSystemImageAssetStore(Path.Combine(root, "ManagedMedia")),
                new CaptureDataStore(dbContext));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ingest.ImportJpegAsync(session.Id, captureSet.Id, sourcePath, cancellationToken));

            Assert.True(File.Exists(sourcePath));
            Assert.Empty(await dbContext.CaptureImages.ToListAsync(cancellationToken));
            Assert.Empty(await dbContext.AuditEntries.ToListAsync(cancellationToken));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task First_image_becomes_primary_and_review_roles_remain_independent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<PilotCaptureDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new PilotCaptureDbContext(options);
        await new DatabaseInitializer(dbContext).InitializeAsync(cancellationToken);

        var captureSet = await CreateCaptureSetAsync(dbContext, cancellationToken);
        var installationId = await dbContext.LocalInstallations.Select(item => item.InstallationId).SingleAsync(cancellationToken);
        var store = new CaptureDataStore(dbContext);
        var first = CreateImage(captureSet.Id, 0);
        await store.AddImageAsync(first.Image, first.Asset, CreateAuditEntry(first.Image, installationId), cancellationToken);
        Assert.True(first.Image.IsPrimary);
        Assert.Equal(CaptureImageReviewState.Accepted, first.Image.ReviewState);

        var second = CreateImage(captureSet.Id, 1);
        await store.AddImageAsync(second.Image, second.Asset, CreateAuditEntry(second.Image, installationId), cancellationToken);
        Assert.False(second.Image.IsPrimary);
        Assert.Equal(CaptureImageReviewState.Pending, second.Image.ReviewState);

        await store.ApplyReviewActionAsync(second.Image.Id, CaptureImageReviewAction.ToggleBanner, cancellationToken);
        await store.ApplyReviewActionAsync(first.Image.Id, CaptureImageReviewAction.ToggleBanner, cancellationToken);
        Assert.True(first.Image.IsPrimary);
        Assert.True(first.Image.IsBanner);
        Assert.True(second.Image.IsBanner);

        await store.ApplyReviewActionAsync(first.Image.Id, CaptureImageReviewAction.Reject, cancellationToken);
        Assert.Equal(CaptureImageReviewState.Rejected, first.Image.ReviewState);
        Assert.False(first.Image.IsPrimary);
        Assert.False(first.Image.IsBanner);
        Assert.True(second.Image.IsPrimary);
        Assert.True(second.Image.IsBanner);
        Assert.Equal(CaptureImageReviewState.Accepted, second.Image.ReviewState);

        var auditEntries = await dbContext.AuditEntries.ToListAsync(cancellationToken);
        Assert.Equal(2, auditEntries.Count);
        Assert.All(auditEntries, entry => Assert.Equal("image.ingested", entry.Action));
        Assert.Contains(auditEntries, entry => entry.EntityId == first.Image.Id);

        var eventId = await dbContext.CaptureSets
            .Where(item => item.Id == captureSet.Id)
            .Select(item => item.CaptureSession!.EventId)
            .SingleAsync(cancellationToken);
        await using var export = new MemoryStream();
        var exporter = new ImageAssociationExportService(
            dbContext,
            new FileSystemImageAssetStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var exportedRows = await exporter.ExportEventAsync(eventId, export, cancellationToken);
        var csv = System.Text.Encoding.UTF8.GetString(export.ToArray());
        Assert.Equal(2, exportedRows);
        Assert.Contains("Test team", csv);
        Assert.Contains("Alex Example", csv);
        Assert.Contains("File Exists", csv);
        Assert.Contains(",False", csv);
    }

    private static async Task<CaptureSet> CreateCaptureSetAsync(
        PilotCaptureDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var captureEvent = new PilotCapture.Domain.Event { Name = "Review test", CreatedAtUtc = now };
        var group = new Group { EventId = captureEvent.Id, Name = "Test team", CreatedAtUtc = now };
        var subject = new Subject
        {
            EventId = captureEvent.Id,
            FirstName = "Alex",
            LastName = "Example",
            DisplayName = "Alex Example",
            CreatedAtUtc = now
        };
        var membership = new Membership
        {
            SubjectId = subject.Id,
            GroupId = group.Id,
            CreatedAtUtc = now
        };
        var profile = new CaptureProfile
        {
            Name = "Portrait",
            WorkflowType = CaptureWorkflowType.Portrait,
            CreatedAtUtc = now
        };
        var photographer = new Photographer { DisplayName = "Test Photographer", CreatedAtUtc = now };
        dbContext.Events.Add(captureEvent);
        dbContext.Groups.Add(group);
        dbContext.Subjects.Add(subject);
        dbContext.Memberships.Add(membership);
        dbContext.CaptureProfiles.Add(profile);
        dbContext.Photographers.Add(photographer);
        await dbContext.SaveChangesAsync(cancellationToken);

        var installation = await dbContext.LocalInstallations.SingleAsync(cancellationToken);
        var session = new CaptureSession
        {
            EventId = captureEvent.Id,
            CaptureProfileId = profile.Id,
            PhotographerId = photographer.Id,
            InstallationId = installation.InstallationId,
            StationCode = installation.StationCode,
            StartedAtUtc = now
        };
        dbContext.CaptureSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        var captureSet = new CaptureSet
        {
            CaptureSessionId = session.Id,
            SubjectId = subject.Id,
            MembershipId = membership.Id,
            StartedAtUtc = now
        };
        dbContext.CaptureSets.Add(captureSet);
        await dbContext.SaveChangesAsync(cancellationToken);
        return captureSet;
    }

    private static (CaptureImage Image, ImageAsset Asset) CreateImage(string captureSetId, int sequenceNumber)
    {
        var now = DateTimeOffset.UtcNow;
        var asset = new ImageAsset
        {
            RelativePath = $"event/session/set/{sequenceNumber}.jpg",
            OriginalFileName = $"IMG_{sequenceNumber:0001}.JPG",
            ByteLength = 128,
            Sha256 = new string((char)('a' + sequenceNumber), 64),
            ImportedAtUtc = now,
            State = ImageAssetState.Available
        };
        var image = new CaptureImage
        {
            CaptureSetId = captureSetId,
            ImageAssetId = asset.Id,
            SequenceNumber = sequenceNumber,
            CapturedAtUtc = now
        };
        return (image, asset);
    }

    private static AuditEntry CreateAuditEntry(CaptureImage image, string installationId)
    {
        return new AuditEntry
        {
            EntityType = "capture_image",
            EntityId = image.Id,
            Action = "image.ingested",
            InstallationId = installationId,
            StationCode = "s10",
            OccurredAtUtc = DateTimeOffset.UtcNow
        };
    }
}
