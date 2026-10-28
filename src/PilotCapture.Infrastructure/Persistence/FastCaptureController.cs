using Microsoft.Extensions.DependencyInjection;
using PilotCapture.Application.FastCapture;
namespace PilotCapture.Infrastructure.Persistence;

/// <summary>Serializes file attribution and player/team boundaries off the UI thread.</summary>
public sealed class FastCaptureController(IServiceScopeFactory factory) : IAsyncDisposable, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _wake = new(0, 1);
    private CancellationTokenSource? _stop;
    private Task? _watchTask;
    private FileSystemWatcher? _watcher;
    public string? Error { get; private set; }
    public bool IsRunning => _watchTask is { IsCompleted: false };
    public async Task<T> ExecuteAsync<T>(Func<IFastCaptureService, Task<T>> action)
    {
        await _gate.WaitAsync();
        try
        {
            return await Task.Run(async () =>
            {
                using var scope = factory.CreateScope();
                return await action(scope.ServiceProvider.GetRequiredService<IFastCaptureService>());
            });
        }
        finally { _gate.Release(); }
    }
    public Task ExecuteAsync(Func<IFastCaptureService, Task> action) => ExecuteAsync(async service => { await action(service); return true; });
    public async Task StartAsync(string jobId, string folder)
    {
        await StopAsync();
        _stop = new CancellationTokenSource();
        _watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite, EnableRaisingEvents = true };
        _watcher.Created += (_, _) => Wake(); _watcher.Changed += (_, _) => Wake(); _watcher.Renamed += (_, _) => Wake();
        _watcher.Error += (_, _) => Wake();
        var token = _stop.Token;
        _watchTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                var settled = false;
                try { settled = await ExecuteAsync(s => s.ScanAsync(jobId, token)); Error = settled ? null : "Waiting for an incoming JPEG to finish writing."; }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception exception) { Error = exception.Message; }
                try { await _wake.WaitAsync(TimeSpan.FromMilliseconds(settled ? 2000 : 200), token); }
                catch (OperationCanceledException) { break; }
            }
        }, token);
    }
    private void Wake() { try { _wake.Release(); } catch (SemaphoreFullException) { } }
    public async Task StopAsync()
    {
        _stop?.Cancel(); _watcher?.Dispose(); _watcher = null;
        if (_watchTask is not null) { try { await _watchTask; } catch (OperationCanceledException) { } }
        _watchTask = null; _stop?.Dispose(); _stop = null;
    }
    public void Dispose() { _stop?.Cancel(); _watcher?.Dispose(); }
    public async ValueTask DisposeAsync() { await StopAsync(); _gate.Dispose(); _wake.Dispose(); }
}
