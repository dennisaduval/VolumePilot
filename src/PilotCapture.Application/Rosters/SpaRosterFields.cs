using System.Text.Json;

namespace PilotCapture.Application.Rosters;

public static class SpaRosterFields
{
    public static readonly string[] Columns = ["FIRSTNAME", "LASTNAME", "NAME", "NUMBER", "POSITION", "TEAMNAME", "LEAGUENAME", "SCHOOLNAME", "CLASS", "YEAR", "SPATEXT1", "SPATEXT2", "SPATEXT3", "SPATEXT4", "SPATEXT5"];

    public static Dictionary<string, string> ReadSource(string? json)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return fields;
        using var document = JsonDocument.Parse(json);
        // Old photographer-entry records are objects; roster source rows are arrays.
        if (document.RootElement.ValueKind != JsonValueKind.Array) return fields;
        foreach (var cell in document.RootElement.EnumerateArray())
        {
            var header = cell.GetProperty("Header").GetString() ?? "";
            var key = string.Concat(header.Where(char.IsLetterOrDigit)).ToUpperInvariant();
            key = key switch { "CLASSNAME" => "CLASS", "ROSTERNUMBER" or "JERSEYNUMBER" => "NUMBER", _ => key };
            if (Columns.Contains(key)) fields.TryAdd(key, cell.GetProperty("Value").GetString() ?? "");
        }
        return fields;
    }
}
