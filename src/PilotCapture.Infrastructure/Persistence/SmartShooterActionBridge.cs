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
    private sealed record ClockTest(string Camera, long Utc, long Monotonic)
    {
        public string? PhotoKey { get; set; }
        public string? SourcePath { get; set; }
        public long? ExposureObservedUtc { get; set; }
    }
    private readonly Dictionary<string, ClockTest> _tests = [];
    private readonly HashSet<string> _seenCameras = [];
    private Task? _task;
    private string _status = "Connecting to SS External API on this laptop…";
    public string Status => Volatile.Read(ref _status);
    private bool _repeat;
    private long _sequence;
    private long? _eventSequence;
    private bool _needsPhotoSync = true;
    public void Start() => _task = Task.Factory.StartNew(Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    public void VerifyCameras(bool repeatWhenIdle)
    {
        SetRepeatChecks(repeatWhenIdle);
        Volatile.Write(ref _status, "Verification requested. Waiting for Smart Shooter and the camera.");
        _verify.Enqueue(true);
    }
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
        var observedAt = Stopwatch.GetTimestamp();
        using var doc = JsonDocument.Parse(json);
        var message = doc.RootElement;
        if (Text(message, "msg_type") == "Event" && message.TryGetProperty("msg_seq_num", out var sequence) && sequence.TryGetInt64(out var number))
        {
            if (_eventSequence is { } prior && number != prior + 1) _needsPhotoSync = true;
            _eventSequence = number;
        }
        // Live-view pixels, property/options updates and other SS traffic do not
        // belong in the Action database or wake the file worker.
        if (Text(message, "msg_id") is not ("CameraUpdated" or "PhotoUpdated" or "Synchronise" or "GetCamera")) return;
        Service(s => s.ReceiveSmartShooterAsync(jobId, json, received, _stop.Token));
        void Photo(JsonElement photo)
        {
            var origin = Text(photo, "PhotoOrigin");
            var photoKey = Text(photo, "PhotoKey");
            if (photoKey.Length == 0) return;
            // The first marked photo event bounds the exposure even if the
            // timestamp or disk path is only reported when the JPEG arrives.
            if (_tests.TryGetValue(origin, out var test) && Text(photo, "CameraKey") == test.Camera &&
                (test.PhotoKey is null || test.PhotoKey == photoKey))
            {
                var elapsed = Stopwatch.GetElapsedTime(test.Monotonic, observedAt);
                if (Math.Abs((TimeSpan.FromTicks(received - test.Utc) - elapsed).TotalMilliseconds) > 250)
                {
                    _tests.Remove(origin);
                    throw new InvalidOperationException("Laptop clock changed during verification. Verify cameras again.");
                }
                test.PhotoKey = photoKey;
                test.ExposureObservedUtc ??= received;
            }
            var pending = _tests.FirstOrDefault(x => x.Value.PhotoKey == photoKey);
            if (pending.Value is not { } clock) return;
            if (Text(photo, "PhotoFilename") is { Length: > 0 } path) clock.SourcePath = path;
            if (clock.ExposureObservedUtc is not { } observed) return;
            try
            {
                if (!Service(s => s.TryVerifyClockPhotoAsync(jobId, photoKey, clock.SourcePath, clock.Utc, observed, _stop.Token))) return;
                _tests.Remove(pending.Key);
                Volatile.Write(ref _status, "Camera time verified. Test frames stay out of team output.");
            }
            catch
            {
                _tests.Remove(pending.Key);
                throw;
            }
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
        var nextSync = DateTime.MinValue; var nextPhotoSync = DateTime.MinValue;
        var lastVerification = DateTime.MinValue; var nextIdleCheck = DateTime.MinValue;
        var requestedVerification = false;
        var connected = false;
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
                        var stateBefore = Service(s => s.GetStatusAsync(jobId, _stop.Token));
                        var full = _needsPhotoSync || now >= nextPhotoSync || stateBefore.Held.Any(p => p.Issue == "Waiting for SS metadata");
                        _needsPhotoSync = false;
                        if (!Request(full ? "Synchronise" : "GetCamera", full ? null : new() { ["CameraSelection"] = "All" }))
                        { connected = false; _needsPhotoSync = true; Volatile.Write(ref _status, "SS not responding. Enable External API: publisher 54543, requests 54544."); nextSync = now.AddSeconds(3); continue; }
                        if (full) nextPhotoSync = now.AddMinutes(5);
                        nextSync = now.AddSeconds(15);
                        var state = Service(s => s.GetStatusAsync(jobId, _stop.Token));
                        foreach (var camera in state.Cameras.Where(c => c.Status is "Ready" or "Busy"))
                            if (_seenCameras.Add(camera.CameraKey)) Service(s => s.InvalidateCameraAsync(jobId, camera.CameraKey, now.Ticks, _stop.Token));
                        if (!connected && !requestedVerification)
                            Volatile.Write(ref _status, "SS connected. Verify cameras before shooting; verification takes test photos.");
                        connected = true;
                    }
                    var repeatDue = false;
                    if (_tests.Count == 0 && now >= nextIdleCheck && Volatile.Read(ref _repeat) && now - lastVerification >= TimeSpan.FromSeconds(30))
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
                        if (_tests.Count > 0)
                            Volatile.Write(ref _status, "Waiting for the current verification shot. Let it finish before retrying.");
                        else if (online.Length == 0)
                            Volatile.Write(ref _status, "Smart Shooter reports no connected camera ready to verify.");
                        else if (online.Any(c => c.Status != "Ready"))
                            Volatile.Write(ref _status, "Waiting for Smart Shooter to report all connected cameras ready.");
                        else if (online.Any(c => now.Ticks - c.LastPhotoUtcTicks < TimeSpan.FromSeconds(5).Ticks))
                            Volatile.Write(ref _status, "Waiting for five seconds without new camera photos. Stop shooting briefly.");
                        else if (!requestedVerification && Service(s => KnownTransfersAsync(s)))
                            Volatile.Write(ref _status, "Automatic clock check deferred: Smart Shooter lists photos on the camera.");
                        else
                        {
                            // A card-resident photo is not necessarily a pending transfer.
                            // An operator-requested check can proceed after the ready/quiet checks.
                            requestedVerification = false;
                            lastVerification = now;
                            foreach (var camera in online)
                            {
                                var label = camera.Name.Length > 0 ? camera.Name : camera.CameraKey;
                                Volatile.Write(ref _status, $"Sending verification shot to {label} through Smart Shooter.");
                                var origin = ActionCaptureService.ClockOrigin + Ids.New();
                                _tests[origin] = new(camera.CameraKey, DateTime.UtcNow.Ticks, Stopwatch.GetTimestamp());
                                try
                                {
                                    if (!Request("Shoot", new() { ["CameraSelection"] = "Single", ["CameraKey"] = camera.CameraKey, ["PhotoOrigin"] = origin }))
                                    {
                                        _tests.Remove(origin);
                                        Volatile.Write(ref _status, $"Smart Shooter did not acknowledge the verification shot for {label}. Verify again when ready.");
                                    }
                                    else if (_tests.ContainsKey(origin))
                                        Volatile.Write(ref _status, $"Smart Shooter accepted the shot for {label}. Waiting for its verification photo.");
                                }
                                catch (IOException ex)
                                {
                                    _tests.Remove(origin);
                                    Volatile.Write(ref _status, ex.Message);
                                }
                            }
                        }
                    }
                    else if (now >= nextIdleCheck) nextIdleCheck = now.AddSeconds(5);
                    foreach (var key in _tests.Where(t => Stopwatch.GetElapsedTime(t.Value.Monotonic) > TimeSpan.FromSeconds(10)).Select(t => t.Key).ToArray())
                    {
                        var test = _tests[key];
                        _tests.Remove(key);
                        Volatile.Write(ref _status, test.PhotoKey is null
                            ? "Smart Shooter did not report the marked verification photo. Check whether the camera fired and downloaded a JPEG."
                            : "Clock test capture time missing from Smart Shooter and the original JPEG. Verify again when idle.");
                    }
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
