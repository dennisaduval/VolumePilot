using Microsoft.Extensions.DependencyInjection;
using PilotCapture.Application;

namespace PilotCapture.Infrastructure.Persistence;

/// <summary>Server I/O uses its own context and thread, independently of local capture.</summary>
public sealed class OriginalPublicationService(IServiceScopeFactory scopeFactory) : IOriginalPublicationService
{
    private readonly SemaphoreSlim _copyLock = new(1, 1);

    public async Task<JobMediaResult> PublishAsync(string eventId, CancellationToken cancellationToken = default)
    {
        await _copyLock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(async () =>
            {
                using var scope = scopeFactory.CreateScope();
                return await scope.ServiceProvider.GetRequiredService<IJobMediaService>()
                    .PublishOriginalsAsync(eventId, cancellationToken);
            }, cancellationToken);
        }
        finally { _copyLock.Release(); }
    }
}
