using PilotCapture.Domain;
namespace PilotCapture.Application.FastCapture;

public sealed record FastJobSettings(CaptureJobType Type, string Name, string SourceFolder, string OutputFolder, string? IndividualsFolder);
public sealed record FastCaptureSnapshot(FastCaptureJob Job, IReadOnlyList<FastCaptureTeam> Teams, IReadOnlyList<FastCaptureImage> Images, IReadOnlyList<int> UsedPlayers);
public interface IFastCaptureService
{
    Task<IReadOnlyList<FastCaptureJob>> GetJobsAsync(CaptureJobType type, CancellationToken token = default);
    Task<FastCaptureSnapshot> GetSnapshotAsync(string jobId, CancellationToken token = default);
    Task<string> CreateJobAsync(FastJobSettings settings, CancellationToken token = default);
    Task<FastRosterImportResult> ImportRosterAsync(FastRosterImportCommand command, CancellationToken token = default);
    Task SelectPerformanceAsync(string jobId, string teamId, CancellationToken token = default);
    Task SelectPlayerAsync(string jobId, int player, CancellationToken token = default);
    Task IncludeExistingAsync(string jobId, CancellationToken token = default);
    Task SelectTeamAsync(string jobId, string name, CancellationToken token = default);
    Task NextPlayerAsync(string jobId, CancellationToken token = default);
    Task ReviewAsync(string imageId, bool reject, CancellationToken token = default);
    Task SetPrimaryAsync(string imageId, CancellationToken token = default);
    Task<bool> ScanAsync(string jobId, CancellationToken token = default);
}
