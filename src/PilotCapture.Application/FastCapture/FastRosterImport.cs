namespace PilotCapture.Application.FastCapture;

public sealed record FastRosterImportCommand(string JobId, string SourceName, byte[] Content,
    int? TeamColumn, int? GroupColumn, int? OrderColumn,
    IReadOnlyList<int>? DetailColumns = null, int HeaderRecordNumber = 1,
    int? DayColumn = null, int? HallColumn = null, int? EntryTypeColumn = null);
public sealed record FastRosterImportResult(int Teams, int Rows);
