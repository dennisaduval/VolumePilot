using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PilotCapture.Application.Capture;
using PilotCapture.Domain;
using PilotCapture.Infrastructure.Persistence;
using Xunit;

namespace PilotCapture.Infrastructure.Tests;

public sealed class CaptureWorkflowServiceTests
{
    [Fact]
    public async Task Next_advances_in_roster_order_and_recovers_the_current_subject_after_restart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<PilotCaptureDbContext>()
            .UseSqlite(connection)
            .Options;

        string sessionId;
        CaptureSetResult firstCaptureSet;
        CaptureSetResult secondCaptureSet;
        await using (var dbContext = new PilotCaptureDbContext(options))
        {
            await new DatabaseInitializer(dbContext).InitializeAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var captureEvent = new PilotCapture.Domain.Event { Name = "Workflow test", CreatedAtUtc = now };
            var group = new Group { EventId = captureEvent.Id, Name = "Test group", CreatedAtUtc = now };
            var roster = new[]
            {
                (First: "Zoe", Last: "Zulu"),
                (First: "Alex", Last: "Alpha"),
                (First: "Morgan", Last: "Miller")
            };
            foreach (var person in roster)
            {
                var subject = new Subject
                {
                    EventId = captureEvent.Id,
                    FirstName = person.First,
                    LastName = person.Last,
                    DisplayName = $"{person.First} {person.Last}",
                    CreatedAtUtc = now
                };
                var membership = new Membership
                {
                    SubjectId = subject.Id,
                    GroupId = group.Id,
                    CreatedAtUtc = now
                };
                dbContext.Subjects.Add(subject);
                dbContext.Memberships.Add(membership);
            }
            dbContext.Events.Add(captureEvent);
            dbContext.Groups.Add(group);
            await dbContext.SaveChangesAsync(cancellationToken);

            var workflow = new CaptureWorkflowService(dbContext);
            Assert.Contains(await workflow.GetEventsAsync(cancellationToken), item => item.Id == captureEvent.Id);
            var session = await workflow.StartSessionAsync(
                captureEvent.Id,
                "Test Photographer",
                CaptureWorkflowType.Portrait,
                cancellationToken);
            sessionId = session.Id;

            var displayedRoster = await workflow.GetSubjectsAsync(group.Id, cancellationToken);
            Assert.Equal(
                new[] { "Alex Alpha", "Morgan Miller", "Zoe Zulu" },
                displayedRoster.Select(subject => subject.DisplayName));

            firstCaptureSet = await workflow.SelectSubjectAsync(
                session.Id,
                displayedRoster[0].MembershipId,
                cancellationToken);
            secondCaptureSet = Assert.IsType<CaptureSetResult>(await workflow.NextSubjectAsync(session.Id, cancellationToken));
            Assert.Equal("Morgan Miller", secondCaptureSet.SubjectName);
            Assert.NotEqual(firstCaptureSet.CaptureSetId, secondCaptureSet.CaptureSetId);
        }

        await using (var restartedContext = new PilotCaptureDbContext(options))
        {
            var recovered = await new CaptureWorkflowService(restartedContext).GetActiveSessionAsync(cancellationToken);
            Assert.NotNull(recovered);
            Assert.Equal(sessionId, recovered.Id);
            Assert.Equal(secondCaptureSet.CaptureSetId, recovered.CurrentCaptureSet?.CaptureSetId);
            Assert.Equal("Morgan Miller", recovered.CurrentCaptureSet?.SubjectName);
        }
    }

    [Fact]
    public async Task Photographer_can_add_a_partially_identified_subject_and_capture_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<PilotCaptureDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new PilotCaptureDbContext(options);
        await new DatabaseInitializer(dbContext).InitializeAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var captureEvent = new PilotCapture.Domain.Event { Name = "Manual subject test", CreatedAtUtc = now };
        var group = new Group { EventId = captureEvent.Id, Name = "Varsity", CreatedAtUtc = now };
        dbContext.Events.Add(captureEvent);
        dbContext.Groups.Add(group);
        await dbContext.SaveChangesAsync(cancellationToken);

        var workflow = new CaptureWorkflowService(dbContext);
        var session = await workflow.StartSessionAsync(
            captureEvent.Id, "Test Photographer", CaptureWorkflowType.Portrait, cancellationToken);
        var captureSet = await workflow.CreateManualSubjectAsync(
            session.Id,
            new CaptureSubjectDetails("Riley", null, "42", "Junior"),
            group.Id,
            cancellationToken);

        var subject = await dbContext.Subjects.SingleAsync(cancellationToken);
        var membership = await dbContext.Memberships.SingleAsync(cancellationToken);
        Assert.Equal(SubjectIdentityStatus.Known, subject.IdentityStatus);
        Assert.Equal("Riley", subject.FirstName);
        Assert.Null(subject.LastName);
        Assert.Equal("Riley", subject.DisplayName);
        Assert.Equal("42", membership.RosterNumber);
        Assert.Equal("Junior", membership.Role);
        Assert.Null(membership.RosterImportId);
        Assert.Equal("{\"source\":\"photographer-entry\"}", membership.SourceDataJson);
        var capturedSubjectId = await dbContext.CaptureSets
            .Where(item => item.Id == captureSet.CaptureSetId)
            .Select(item => item.SubjectId)
            .SingleAsync(cancellationToken);
        Assert.Equal(subject.Id, capturedSubjectId);
        Assert.Equal(group.Id, captureSet.GroupId);
    }

    [Fact]
    public async Task Manual_subject_requires_a_name_and_does_not_replace_the_unidentified_option()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<PilotCaptureDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new PilotCaptureDbContext(options);
        await new DatabaseInitializer(dbContext).InitializeAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var captureEvent = new PilotCapture.Domain.Event { Name = "Identity test", CreatedAtUtc = now };
        dbContext.Events.Add(captureEvent);
        await dbContext.SaveChangesAsync(cancellationToken);
        var workflow = new CaptureWorkflowService(dbContext);
        var session = await workflow.StartSessionAsync(
            captureEvent.Id, "Test Photographer", CaptureWorkflowType.Portrait, cancellationToken);

        await Assert.ThrowsAsync<ArgumentException>(() => workflow.CreateManualSubjectAsync(
            session.Id, new CaptureSubjectDetails(null, null, null, null), null, cancellationToken));
        var unknownCapture = await workflow.CreateUnidentifiedSubjectAsync(
            session.Id, "Walk-in 1", null, cancellationToken);

        var unknown = await dbContext.Subjects.SingleAsync(cancellationToken);
        Assert.Equal(SubjectIdentityStatus.Unidentified, unknown.IdentityStatus);
        Assert.True(unknownCapture.IsUnidentified);
        Assert.Equal("Walk-in 1", unknown.DisplayName);
        Assert.Empty(await dbContext.Memberships.ToListAsync(cancellationToken));
    }
}
