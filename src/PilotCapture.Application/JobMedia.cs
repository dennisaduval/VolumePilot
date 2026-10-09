namespace PilotCapture.Application;

public enum JobImageExportKind { Originals, EditedPng, BatchEditingOriginals }
public sealed record JobMediaResult(int Images, string Location);
public interface IJobMediaService
{
    Task<string?> GetMasterPathAsync(CancellationToken cancellationToken = default);
    Task SetMasterPathAsync(string path, CancellationToken cancellationToken = default);
    Task<JobMediaResult> PublishOriginalsAsync(string eventId, CancellationToken cancellationToken = default);
    Task<JobMediaResult> AssociateEditedAsync(string eventId, CancellationToken cancellationToken = default);
    Task<JobMediaResult> ExportAsync(string eventId, string destination, JobImageExportKind kind, CancellationToken cancellationToken = default);
}
