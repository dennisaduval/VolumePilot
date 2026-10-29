using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NetMQ;
using NetMQ.Sockets;
using PilotCapture.Application.FastCapture;
using PilotCapture.Domain;
using PilotCapture.Infrastructure.Persistence;
using Xunit;

namespace PilotCapture.Infrastructure.Tests;

public sealed class SmartShooterBridgeTests
{
    [Fact]
    public async Task Real_Zmq_transport_correlates_clock_test_to_camera_and_excludes_it_from_team_output()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "vp-ss-wire-" + Guid.NewGuid().ToString("N"));
        foreach (var dir in new[] { "incoming", "output", "media" }) Directory.CreateDirectory(Path.Combine(root, dir));
        using var serverStop = new CancellationTokenSource();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = Task.Factory.StartNew(() =>
        {
            using var responses = new ResponseSocket(); using var events = new PublisherSocket();
            responses.Options.Linger = events.Options.Linger = TimeSpan.Zero;
            responses.Bind("tcp://127.0.0.1:54544"); events.Bind("tcp://127.0.0.1:54543");
            ready.TrySetResult(); Dictionary<string, object?>? photo = null;
            while (!serverStop.IsCancellationRequested)
            {
                if (responses.TryReceiveFrameString(TimeSpan.FromMilliseconds(20), out var json))
                {
                    using var document = JsonDocument.Parse(json); var request = document.RootElement;
                    var id = request.GetProperty("msg_id").GetString();
                    var reply = new Dictionary<string, object?> { ["msg_type"] = "Response", ["msg_id"] = id, ["msg_seq_num"] = request.GetProperty("msg_seq_num").GetInt64(), ["msg_result"] = true };
                    if (id is "Synchronise" or "GetCamera")
                    {
                        reply["CameraInfo"] = new[] { new { CameraKey = "canon-r6ii-test", CameraName = "Canon R6 II", CameraStatus = "Ready" } };
                        if (id == "Synchronise") reply["PhotoInfo"] = photo is null ? Array.Empty<object>() : new object[] { photo };
                    }
                    else if (id == "Shoot")
                    {
                        Assert.Equal("Single", request.GetProperty("CameraSelection").GetString());
                        Assert.Equal("canon-r6ii-test", request.GetProperty("CameraKey").GetString());
                        var path = Path.Combine(root, "incoming", "clock.jpg"); File.WriteAllBytes(path, [255,216,255,1]);
                        photo = new() { ["msg_type"] = "Event", ["msg_id"] = "PhotoUpdated", ["PhotoKey"] = "clock-photo", ["CameraKey"] = "canon-r6ii-test",
                            ["PhotoOrigin"] = request.GetProperty("PhotoOrigin").GetString(), ["PhotoDateCaptured"] = DateTime.UtcNow.AddDays(-400).ToString("yyyyMMdd HH:mm:ss.fff"),
                            ["PhotoLocation"] = "Local Disk", ["PhotoFilename"] = path };
                    }
                    else
                    {
                        Assert.Equal("CheckClocks", id);
                        reply["msg_result"] = false; // Supported capture need not imply camera clock-read support.
                    }
                    responses.SendFrame(JsonSerializer.Serialize(reply));
                }
                if (photo is not null) events.SendFrame(JsonSerializer.Serialize(photo));
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        SmartShooterActionBridge? bridge = null;
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            var services = new ServiceCollection().AddPilotCaptureInfrastructure(Path.Combine(root, "capture.db"), Path.Combine(root, "media"));
            await using var provider = services.BuildServiceProvider();
            string jobId;
            using (var scope = provider.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(token);
                var fast = scope.ServiceProvider.GetRequiredService<IFastCaptureService>();
                jobId = await fast.CreateJobAsync(new(CaptureJobType.ActionPhotography, "Wire test", Path.Combine(root, "incoming"), Path.Combine(root, "output"), null), token);
                await fast.SelectTeamAsync(jobId, "A", token);
                await scope.ServiceProvider.GetRequiredService<IActionCaptureService>().EnableAsync(jobId, DateTime.UtcNow.Ticks, token);
            }
            bridge = new(provider.GetRequiredService<IServiceScopeFactory>(), jobId); bridge.Start(); bridge.VerifyCameras(false);
            ActionCaptureStatus? status = null;
            for (var n = 0; n < 200; n++)
            {
                using var scope = provider.CreateScope();
                status = await scope.ServiceProvider.GetRequiredService<IActionCaptureService>().GetStatusAsync(jobId, token);
                if (status.Clocks.Count > 0) break;
                await Task.Delay(50, token);
            }
            var sample = Assert.Single(status!.Clocks);
            Assert.InRange(TimeSpan.FromTicks(sample.OffsetTicks).TotalDays, 399.999, 400.001);
            Assert.Equal("canon-r6ii-test", sample.CameraKey);
            await bridge.StopAsync(); bridge = null;
            using (var scope = provider.CreateScope())
            {
                var fast = scope.ServiceProvider.GetRequiredService<IFastCaptureService>(); await fast.ScanAsync(jobId, token);
                Assert.Empty((await fast.GetSnapshotAsync(jobId, token)).Images);
            }
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "output"), "*.jpg", SearchOption.AllDirectories));
        }
        finally
        {
            if (bridge is not null) await bridge.StopAsync();
            serverStop.Cancel(); await server.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true);
        }
    }
}
