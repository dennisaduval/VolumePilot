using System.Text;
using System.Text.Json;
using PilotCapture.Application.Rosters;
using Xunit;

namespace PilotCapture.Application.Tests;

public sealed class RosterCsvReaderTests
{
    [Fact]
    public void Read_handles_quoted_commas_escaped_quotes_and_multiline_fields()
    {
        const string csv = "Name,Notes\r\n\"Smith, Jr.\",\"said \"\"hello\"\"\r\nagain\"\r\n";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var result = RosterCsvReader.Read(input);

        Assert.Equal(new[] { "Name", "Notes" }, result.Headers);
        Assert.Single(result.Rows);
        Assert.Equal("Smith, Jr.", result.Rows[0].Cells[0].Value);
        Assert.Equal("said \"hello\"\r\nagain", result.Rows[0].Cells[1].Value);
        Assert.True(result.Rows[0].HasExpectedFieldCount);
    }

    [Fact]
    public void Read_preserves_duplicate_headers_spacing_and_extra_cells_in_source_json()
    {
        const string csv = "Name,Name,\r\n Alice ,Bob,tail,extra\r\n";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var result = RosterCsvReader.Read(input);
        var row = Assert.Single(result.Rows);
        var source = JsonSerializer.Deserialize<RosterCsvCell[]>(row.ToSourceDataJson());!;

        Assert.Equal(" Alice ", source[0].Value);
        Assert.Equal("Name", source[0].Header);
        Assert.Equal("Name", source[1].Header);
        Assert.Equal(string.Empty, source[2].Header);
        Assert.Null(source[3].Header);
        Assert.Equal("extra", source[3].Value);
        Assert.False(row.HasExpectedFieldCount);
    }

    [Fact]
    public void Read_accepts_a_utf8_bom_and_keeps_empty_fields()
    {
        var payload = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("First,Last\r\nAda,\r\n")).ToArray();
        using var input = new MemoryStream(payload);

        var result = RosterCsvReader.Read(input);

        Assert.Equal("First", result.Headers[0]);
        Assert.Equal(string.Empty, Assert.Single(result.Rows[0].Cells.Skip(1)).Value);
    }

    [Fact]
    public void Read_rejects_an_empty_file()
    {
        using var input = new MemoryStream();

        Assert.Throws<RosterCsvFormatException>(() => RosterCsvReader.Read(input));
    }
}
