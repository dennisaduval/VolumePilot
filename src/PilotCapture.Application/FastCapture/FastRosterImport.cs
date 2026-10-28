namespace PilotCapture.Application.FastCapture;

public sealed record FastRosterImportCommand(string JobId, string SourceName, byte[] Content,
    int TeamColumn, int? GroupColumn, int? OrderColumn);
public sealed record FastRosterImportResult(int Teams, int Rows);
