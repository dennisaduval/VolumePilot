using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using PilotCapture.Application.FastCapture;
namespace PilotCapture.Infrastructure.Persistence;

/// <summary>Serializes file attribution and player/team boundaries off the UI thread.</summary>
public sealed class FastCaptureController(IServiceScopeFactory factory) : IAsyncDisposable, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _actionFiles = new(1, 1);
    private readonly SemaphoreSlim _wake = new(0, 1);
    private CancellationTokenSource? _stop;
    private Task? _watchTask;
    private FileSystemWatcher? _watcher;
    private readonly ConcurrentDictionary<string, byte> _actionIncoming = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _actionQueue = new();
    private readonly ConcurrentDictionary<string, byte> _knownActionVersions = new(StringComparer.OrdinalIgnoreCase);
    private static string SourceVersion(string path)
    {
        var info = new FileInfo(path);
        return $"{Path.GetFullPath(path).ToUpperInvariant()}|{info.LastWriteTimeUtc.Ticks}|{info.Length}";
    }
    private void QueueActionPath(string path)
    {
        if (!Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            if (!File.Exists(path) || _knownActionVersions.ContainsKey(SourceVersion(path))) return;
            if (_actionIncoming.TryAdd(path, 0)) _actionQueue.Enqueue(path);
        }
        catch (IOException) { } // A rename/write race is recovered by the next folder pass.
    }
    public void RecoverActionIncoming(string folder)
    {
        foreach (var path in Directory.EnumerateFiles(folder)) QueueActionPath(path);
        Wake();
    }
    private SmartShooterActionBridge? _actionBridge;
    public bool ActionMode { get; set; }
    public int QueuedActionFiles => _actionIncoming.Count;
    public string ActionConnectionStatus => _actionBridge?.Status ?? "SS not connected. Enable its External API, then connect below.";
    public Task<T> ActionAsync<T>(Func<IActionCaptureService, Task<T>> action) => Task.Run(async () =>
    {
        using var scope = factory.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IActionCaptureService>());
    });
    public Task ActionAsync(Func<IActionCaptureService, Task> action) => ActionAsync(async s => { await action(s); return true; });
    public async Task ConnectActionAsync(string jobId)
    {
        await ActionAsync(s => s.EnableAsync(jobId, DateTime.UtcNow.Ticks));
        if (_actionBridge is not null) await _actionBridge.StopAsync();
        _actionBridge = new(factory, jobId, Wake); _actionBridge.Start();
    }
    public async Task VerifyActionCamerasAsync(string jobId, bool repeatWhenIdle)
    {
        if (_actionBridge is null) await ConnectActionAsync(jobId);
        _actionBridge?.VerifyCameras(repeatWhenIdle);
    }
    public void SetRepeatActionClockChecks(bool enabled) => _actionBridge?.SetRepeatChecks(enabled);
    public async Task AssignActionPhotosAsync(string jobId, IReadOnlyList<string> ids, string teamId)
    {
        await _actionFiles.WaitAsync();
        try
        {
            foreach (var id in ids) await ActionAsync(s => s.AssignHeldAsync(jobId, id, teamId));
            await ExecuteAsync(s => s.ScanAsync(jobId, actionPaths: []));
        }
        finally { _actionFiles.Release(); }
    }
    public async Task ReviewImageAsync(string imageId, bool reject)
    {
        if (ActionMode) await _actionFiles.WaitAsync();
        try { await ExecuteAsync(s => s.ReviewAsync(imageId, reject)); }
        finally { if (ActionMode) _actionFiles.Release(); }
    }
    public string? Error { get; private set; }
    public bool IsRunning => _watchTask is { IsCompleted: false };
    public async Task<T> ExecuteAsync<T>(Func<IFastCaptureService, Task<T>> action)
    {
        if (!ActionMode) await _gate.WaitAsync();
        try
        {
            return await Task.Run(async () =>
            {
                using var scope = factory.CreateScope();
                return await action(scope.ServiceProvider.GetRequiredService<IFastCaptureService>());
            });
        }
        finally { if (!ActionMode) _gate.Release(); }
    }
    public Task ExecuteAsync(Func<IFastCaptureService, Task> action) => ExecuteAsync(async service => { await action(service); return true; });
    public async Task StartAsync(string jobId, string folder)
    {
        await StopAsync();
        _knownActionVersions.Clear();
        if (ActionMode)
            foreach (var version in await ExecuteAsync(s => s.GetKnownActionSourceVersionsAsync(jobId)))
                _knownActionVersions.TryAdd(version, 0);
        _stop = new CancellationTokenSource();
        _watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite, EnableRaisingEvents = true };
        void Arrived(string path) { if (ActionMode) QueueActionPath(path); Wake(); }
        _actionIncoming.Clear();
        _actionQueue.Clear();
        _watcher.Created += (_, e) => Arrived(e.FullPath); _watcher.Changed += (_, e) => Arrived(e.FullPath); _watcher.Renamed += (_, e) => Arrived(e.FullPath);
        _watcher.Error += (_, _) => Wake();
        var token = _stop.Token;
        _watchTask = Task.Run(async () =>
        {
            var recovery = DateTime.MinValue;
            while (!token.IsCancellationRequested)
            {
                var settled = false;
                string[]? paths = null;
                try
                {
                    // One intake worker; Action UI commands use independent short DB scopes.
                    if (ActionMode && DateTime.UtcNow >= recovery)
                    {
                        RecoverActionIncoming(folder);
                        recovery = DateTime.UtcNow.AddMinutes(1);
                    }
                    var batch = new List<string>();
                    if (ActionMode) while (batch.Count < 8 && _actionQueue.TryDequeue(out var path)) { batch.Add(path); }
                    paths = ActionMode ? batch.ToArray() : null;
                    if (ActionMode) await _actionFiles.WaitAsync(token);
                    try
                    {
                        settled = await ExecuteAsync(s => s.ScanAsync(jobId, token, paths));
                        if (ActionMode && paths is not null)
                            foreach (var version in await ExecuteAsync(s => s.GetKnownActionSourceVersionsAsync(jobId, paths, token)))
                                _knownActionVersions.TryAdd(version, 0);
                    }
                    finally { if (ActionMode) _actionFiles.Release(); }
                    if (paths is not null)
                        foreach (var path in paths)
                        {
                            _actionIncoming.TryRemove(path, out _);
                            QueueActionPath(path); // Only genuinely unarchived versions return to the intake queue.
                        }
                    if (!_actionIncoming.IsEmpty) settled = false;
                    Error = settled ? null : "Processing incoming JPEGs.";
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception exception)
                {
                    Error = exception.Message;
                    if (paths is not null) foreach (var path in paths) { _actionIncoming.TryRemove(path, out _); QueueActionPath(path); }
                }
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
    public async Task DisconnectActionAsync() { if (_actionBridge is not null) { await _actionBridge.StopAsync(); _actionBridge = null; } }
    public void Dispose() { _stop?.Cancel(); _watcher?.Dispose(); _actionBridge?.Dispose(); }
    public async ValueTask DisposeAsync() { await StopAsync(); await DisconnectActionAsync(); _gate.Dispose(); _actionFiles.Dispose(); _wake.Dispose(); }
}

