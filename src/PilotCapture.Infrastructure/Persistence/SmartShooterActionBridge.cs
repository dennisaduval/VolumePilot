using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NetMQ;
using NetMQ.Sockets;
using PilotCapture.Application.FastCapture;
using PilotCapture.Domain;

namespace PilotCapture.Infrastructure.Persistence;

/// <summary>SS sockets stay on one dedicated thread. No image copies or UI work on this thread.</summary>
public sealed class SmartShooterActionBridge(IServiceScopeFactory factory, string jobId, Action? wake = null) : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<bool> _verify = new();
    private readonly Dictionary<string, (long Utc, long Monotonic)> _tests = [];
    private readonly HashSet<string> _seenCameras = [];
    private Task? _task;
    private string _status = "Connecting to SS External API on this laptop…";
    public string Status => Volatile.Read(ref _status);
    private bool _repeat;
    private long _sequence;
    public void Start() => _task = Task.Factory.StartNew(Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    public void VerifyCameras(bool repeatWhenIdle) { SetRepeatChecks(repeatWhenIdle); _verify.Enqueue(true); }
    public void SetRepeatChecks(bool enabled) => Volatile.Write(ref _repeat, enabled);
    private T Service<T>(Func<IActionCaptureService, Task<T>> action)
    {
        using var scope = factory.CreateScope();
        return action(scope.ServiceProvider.GetRequiredService<IActionCaptureService>()).GetAwaiter().GetResult();
    }
    private void Service(Func<IActionCaptureService, Task> action) => Service(async s => { await action(s); return true; });
    private static string Text(JsonElement root, string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private void Process(string json)
    {
        var received = DateTime.UtcNow.Ticks;
        using var doc = JsonDocument.Parse(json);
        Service(s => s.ReceiveSmartShooterAsync(jobId, json, received, _stop.Token));
        void Photo(JsonElement photo)
        {
            var origin = Text(photo, "PhotoOrigin");
            if (!_tests.TryGetValue(origin, out var test) || !ActionCaptureService.TryCameraTime(Text(photo, "PhotoDateCaptured"), out _)) return;
            _tests.Remove(origin);
            var wallElapsed = TimeSpan.FromTicks(received - test.Utc);
            var elapsed = Stopwatch.GetElapsedTime(test.Monotonic);
            if (Math.Abs((wallElapsed - elapsed).TotalMilliseconds) > 250)
                throw new InvalidOperationException("Laptop clock changed during verification. Verify cameras again.");
            Service(s => s.VerifyClockAsync(jobId, Text(photo, "PhotoKey"), test.Utc, received, _stop.Token));
            Volatile.Write(ref _status, "Camera time verified. Test frames stay out of team output.");
        }
        var root = doc.RootElement;
        if (Text(root, "msg_id") == "PhotoUpdated") Photo(root);
        if (root.TryGetProperty("PhotoInfo", out var photos) && photos.ValueKind == JsonValueKind.Array)
            foreach (var photo in photos.EnumerateArray()) Photo(photo);
        wake?.Invoke();
    }
    private void Run()
    {
        using var subscriber = new SubscriberSocket();
        subscriber.Options.Linger = TimeSpan.Zero;
        subscriber.Connect("tcp://127.0.0.1:54543"); subscriber.SubscribeToAnyTopic();
        void Events()
        {
            var limit = 0;
            while (limit++ < 100 && subscriber.TryReceiveFrameString(TimeSpan.Zero, out var json))
            {
                try { Process(json); }
                catch (Exception ex) when (ex is not OperationCanceledException) { Volatile.Write(ref _status, ex.Message); }
            }
        }
        bool Request(string id, Dictionary<string, object?>? fields = null)
        {
            using var request = new RequestSocket(); request.Options.Linger = TimeSpan.Zero;
            request.Connect("tcp://127.0.0.1:54544");
            var message = fields ?? []; message["msg_type"] = "Request"; message["msg_id"] = id; message["msg_seq_num"] = ++_sequence;
            if (!request.TrySendFrame(TimeSpan.FromSeconds(1), JsonSerializer.Serialize(message))) return false;
            var started = Stopwatch.GetTimestamp();
            while (!_stop.IsCancellationRequested && Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(2))
            {
                Events();
                if (request.TryReceiveFrameString(TimeSpan.FromMilliseconds(20), out var json))
                {
                    using var reply = JsonDocument.Parse(json);
                    var root = reply.RootElement;
                    if (!root.TryGetProperty("msg_seq_num", out var seq) || seq.GetInt64() != _sequence) continue;
                    if (!root.TryGetProperty("msg_result", out var result) || result.ValueKind != JsonValueKind.True)
                        throw new IOException($"SS rejected {id}. Check camera readiness and SS configuration.");
                    Process(json); return true;
                }
            }
            return false;
        }
        var nextSync = DateTime.MinValue; var lastVerification = DateTime.MinValue; var nextIdleCheck = DateTime.MinValue;
        var requestedVerification = false;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    Events();
                    if (_verify.TryDequeue(out _)) { requestedVerification = true; nextIdleCheck = DateTime.MinValue; }
                    var now = DateTime.UtcNow;
                    if (now >= nextSync)
                    {
                        if (!Request("Synchronise")) { Volatile.Write(ref _status, "SS not responding. Enable External API: publisher 54543, requests 54544."); nextSync = now.AddSeconds(3); continue; }
                        nextSync = now.AddSeconds(15);
                        var state = Service(s => s.GetStatusAsync(jobId, _stop.Token));
                        foreach (var camera in state.Cameras.Where(c => c.Status is "Ready" or "Busy"))
                            if (_seenCameras.Add(camera.CameraKey)) Service(s => s.InvalidateCameraAsync(jobId, camera.CameraKey, now.Ticks, _stop.Token));
                        if (!requestedVerification && !_repeat) Volatile.Write(ref _status, "SS connected. Verify cameras before shooting; verification takes test photos.");
                    }
                    var repeatDue = false;
                    if (now >= nextIdleCheck && Volatile.Read(ref _repeat) && now - lastVerification >= TimeSpan.FromSeconds(30))
                    {
                        var check = Service(s => s.GetStatusAsync(jobId, _stop.Token));
                        repeatDue = now - lastVerification >= TimeSpan.FromMinutes(15) || check.Cameras.Any(c => c.Status is "Ready" or "Busy" &&
                            !check.Clocks.Any(s => s.CameraKey == c.CameraKey && s.UntilUtcTicks > now.Ticks));
                    }
                    if (now >= nextIdleCheck && (requestedVerification || repeatDue))
                    {
                        nextIdleCheck = now.AddSeconds(5);
                        var state = Service(s => s.GetStatusAsync(jobId, _stop.Token));
                        var online = state.Cameras.Where(c => c.Status is "Ready" or "Busy").ToArray();
                        if (online.Length == 0) { Volatile.Write(ref _status, "No connected cameras reported by SS."); }
                        else if (online.Any(c => c.Status != "Ready" || now.Ticks - c.LastPhotoUtcTicks < TimeSpan.FromSeconds(5).Ticks)
                            || Service(s => KnownTransfersAsync(s)))
                            Volatile.Write(ref _status, "Clock verification waiting for an idle camera/SS transfer queue.");
                        else
                        {
                            // CheckClocks has no documented measurement response. Never infer
                            // an offset from CameraDateTimeOffset (a sync parameter) or arrival lag.
                            try { Request("CheckClocks"); }
                            catch (IOException) { Volatile.Write(ref _status, "SS clock read unavailable; verifying from marked test exposures."); }
                            foreach (var camera in online)
                            {
                                var origin = ActionCaptureService.ClockOrigin + Ids.New();
                                _tests[origin] = (DateTime.UtcNow.Ticks, Stopwatch.GetTimestamp());
                                if (!Request("Shoot", new() { ["CameraSelection"] = "Single", ["CameraKey"] = camera.CameraKey, ["PhotoOrigin"] = origin }))
                                { _tests.Remove(origin); Volatile.Write(ref _status, "SS clock test timed out; let transfers finish and verify again."); }
                            }
                            requestedVerification = false; lastVerification = now;
                        }
                    }
                    else if (now >= nextIdleCheck) nextIdleCheck = now.AddSeconds(5);
                    foreach (var key in _tests.Where(t => Stopwatch.GetElapsedTime(t.Value.Monotonic) > TimeSpan.FromSeconds(10)).Select(t => t.Key).ToArray())
                    { _tests.Remove(key); Volatile.Write(ref _status, "Clock test metadata missing. Verify again when SS is idle."); }
                    _stop.Token.WaitHandle.WaitOne(50);
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
                catch (Exception ex) { Volatile.Write(ref _status, ex.Message); nextSync = DateTime.MinValue; _stop.Token.WaitHandle.WaitOne(500); }
            }
        }
        finally { _tests.Clear(); }
    }
    private Task<bool> KnownTransfersAsync(IActionCaptureService service) => service.HasCameraTransfersAsync(jobId, _stop.Token);
    public async Task StopAsync() { _stop.Cancel(); if (_task is not null) await _task; }
    public void Dispose() => _stop.Cancel();
}
