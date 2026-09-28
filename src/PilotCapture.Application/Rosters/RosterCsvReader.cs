using System.Text;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;

namespace PilotCapture.Application.Rosters;

/// <summary>Reads CSV fields without mapping or normalizing roster values.</summary>
public static class RosterCsvReader
{
    public static RosterCsvDocument Read(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead)
            throw new ArgumentException("The roster CSV stream must be readable.", nameof(input));

        using var text = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        using var parser = new TextFieldParser(text)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");

        string[]? headerFields;
        try
        {
            headerFields = parser.ReadFields();
        }
        catch (MalformedLineException exception)
        {
            throw new RosterCsvFormatException("The roster CSV header is malformed.", parser.ErrorLineNumber, exception);
        }

        if (headerFields is null || headerFields.Length == 0)
            throw new RosterCsvFormatException("The roster CSV is empty or has no header row.");

        var headers = headerFields.ToArray();
        var rows = new List<RosterCsvRow>();
        var recordNumber = 0;

        while (!parser.EndOfData)
        {
            string[]? fields;
            try
            {
                fields = parser.ReadFields();
            }
            catch (MalformedLineException exception)
            {
                throw new RosterCsvFormatException(
                    $"Roster CSV record {recordNumber + 1} is malformed.",
                    parser.ErrorLineNumber,
                    exception);
            }

            if (fields is null)
                continue;

            recordNumber++;
            var cells = fields.Select((value, index) => new RosterCsvCell(
                index,
                index < headers.Length ? headers[index] : null,
                value)).ToArray();

            rows.Add(new RosterCsvRow(recordNumber, cells, fields.Length == headers.Length));
        }

        return new RosterCsvDocument(headers, rows);
    }
}

public sealed record RosterCsvDocument(
    IReadOnlyList<string> Headers,
    IReadOnlyList<RosterCsvRow> Rows);

public sealed record RosterCsvCell(
    int ColumnIndex,
    string? Header,
    string Value);

public sealed record RosterCsvRow(
    int RecordNumber,
    IReadOnlyList<RosterCsvCell> Cells,
    bool HasExpectedFieldCount)
{
    /// <summary>Lossless JSON representation, including duplicate/blank headers and extra cells.</summary>
    public string ToSourceDataJson() => JsonSerializer.Serialize(Cells);
}

public sealed class RosterCsvFormatException : FormatException
{
    public RosterCsvFormatException(string message, long? lineNumber = null, Exception? innerException = null)
        : base(lineNumber is null ? message : $"{message} (physical line {lineNumber.Value}).", innerException)
    {
        LineNumber = lineNumber;
    }

    public long? LineNumber { get; }
}
