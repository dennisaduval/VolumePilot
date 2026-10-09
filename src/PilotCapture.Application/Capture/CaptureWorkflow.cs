using PilotCapture.Domain;

namespace PilotCapture.Application.Capture;

public sealed record CaptureEventChoice(string Id, string Name);
public sealed record CaptureGroupChoice(string Id, string EventId, string Name, string? LeagueName = null);
public sealed record CaptureSubjectChoice(string SubjectId, string MembershipId, string DisplayName, string? RosterNumber, string? Role, bool HasPhotos = false, string? GroupId = null, string? GroupName = null);
public sealed record CaptureSubjectDetails(string? FirstName, string? LastName, string? RosterNumber, string? Role);
public sealed record ActiveCaptureSession(string Id, string EventId, string EventName, string PhotographerName, string ProfileName, CaptureWorkflowType WorkflowType, string StationCode, DateTimeOffset StartedAtUtc, CaptureSetResult? CurrentCaptureSet);
public sealed record CaptureSetResult(string CaptureSetId, string SubjectName, bool IsUnidentified, string? GroupId, string? MembershipId, DateTimeOffset StartedAtUtc);

public interface ICaptureWorkflowService
{
    Task<string> GetStationCodeAsync(CancellationToken cancellationToken = default);
    Task SetStationCodeAsync(string stationCode, CancellationToken cancellationToken = default);
    Task<string?> GetSmartShooterOutputPathAsync(CancellationToken cancellationToken = default);
    Task SetSmartShooterOutputPathAsync(string path, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CaptureEventChoice>> GetEventsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CaptureGroupChoice>> GetGroupsAsync(string eventId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CaptureSubjectChoice>> GetSubjectsAsync(string groupId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CaptureSubjectChoice>> SearchSubjectsAsync(string eventId, string query, CancellationToken cancellationToken = default);
    Task<ActiveCaptureSession?> GetActiveSessionAsync(CancellationToken cancellationToken = default);
    Task<ActiveCaptureSession> StartSessionAsync(string eventId, string photographerName, CaptureWorkflowType workflowType, CancellationToken cancellationToken = default);
    Task<CaptureSetResult> SelectSubjectAsync(string sessionId, string membershipId, CancellationToken cancellationToken = default);
    Task<CaptureSetResult> CreateManualSubjectAsync(string sessionId, CaptureSubjectDetails details, string? groupId, CancellationToken cancellationToken = default);
    Task<CaptureSetResult> CreateUnidentifiedSubjectAsync(string sessionId, string displayName, string? groupId, CancellationToken cancellationToken = default);
    Task<CaptureSetResult?> NextSubjectAsync(string sessionId, CancellationToken cancellationToken = default);
    Task EndSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}

