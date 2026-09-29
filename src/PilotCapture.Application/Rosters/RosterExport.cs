namespace PilotCapture.Application.Rosters;

public sealed record RosterExportRow(
    string EventName,
    string SubjectId,
    string? MembershipId,
    string? FirstName,
    string? LastName,
    string DisplayName,
    string IdentityStatus,
    string? GroupName,
    string? RosterNumber,
    string? Role,
    string RecordSource);

public interface IRosterExportService
{
    /// <summary>Exports active subject and membership records as a new normalized roster CSV.</summary>
    Task<int> ExportEventAsync(string eventId, Stream destination, CancellationToken cancellationToken = default);
}
