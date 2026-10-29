using PilotCapture.Domain;

namespace PilotCapture.Application.FastCapture;

public sealed record ActionTeamProgress(string TeamId, string Team, int Received, int Pending, int Delivered);
public sealed record ActionCaptureStatus(IReadOnlyList<ActionCamera> Cameras, IReadOnlyList<ActionClockSample> Clocks,
    IReadOnlyList<ActionReceipt> Held, int HeldCount, IReadOnlyList<ActionTeamProgress> Progress);

public interface IActionCaptureService
{
    Task EnableAsync(string jobId, long utcTicks, CancellationToken token = default);
    Task ReceiveSmartShooterAsync(string jobId, string json, long receivedUtcTicks, CancellationToken token = default);
    Task VerifyClockAsync(string jobId, string photoKey, long requestUtcTicks, long observedUtcTicks, CancellationToken token = default);
    Task InvalidateCameraAsync(string jobId, string cameraKey, long utcTicks, CancellationToken token = default);
    Task<ActionCaptureStatus> GetStatusAsync(string jobId, CancellationToken token = default);
    Task<bool> HasCameraTransfersAsync(string jobId, CancellationToken token = default);
    Task AssignHeldAsync(string jobId, string receiptId, string teamId, CancellationToken token = default);
}
