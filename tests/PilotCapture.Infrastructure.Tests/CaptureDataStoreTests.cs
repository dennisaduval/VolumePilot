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

        var auditEntries = await dbContext.AuditEntries.OrderBy(entry => entry.OccurredAtUtc).ToListAsync(cancellationToken);
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
        Assert.Contains(",False\n", csv);
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
