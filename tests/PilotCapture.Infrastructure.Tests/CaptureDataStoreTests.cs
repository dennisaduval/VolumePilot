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
        Assert.True(second.Image.IsSecondary);
        Assert.Equal(CaptureImageReviewState.Accepted, second.Image.ReviewState);

        await store.ApplyReviewActionAsync(second.Image.Id, CaptureImageReviewAction.ToggleBanner, cancellationToken);
        await store.ApplyReviewActionAsync(first.Image.Id, CaptureImageReviewAction.ToggleBanner, cancellationToken);
        Assert.True(first.Image.IsPrimary);
        Assert.True(first.Image.IsBanner);
        Assert.False(second.Image.IsBanner);

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

    [Fact]
    public async Task Roles_persist_across_visits_toggle_rejection_and_remain_unique()
    {
        var token = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(token);
        var options = new DbContextOptionsBuilder<PilotCaptureDbContext>().UseSqlite(connection).Options;
        await using var db = new PilotCaptureDbContext(options);
        await new DatabaseInitializer(db).InitializeAsync(token);
        var set = await CreateCaptureSetAsync(db, token);
        var installation = await db.LocalInstallations.Select(x => x.InstallationId).SingleAsync(token);
        var store = new CaptureDataStore(db);
        var first = CreateImage(set.Id, 0);
        await store.AddImageAsync(first.Image, first.Asset, CreateAuditEntry(first.Image, installation), token);
        Assert.True(first.Image.IsPrimary && first.Image.IsSecondary && first.Image.IsBanner);
        var second = CreateImage(set.Id, 1);
        await store.AddImageAsync(second.Image, second.Asset, CreateAuditEntry(second.Image, installation), token);
        Assert.True(first.Image.IsPrimary && first.Image.IsBanner);
        Assert.False(first.Image.IsSecondary);
        Assert.True(second.Image.IsSecondary);
        await store.ApplyReviewActionAsync(second.Image.Id, CaptureImageReviewAction.SetPrimary, token);
        Assert.True(first.Image.IsSecondary && second.Image.IsPrimary);
        var repeatedSet = new CaptureSet { CaptureSessionId = set.CaptureSessionId, SubjectId = set.SubjectId, MembershipId = set.MembershipId, StartedAtUtc = DateTimeOffset.UtcNow };
        db.CaptureSets.Add(repeatedSet);
        await db.SaveChangesAsync(token);
        var third = CreateImage(repeatedSet.Id, 2);
        await store.AddImageAsync(third.Image, third.Asset, CreateAuditEntry(third.Image, installation), token);
        Assert.True(second.Image.IsPrimary && first.Image.IsSecondary && first.Image.IsBanner);
        var review = new ImageReviewService(db, store);
        Assert.Equal(3, (await review.GetImagesAsync(repeatedSet.Id, token)).Count);
        await store.ApplyReviewActionAsync(first.Image.Id, CaptureImageReviewAction.Reject, token);
        Assert.True(second.Image.IsPrimary && third.Image.IsSecondary && second.Image.IsBanner);
        await store.ApplyReviewActionAsync(first.Image.Id, CaptureImageReviewAction.Reject, token);
        Assert.NotEqual(CaptureImageReviewState.Rejected, first.Image.ReviewState);
        Assert.True(second.Image.IsPrimary && third.Image.IsSecondary && second.Image.IsBanner);
        await store.ApplyReviewActionAsync(third.Image.Id, CaptureImageReviewAction.ToggleBanner, token);
        Assert.True(third.Image.IsBanner);
        Assert.False(second.Image.IsBanner);
        await store.ApplyReviewActionAsync(second.Image.Id, CaptureImageReviewAction.Reject, token);
        await store.ApplyReviewActionAsync(first.Image.Id, CaptureImageReviewAction.Reject, token);
        Assert.True(third.Image.IsPrimary && third.Image.IsSecondary && third.Image.IsBanner);
        await store.ApplyReviewActionAsync(third.Image.Id, CaptureImageReviewAction.Reject, token);
        Assert.All(await review.GetImagesAsync(repeatedSet.Id, token), x => Assert.False(x.IsPrimary || x.IsSecondary || x.IsBanner));
        db.ChangeTracker.Clear();
        await new DatabaseInitializer(db).InitializeAsync(token);
        Assert.Equal(3, (await review.GetImagesAsync(repeatedSet.Id, token)).Count);
    }

    [Fact]
    public async Task Team_exports_pair_pngs_use_collision_safe_names_and_exclude_rejected_images()
    {
        var token = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(token);
        await using var db = new PilotCaptureDbContext(new DbContextOptionsBuilder<PilotCaptureDbContext>().UseSqlite(connection).Options);
        await new DatabaseInitializer(db).InitializeAsync(token);
        var set = await CreateCaptureSetAsync(db, token);
        var membership = await db.Memberships.SingleAsync(token);
        membership.RosterNumber = "07";
        membership.SpaDataJson = "{\"POSITION\":\"Pitcher\",\"LEAGUENAME\":\"League A\",\"SPATEXT1\":\"Hello, team\"}";
        await db.SaveChangesAsync(token);
        var installation = await db.LocalInstallations.Select(x => x.InstallationId).SingleAsync(token);
        var root = Path.Combine(Path.GetTempPath(), "pilot-export-" + Guid.NewGuid().ToString("N"));
        var media = Path.Combine(root, "media");
        var master = Path.Combine(root, "master");
        Directory.CreateDirectory(master);
        try
        {
            var store = new CaptureDataStore(db);
            var images = new List<(CaptureImage Image, ImageAsset Asset)>();
            for (var n = 0; n < 3; n++)
            {
                var pair = CreateImage(set.Id, n);
                var bytes = new byte[] { 255, 216, 255, (byte)n };
                pair.Asset.Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
                var file = Path.Combine(media, pair.Asset.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await File.WriteAllBytesAsync(file, bytes, token);
                await store.AddImageAsync(pair.Image, pair.Asset, CreateAuditEntry(pair.Image, installation), token);
                images.Add(pair);
            }
            await store.ApplyReviewActionAsync(images[2].Image.Id, CaptureImageReviewAction.Reject, token);
            var exporter = new JobMediaService(db, new FileSystemImageAssetStore(media));
            var eventId = await db.Events.Select(x => x.Id).SingleAsync(token);
            await exporter.SetMasterPathAsync(master, token);
            Assert.Equal(3, (await exporter.PublishOriginalsAsync(eventId, token)).Images);
            Assert.Equal(3, (await exporter.PublishOriginalsAsync(eventId, token)).Images);
            var batch = await exporter.ExportAsync(eventId, root, JobImageExportKind.BatchEditingOriginals, token);
            Assert.Equal(2, Directory.GetFiles(Path.Combine(batch.Location, "original photos"), "*.jpg").Length);
            Assert.True(File.Exists(Path.Combine(batch.Location, "original photos", images[0].Asset.Id + ".jpg")));
            var before = Directory.GetDirectories(root).Length;
            await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync(eventId, root, JobImageExportKind.EditedPng, token));
            Assert.Equal(before, Directory.GetDirectories(root).Length);
            foreach (var pair in images.Take(2))
                await File.WriteAllBytesAsync(Path.Combine(master, "Extracted Images", eventId, pair.Asset.Id + ".png"), [137,80,78,71,13,10,26,10,1], token);
            Assert.Equal(2, (await exporter.AssociateEditedAsync(eventId, token)).Images);
            var result = await exporter.ExportAsync(eventId, root, JobImageExportKind.EditedPng, token);
            var all = Path.Combine(result.Location, "All Images", "Test team");
            var primary = Path.Combine(result.Location, "Team Photo Images", "Test team");
            Assert.Equal(2, Directory.GetFiles(all, "*.png").Length);
            Assert.Single(Directory.GetFiles(primary, "*.png"));
            Assert.True(File.Exists(Path.Combine(all, "Alex_Example_Test team_07.png")));
            Assert.True(File.Exists(Path.Combine(all, "Alex_Example_Test team_07_02.png")));
            var csv = await File.ReadAllTextAsync(Path.Combine(all, "SPA.csv"), token);
            Assert.Contains("POSITION", csv);
            Assert.Contains("LEAGUENAME", csv);
            Assert.Contains("SPATEXT1", csv);
            Assert.Contains("\"Hello, team\"", csv);
            foreach (var path in Directory.GetFiles(all, "*.png")) Assert.Contains(Path.GetFileName(path), csv);
            Assert.Equal("_CON", JobMediaService.SafeName("CON"));
            Assert.DoesNotContain('/', JobMediaService.SafeName("../bad/team"));
        }
        finally { Directory.Delete(root, true); }
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

