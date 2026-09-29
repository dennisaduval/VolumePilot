using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PilotCapture.Domain;
using PilotCapture.Infrastructure.Persistence;
using Xunit;

namespace PilotCapture.Infrastructure.Tests;

public sealed class RosterExportServiceTests
{
    [Fact]
    public async Task Exports_all_active_subjects_and_memberships_without_editing_the_import_source()
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
        var captureEvent = new PilotCapture.Domain.Event { Name = "Spring, 2026", CreatedAtUtc = now };
        var group = new Group { EventId = captureEvent.Id, Name = "Varsity", CreatedAtUtc = now };
        var import = new RosterImport
        {
            EventId = captureEvent.Id,
            SourceFileName = "source-roster.csv",
            SourceSha256 = new string('a', 64),
            ImportedAtUtc = now,
            RowsRead = 1,
            RowsImported = 1
        };
        var rostered = new Subject
        {
            EventId = captureEvent.Id,
            FirstName = "Riley",
            LastName = "Miller",
            DisplayName = "Miller, Riley",
            CreatedAtUtc = now
        };
        var entered = new Subject
        {
            EventId = captureEvent.Id,
            FirstName = "Sam",
            LastName = "Lee",
            DisplayName = "Sam Lee",
            CreatedAtUtc = now
        };
        var unidentified = new Subject
        {
            EventId = captureEvent.Id,
            IdentityStatus = SubjectIdentityStatus.Unidentified,
            DisplayName = "Walk-in 1",
            CreatedAtUtc = now
        };
        var rosteredMembership = new Membership
        {
            SubjectId = rostered.Id,
            GroupId = group.Id,
            RosterImportId = import.Id,
            RosterNumber = "7",
            CreatedAtUtc = now
        };
        var enteredMembership = new Membership
        {
            SubjectId = entered.Id,
            GroupId = group.Id,
            RosterNumber = "12",
            Role = "Senior",
            SourceDataJson = "{\"source\":\"photographer-entry\"}",
            CreatedAtUtc = now
        };
        var sourceRow = new RosterImportRow
        {
            RosterImportId = import.Id,
            SourceRecordNumber = 1,
            MembershipId = rosteredMembership.Id,
            SourceDataJson = "{\"cells\":[\"Riley\",\"Miller\",\"Varsity\"]}"
        };
        dbContext.Events.Add(captureEvent);
        dbContext.Groups.Add(group);
        dbContext.RosterImports.Add(import);
        dbContext.Subjects.AddRange(rostered, entered, unidentified);
        dbContext.Memberships.AddRange(rosteredMembership, enteredMembership);
        dbContext.RosterImportRows.Add(sourceRow);
        await dbContext.SaveChangesAsync(cancellationToken);

        await using var output = new MemoryStream();
        var count = await new RosterExportService(dbContext).ExportEventAsync(captureEvent.Id, output, cancellationToken);
        var csv = Encoding.UTF8.GetString(output.ToArray());

        Assert.Equal(3, count);
        Assert.Contains("Subject ID,Membership ID,First Name,Last Name,Display Name,Identity Status", csv);
        Assert.Contains("\"Miller, Riley\"", csv);
        Assert.Contains("Photographer entered", csv);
        Assert.Contains("Imported roster", csv);
        Assert.Contains("Unidentified", csv);
        Assert.Contains("Walk-in 1", csv);
        Assert.Equal("{\"cells\":[\"Riley\",\"Miller\",\"Varsity\"]}",
            await dbContext.RosterImportRows.Select(row => row.SourceDataJson).SingleAsync(cancellationToken));
    }
}
