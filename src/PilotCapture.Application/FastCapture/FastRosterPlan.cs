using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PilotCapture.Application.Rosters;

namespace PilotCapture.Application.FastCapture;

public sealed record FastRosterEntry(int Row, string Name, string Order, string Details, string Key,
    string Day, string Hall, string[] LegacyNames);

public sealed record FastRosterPlan(RosterCsvDocument Raw, int Rows, IReadOnlyList<FastRosterEntry> Entries)
{
    public static FastRosterPlan Read(FastRosterImportCommand command)
    {
        using var input = new MemoryStream(command.Content, false);
        var raw = RosterCsvReader.Read(input);
        var document = FastRosterColumns.WithHeader(raw, command.HeaderRecordNumber);
        var details = (command.DetailColumns ?? []).Concat(command.GroupColumn is { } groupColumn ? new[] { groupColumn } : [])
            .Distinct().Order().ToArray();
        var mapped = new int?[] { command.TeamColumn, command.OrderColumn, command.DayColumn, command.HallColumn, command.EntryTypeColumn }
            .Concat(details.Select(x => (int?)x)).ToArray();
        foreach (var column in mapped)
            if (column is < 0 || column >= FastRosterColumns.ColumnCount(document)) throw new ArgumentException("Review the selected CSV columns.");
        if (mapped.Where(x => x.HasValue).Distinct().Count() != mapped.Count(x => x.HasValue))
            throw new ArgumentException("Use each CSV column for one purpose only.");
        string Cell(RosterCsvRow row, int? index) => index is { } i && i < row.Cells.Count ? row.Cells[i].Value.Trim() : "";
        var entries = new List<FastRosterEntry>();
        foreach (var row in document.Rows)
        {
            // Repeated headings are common in CSVs converted from paginated schedules.
            var columns = mapped.Where(c => c.HasValue).Select(c => c!.Value).ToArray();
            if (columns.Length > 0 && columns.All(c => c < document.Headers.Count && Cell(row, c).Equals(document.Headers[c], StringComparison.OrdinalIgnoreCase))) continue;
            var type = Cell(row, command.EntryTypeColumn).ToUpperInvariant();
            if (type is "BREAK" or "AWARDS" or "AWARD" or "OPEN" or "EVENT BREAK" or "LUNCH") continue;
            if (type.Length > 0 && type is not ("TEAM" or "PERFORMANCE"))
                throw new ArgumentException($"Entry type '{type}' on source record {row.RecordNumber + 1} is unknown. Use Team, Performance, Break, Awards, Open or leave it blank.");
            var order = Cell(row, command.OrderColumn);
            var name = command.TeamColumn is null ? (order.Length > 0 ? $"Performance {order}" : $"Entry {row.RecordNumber + 1}") : Cell(row, command.TeamColumn);
            if (name.Length == 0 || row.Cells.All(c => string.IsNullOrWhiteSpace(c.Value))) continue;
            var values = details.Select(i => Cell(row, i)).ToArray();
            var display = string.Join(" · ", details.Select((i, n) => values[n].Length > 0 ? $"{FastRosterColumns.Label(document, i)}: {values[n]}" : "").Where(x => x.Length > 0));
            var day = Cell(row, command.DayColumn); var hall = Cell(row, command.HallColumn);
            var key = order.ToUpperInvariant();
            static string Hash(IEnumerable<string> values) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values.Select(v => v.ToUpperInvariant())))));
            if (values.Any(v => v.Length > 0)) key += "|" + Hash(values);
            if (day.Length > 0 || hall.Length > 0) key += "|S" + Hash([day, hall]);
            var legacy = new[] { name, string.Join(" / ", new[] { name }.Concat(values).Where(v => v.Length > 0)) }
                .Concat(values.Where(v => v.Length > 0).Select(v => name + " / " + v)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            entries.Add(new(row.RecordNumber, name, order, display, key, day, hall, legacy));
        }
        entries = entries.GroupBy(x => (x.Name.ToUpperInvariant(), x.Key)).Select(x => x.First()).ToList();
        if (entries.Count == 0) throw new ArgumentException("The selected columns contain no teams / performances.");
        // Numeric ordering is independent in each day/hall. A nonnumeric group
        // keeps source order. Times never control identity or ordering.
        entries = entries.GroupBy(x => (x.Day.ToUpperInvariant(), x.Hall.ToUpperInvariant())).SelectMany(group =>
            command.OrderColumn is not null && group.All(x => decimal.TryParse(x.Order, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                ? group.OrderBy(x => decimal.Parse(x.Order, CultureInfo.InvariantCulture)).ThenBy(x => x.Row).AsEnumerable() : group).ToList();
        return new(raw, document.Rows.Count, entries);
    }
}
