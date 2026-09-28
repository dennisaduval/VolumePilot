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
}
