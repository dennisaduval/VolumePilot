using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PilotCapture.Application.FastCapture;
using PilotCapture.Domain;
using PilotCapture.Infrastructure.Persistence;
using Xunit;

namespace PilotCapture.Infrastructure.Tests;

public sealed class ActionQueueTests
{
    [Fact]
    public async Task Archived_files_remain_in_hot_folder_without_reentering_queue_on_recovery_or_restart()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "vp-queue-" + Guid.NewGuid().ToString("N"));
        var incoming = Path.Combine(root, "incoming"); var output = Path.Combine(root, "output");
        Directory.CreateDirectory(incoming); Directory.CreateDirectory(output);
        try
        {
            var services = new ServiceCollection().AddPilotCaptureInfrastructure(Path.Combine(root, "capture.db"), Path.Combine(root, "media"));
            await using var provider = services.BuildServiceProvider();
            string job;
            using (var scope = provider.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(token);
                var fast = scope.ServiceProvider.GetRequiredService<IFastCaptureService>();
                job = await fast.CreateJobAsync(new(CaptureJobType.ActionPhotography, "Queue", incoming, output, null), token);
                await fast.SelectTeamAsync(job, "Team A", token);
                await scope.ServiceProvider.GetRequiredService<IActionCaptureService>().EnableAsync(job, DateTime.UtcNow.Ticks, token);
            }
            for (var i = 0; i < 160; i++) await File.WriteAllBytesAsync(Path.Combine(incoming, $"{i}.jpg"), [255,216,255,1], token);
            await File.WriteAllTextAsync(Path.Combine(incoming, "ignore.txt"), "not a photo", token);
            await using var controller = new FastCaptureController(provider.GetRequiredService<IServiceScopeFactory>()) { ActionMode = true };
            await controller.StartAsync(job, incoming);
            var count = 0;
            for (var i = 0; i < 300; i++)
            {
                using var scope = provider.CreateScope();
                count = await scope.ServiceProvider.GetRequiredService<PilotCaptureDbContext>().ActionReceipts.CountAsync(token);
                if (count == 160 && controller.QueuedActionFiles == 0) break;
                await Task.Delay(50, token);
            }
            Assert.Equal(160, count);
            Assert.Equal(0, controller.QueuedActionFiles);
            controller.RecoverActionIncoming(incoming);
            Assert.Equal(0, controller.QueuedActionFiles);
            await controller.StopAsync();
            await controller.StartAsync(job, incoming);
            controller.RecoverActionIncoming(incoming);
            Assert.Equal(0, controller.QueuedActionFiles);
            Assert.Equal(160, Directory.GetFiles(incoming, "*.jpg").Length);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }
}
