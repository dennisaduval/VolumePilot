using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PilotCapture.Application.Capture;
using PilotCapture.Application.Rosters;
using PilotCapture.Domain;
using PilotCapture.Infrastructure.Persistence;
using Xunit;

namespace PilotCapture.Infrastructure.Tests;

public sealed class TeamVolumeTests
{
    [Fact]
    public async Task Full_name_roster_keeps_same_named_teams_and_allows_walk_ins()
    {
        var token = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(token);
        await using var db = new PilotCaptureDbContext(new DbContextOptionsBuilder<PilotCaptureDbContext>().UseSqlite(connection).Options);
        await new DatabaseInitializer(db).InitializeAsync(token);

        const string csv = "Name,Grade,School,Team,Role\r\n"
            + "\"Alex Jones, Jr.\",Senior,North High,Tigers,Player\r\n"
            + "\"Alex Jones, Jr.\",Junior,South High,Tigers,Player\r\n"
            + ",,North High,Tigers,Head Coach\r\n";
        var bytes = Encoding.UTF8.GetBytes(csv);
        using var input = new MemoryStream(bytes);
        var document = RosterCsvReader.Read(input);
        var suggestions = RosterColumnSuggestionBuilder.Build(document.Headers);
        var mapping = new RosterColumnMapping(null, null,
            RosterColumnSuggestionBuilder.FindTeam(document.Headers),
            RosterColumnSuggestionBuilder.FindTeamContext(document.Headers), null, suggestions.ClassOrCategoryColumn,
            RosterColumnSuggestionBuilder.FindFullName(document.Headers), TeamVolume: true);
        var rows = RosterImportPreviewBuilder.Build(document, mapping);
        var imported = await new RosterImportService(db).ImportAsync(new RosterImportCommand(
            "Basketball", "sample.csv", RosterImportService.ComputeSha256(bytes), rows, new HashSet<string>(), AllowDisplayNameOnly: true), token);
        Assert.Equal(2, imported.RowsImported);
        Assert.Equal(1, imported.RowsSkipped);
        Assert.Equal(3, await db.RosterImportRows.CountAsync(token));
        var workflow = new CaptureWorkflowService(db);
        var teams = await workflow.GetGroupsAsync(imported.EventId, token);
        Assert.Equal(new[] { "Tigers / North High", "Tigers / South High" }, teams.Select(x => x.Name));
        var session = await workflow.StartSessionAsync(imported.EventId, "Photographer", CaptureWorkflowType.Portrait, token);
        var matches = await workflow.SearchSubjectsAsync(imported.EventId, "alex", token);
        Assert.Equal(2, matches.Count);
        Assert.All(matches, x => Assert.Equal("Alex Jones, Jr.", x.DisplayName));
        var south = Assert.Single(matches, x => x.GroupName == "Tigers / South High");
        var selected = await workflow.SelectSubjectAsync(session.Id, south.MembershipId, token);
        Assert.Equal(teams[1].Id, selected.GroupId);
        Assert.Equal("Junior · Player", south.Role);
        var newTeam = await workflow.AddTeamAsync(session.Id, "Tigers", "JV", token);
        Assert.Null((await workflow.GetActiveSessionAsync(token))!.CurrentCaptureSet);
        var walkIn = await workflow.CreateManualSubjectAsync(session.Id, new CaptureSubjectDetails("Robin", "Lee", "12", "Player"), newTeam, token);
        Assert.Equal(newTeam, walkIn.GroupId);
        Assert.Equal("Tigers / JV", Assert.Single(await workflow.SearchSubjectsAsync(imported.EventId, "robin", token)).GroupName);
        await workflow.CompleteSubjectAsync(session.Id, token);
        Assert.Null((await workflow.GetActiveSessionAsync(token))!.CurrentCaptureSet);
    }
}
