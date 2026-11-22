using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NetMQ;
using NetMQ.Sockets;
using PilotCapture.Application.FastCapture;
using PilotCapture.Domain;
using PilotCapture.Infrastructure.Persistence;
using Xunit;

namespace PilotCapture.Infrastructure.Tests;

[Collection("SS transport")]
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
                        if (id == "Synchronise")
                        {
                            // A card-only record can survive reconnects. It must
                            // not block an explicit verification of a ready camera.
                            var onCard = new { PhotoKey = "old-card-photo", CameraKey = "canon-r6ii-test", PhotoLocation = "Camera" };
                            reply["PhotoInfo"] = photo is null ? new object[] { onCard } : new object[] { onCard, photo };
                        }
                    }
                    else if (id == "Shoot")
                    {
                        Assert.Equal("Single", request.GetProperty("CameraSelection").GetString());
                        Assert.Equal("canon-r6ii-test", request.GetProperty("CameraKey").GetString());
                        var number = request.GetProperty("msg_seq_num").GetInt64();
                        var path = Path.Combine(root, "incoming", $"clock-{number}.jpg"); File.WriteAllBytes(path, [255,216,255,1]);
                        photo = new() { ["msg_type"] = "Event", ["msg_id"] = "PhotoUpdated", ["PhotoKey"] = "clock-photo-" + number, ["CameraKey"] = "canon-r6ii-test",
                            ["PhotoOrigin"] = request.GetProperty("PhotoOrigin").GetString(), ["PhotoDateCaptured"] = DateTime.UtcNow.AddDays(-400).ToString("yyyyMMdd HH:mm:ss.fff"),
                            ["PhotoLocation"] = "Local Disk", ["PhotoFilename"] = path };
                    }
                    else throw new InvalidOperationException($"Unexpected Smart Shooter request: {id}");
                    responses.SendFrame(JsonSerializer.Serialize(reply));
                }
                if (photo is not null) events.SendFrame(JsonSerializer.Serialize(photo));
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        FastCaptureController? controller = null;
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
            controller = new(provider.GetRequiredService<IServiceScopeFactory>()) { ActionMode = true };
            await controller.VerifyActionCamerasAsync(jobId, false);
            ActionCaptureStatus? status = null;
            for (var n = 0; n < 600; n++)
            {
                using var scope = provider.CreateScope();
                status = await scope.ServiceProvider.GetRequiredService<IActionCaptureService>().GetStatusAsync(jobId, token);
                if (status.Clocks.Count > 0) break;
                // Keep the production two-second uncertainty limit. If an
                // overloaded CI host rejects a slow attempt, exercise the same
                // explicit retry the operator uses rather than relaxing timing.
                if (n is 200 or 400) await controller.VerifyActionCamerasAsync(jobId, false);
                await Task.Delay(50, token);
            }
            Assert.True(status!.Clocks.Count > 0, "SS clock verification failed: " + controller.ActionConnectionStatus);
            var sample = status.Clocks.OrderByDescending(c => c.FromUtcTicks).First();
            Assert.InRange(TimeSpan.FromTicks(sample.OffsetTicks).TotalDays, 399.999, 400.001);
            Assert.Equal("canon-r6ii-test", sample.CameraKey);
            await controller.DisposeAsync(); controller = null;
            using (var scope = provider.CreateScope())
            {
                var fast = scope.ServiceProvider.GetRequiredService<IFastCaptureService>(); await fast.ScanAsync(jobId, token);
                Assert.Empty((await fast.GetSnapshotAsync(jobId, token)).Images);
            }
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "output"), "*.jpg", SearchOption.AllDirectories));
        }
        finally
        {
            if (controller is not null) await controller.DisposeAsync();
            serverStop.Cancel(); await server.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true);
        }
    }
}

[CollectionDefinition("SS transport", DisableParallelization = true)]
public sealed class SmartShooterTransportCollection { }
