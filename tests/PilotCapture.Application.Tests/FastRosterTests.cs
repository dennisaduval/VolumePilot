using System.Text;
using PilotCapture.Application.FastCapture;
using PilotCapture.Application.Rosters;
using PilotCapture.Domain;
using Xunit;

namespace PilotCapture.Application.Tests;

public sealed class FastRosterTests
{
    [Fact]
    public void Header_selection_keeps_quoted_records_duplicate_headers_and_extra_fields()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Event title\r\nName,Name,\r\n\"Rockets\nBlue\",Junior,,extra\r\n"));
        var raw = RosterCsvReader.Read(stream);
        var document = FastRosterColumns.WithHeader(raw, 2);
        Assert.Equal(new[] { "Name", "Name", "" }, document.Headers);
        var row = Assert.Single(document.Rows);
        Assert.Equal("Rockets\nBlue", row.Cells[0].Value); Assert.Equal("extra", row.Cells[3].Value);
        Assert.Equal(4, FastRosterColumns.ColumnCount(document)); Assert.Equal("Column 4", FastRosterColumns.Label(document, 3));
        Assert.Equal("Event title", raw.Headers[0]); Assert.Equal(2, raw.Rows.Count);
        Assert.Throws<ArgumentException>(() => FastRosterColumns.WithHeader(raw, 0));
        Assert.Throws<ArgumentException>(() => FastRosterColumns.WithHeader(raw, 4));
    }

    [Fact]
    public void Search_matches_partial_names_numbers_and_multiple_division_details()
    {
        FastCaptureTeam[] teams = [
            new() { Name = "Rockets", PerformanceLabel = "101", Details = "Club: A · Division: Junior", SortOrder = 1 },
            new() { Name = "Rockets", PerformanceLabel = "102", Details = "Club: A · Division: Senior", SortOrder = 2 },
            new() { Name = "Stars", PerformanceLabel = "103", Details = "Club: B · Division: Senior", SortOrder = 3 }];
        Assert.Equal(2, FastPerformanceSearch.Find(teams, "rock").Count);
        Assert.Same(teams[1], Assert.Single(FastPerformanceSearch.Find(teams, "ROCK sen")));
        Assert.Same(teams[1], Assert.Single(FastPerformanceSearch.Find(teams, "102")));
        Assert.Equal(3, FastPerformanceSearch.Find(teams, "10").Count);
        Assert.Empty(FastPerformanceSearch.Find(teams, "unknown"));
        Assert.Equal(teams, FastPerformanceSearch.Find(teams, ""));
        Assert.Contains("Junior", FastPerformanceSearch.Label(teams[0]));
    }
}
