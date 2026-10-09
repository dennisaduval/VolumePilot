using System.Text;
using PilotCapture.Application.Rosters;
using Xunit;

namespace PilotCapture.Application.Tests;

public sealed class RosterImportPreviewTests
{
    [Fact]
    public void Build_suggests_columns_for_the_supplied_roster_shape()
    {
        var suggestions = RosterColumnSuggestionBuilder.Build(
            ["NUMBER", "FIRSTNAME", "LASTNAME", "TEAM/SCHOOL", "SPORT", "CLASS"]);

        Assert.Equal(1, suggestions.FirstNameColumn);
        Assert.Equal(2, suggestions.LastNameColumn);
        Assert.Equal(3, suggestions.TeamOrSchoolColumn);
        Assert.Equal(4, suggestions.SportOrGroupColumn);
        Assert.Equal(0, suggestions.RosterNumberColumn);
        Assert.Equal(5, suggestions.ClassOrCategoryColumn);
        Assert.Empty(suggestions.Warnings);
    }

    [Fact]
    public void Build_suggests_cross_group_name_matches_without_combining_roster_rows()
    {
        const string csv = "NUMBER,FIRSTNAME,LASTNAME,TEAM/SCHOOL,SPORT,CLASS\r\n"
            + "12,Alex,Smith,Central High,Volleyball,Freshman\r\n"
            + "12,Alex,Smith,Central High,Basketball,Freshman\r\n";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var document = RosterCsvReader.Read(input);

        var rows = RosterImportPreviewBuilder.Build(
            document,
            new RosterColumnMapping(1, 2, 3, 4, 0, 5));

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal(2, row.PotentialCrossGroupNameMatchCount));
        Assert.Equal("Central High / Volleyball", rows[0].GroupName);
        Assert.Equal("Central High / Basketball", rows[1].GroupName);
        Assert.All(rows, row => Assert.Equal("12", row.RosterNumber));
        Assert.All(rows, row => Assert.Equal("Freshman", row.ClassOrCategory));
        Assert.All(rows, row => Assert.Equal("Alex Smith", row.DisplayName));
        Assert.NotNull(rows[0].PotentialMatchKey);
        Assert.Equal(rows[0].PotentialMatchKey, rows[1].PotentialMatchKey);
    }

    [Fact]
    public void Build_keeps_source_whitespace_and_missing_values_in_the_raw_row()
    {
        const string csv = "FIRSTNAME,LASTNAME,TEAM/SCHOOL,SPORT,NUMBER,CLASS\r\n Alex ,Smith,Central High,Cheer,,Manager\r\n";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var document = RosterCsvReader.Read(input);

        var row = Assert.Single(RosterImportPreviewBuilder.Build(
            document,
            new RosterColumnMapping(0, 1, 2, 3, 4, 5)));

        Assert.Equal(" Alex ", row.FirstName);
        Assert.Equal("Alex Smith", row.DisplayName);
        Assert.Equal(string.Empty, row.RosterNumber);
        Assert.Equal("Manager", row.ClassOrCategory);
        Assert.True(row.SourceDataJson.Contains(" Alex ", StringComparison.Ordinal));
    }
    [Fact]
    public void Spa_fields_preserve_number_position_class_and_all_custom_texts()
    {
        const string csv = "NUMBER,FIRSTNAME,LASTNAME,TEAMNAME,POSITION,LEAGUENAME,SCHOOLNAME,CLASS,YEAR,SPATEXT1,SPATEXT2,SPATEXT3,SPATEXT4,SPATEXT5\r\n"
            + "007,Dave,Smith,Falcons,Pitcher,Junior League,Central,Senior,2026,A,B,C,D,E\r\n";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var document = RosterCsvReader.Read(input);
        var row = Assert.Single(RosterImportPreviewBuilder.Build(document, new RosterColumnMapping(1, 2, 3, null, 0, 7)));
        var fields = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(row.SpaDataJson!)!;
        Assert.Equal("007", fields["NUMBER"]);
        Assert.Equal("Pitcher", fields["POSITION"]);
        Assert.Equal("Senior", fields["CLASS"]);
        Assert.Equal("Central", fields["SCHOOLNAME"]);
        Assert.Equal("Junior League", fields["LEAGUENAME"]);
        Assert.Equal("2026", fields["YEAR"]);
        Assert.Equal("E", fields["SPATEXT5"]);
        Assert.Equal(document.Rows[0].ToSourceDataJson(), row.SourceDataJson);
    }

}

