using PilotCapture.Application.Rosters;

namespace PilotCapture.Application.FastCapture;

public static class FastRosterColumns
{
    // Header selection uses logical CSV records, so quoted newlines stay in one record.
    // The caller also keeps the raw document, including every preamble record.
    public static RosterCsvDocument WithHeader(RosterCsvDocument raw, int headerRecordNumber)
    {
        if (headerRecordNumber < 1 || headerRecordNumber > raw.Rows.Count + 1)
            throw new ArgumentException("Choose a header row within the CSV.");
        if (headerRecordNumber == 1) return raw;
        var headers = raw.Rows[headerRecordNumber - 2].Cells.Select(c => c.Value).ToArray();
        var rows = raw.Rows.Skip(headerRecordNumber - 1).Select(row => new RosterCsvRow(row.RecordNumber,
            row.Cells.Select(c => new RosterCsvCell(c.ColumnIndex, c.ColumnIndex < headers.Length ? headers[c.ColumnIndex] : null, c.Value)).ToArray(),
            row.Cells.Count == headers.Length)).ToArray();
        return new(headers, rows);
    }

    public static int ColumnCount(RosterCsvDocument document) => Math.Max(document.Headers.Count,
        document.Rows.Select(r => r.Cells.Count).DefaultIfEmpty(0).Max());

    public static string Label(RosterCsvDocument document, int column) => column < document.Headers.Count &&
        !string.IsNullOrWhiteSpace(document.Headers[column]) ? document.Headers[column] : $"Column {column + 1}";
}
