namespace PilotCapture.Application.FastCapture;

public sealed record FastRosterImportCommand(string JobId, string SourceName, byte[] Content,
    int? TeamColumn, int? GroupColumn, int? OrderColumn,
    IReadOnlyList<int>? DetailColumns = null, int HeaderRecordNumber = 1);
public sealed record FastRosterImportResult(int Teams, int Rows);
