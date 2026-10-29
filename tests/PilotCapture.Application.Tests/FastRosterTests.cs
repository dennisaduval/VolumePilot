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
        Assert.Equal("101 · Rockets · A · Junior", FastPerformanceSearch.Label(teams[0]));
        Assert.DoesNotContain("Division:", FastPerformanceSearch.Label(teams[0]));
    }

    [Fact]
    public void Schedule_plan_maps_arbitrary_columns_keeps_appearances_and_ignores_times_and_breaks()
    {
        var bytes = Encoding.UTF8.GetBytes("Event title\nWhen,Where,Run,Squad,Organization,Age band,Kind,Time\nSaturday,F,10,Rockets,Club A,Junior,Team,9:00 AM\nSaturday,F,2,Stars,Club B,Senior,Team,8:00 AM\nSaturday,F,3,Break,,,Break,8:05 AM\nSunday,F,2,Rockets,Club A,Junior,Team,7:00 AM\nSaturday,G,2,Rockets,Club A,Junior,Team,7:00 AM\nWhen,Where,Run,Squad,Organization,Age band,Kind,Time\n");
        var command = new FastRosterImportCommand("job", "schedule.csv", bytes, 3, null, 2, [4, 5], 2, 0, 1, 6);
        var plan = FastRosterPlan.Read(command);
        Assert.Equal(4, plan.Entries.Count);
        Assert.Equal(new[] { "2", "10" }, plan.Entries.Where(e => e.Day == "Saturday" && e.Hall == "F").Select(e => e.Order));
        Assert.Equal(3, plan.Entries.Where(e => e.Name == "Rockets").Select(e => e.Key).Distinct().Count());
        Assert.Equal("Event title", plan.Raw.Headers[0]); Assert.Equal(6, plan.Rows);
        var changedTimes = command with { Content = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("9:00 AM", "10:00 AM")) };
        Assert.Equal(plan.Entries.Select(e => e.Key), FastRosterPlan.Read(changedTimes).Entries.Select(e => e.Key));
        Assert.Throws<ArgumentException>(() => FastRosterPlan.Read(command with { HallColumn = 0 }));
        Assert.Throws<ArgumentException>(() => FastRosterPlan.Read(command with { Content = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace(",Break,8:", ",Mystery,8:")) }));
    }

    [Fact]
    public void Each_hall_orders_independently_and_search_stays_within_the_selected_schedule()
    {
        var command = new FastRosterImportCommand("job", "schedule.csv", Encoding.UTF8.GetBytes("DAY,HALL,PERFORMANCEORDER,TEAMNAME\nDay 1,F,10,Rockets\nDay 1,G,Exhibition,Stars\nDay 1,F,2,Stars\nDay 1,G,1,Rockets\n"), 3, null, 2, DayColumn: 0, HallColumn: 1);
        var plan = FastRosterPlan.Read(command);
        Assert.Equal(new[] { "2", "10" }, plan.Entries.Where(e => e.Hall == "F").Select(e => e.Order));
        Assert.Equal(new[] { "Exhibition", "1" }, plan.Entries.Where(e => e.Hall == "G").Select(e => e.Order));
        var teams = plan.Entries.Select(e => new FastCaptureTeam { Name = e.Name, PerformanceLabel = e.Order, Day = e.Day, Hall = e.Hall }).ToArray();
        Assert.Equal(2, FastPerformanceSearch.Schedule(teams, "DAY 1", "f").Count);
        Assert.Single(FastPerformanceSearch.Find(FastPerformanceSearch.Schedule(teams, "Day 1", "F"), "star"));
    }
}
