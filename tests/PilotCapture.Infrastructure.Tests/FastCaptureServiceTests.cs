using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PilotCapture.Application.FastCapture;
using PilotCapture.Domain;
using PilotCapture.Infrastructure.Persistence;
using Xunit;
namespace PilotCapture.Infrastructure.Tests;

public sealed class FastCaptureServiceTests
{
    [Theory]
    [InlineData(CaptureJobType.PrintOnSiteTeamsOnly)]
    [InlineData(CaptureJobType.ActionPhotography)]
    public async Task Automatic_modes_route_without_roles_reject_restore_and_do_not_repeat_consumed_outputs(CaptureJobType type)
    {
        await using var env = await Environment.CreateAsync(type);
        await env.ShootAsync("camera.jpg");
        Assert.True(await env.Service.ScanAsync(env.JobId, env.Token));
        var image = Assert.Single((await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Images);
        Assert.False(image.IsPrimary); Assert.Equal(0, image.PlayerNumber);
        Assert.True(File.Exists(image.DeliveredPath));
        Assert.Equal("Falcons", Path.GetFileName(Path.GetDirectoryName(image.DeliveredPath)));
        Assert.True(File.Exists(image.SourcePath));
        Assert.Empty(Directory.GetFiles(env.Root, "*.csv", SearchOption.AllDirectories));
        await env.Service.ReviewAsync(image.Id, true, env.Token);
        var rejected = Assert.Single((await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Images);
        Assert.Contains(Path.Combine("Rejects", "Falcons"), rejected.DeliveredPath!);
        Assert.False(File.Exists(image.DeliveredPath)); Assert.True(File.Exists(rejected.DeliveredPath));
        await env.Service.ReviewAsync(image.Id, false, env.Token);
        var restored = Assert.Single((await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Images);
        Assert.True(File.Exists(restored.DeliveredPath));
        File.Delete(restored.DeliveredPath!); // Downstream watcher has consumed its delivery.
        await env.Service.ScanAsync(env.JobId, env.Token);
        Assert.Single((await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Images);
        Assert.False(File.Exists(restored.DeliveredPath));
    }
    [Fact]
    public async Task Primary_is_held_until_next_player_changes_route_correctly_and_slots_survive_restart()
    {
        await using var env = await Environment.CreateAsync(CaptureJobType.PrintOnSiteTeamAndIndividual);
        await env.Service.NextPlayerAsync(env.JobId, env.Token);
        Assert.False(Directory.Exists(Path.Combine(env.Root, "individuals", "Falcons", "Player 1")));
        await env.ShootAsync("first.jpg"); await env.Service.ScanAsync(env.JobId, env.Token);
        var first = Assert.Single((await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Images);
        Assert.True(first.IsPrimary); Assert.Null(first.DeliveredPath);
        await env.ShootAsync("second.jpg"); await env.Service.ScanAsync(env.JobId, env.Token);
        var photos = (await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Images;
        var second = photos.Single(x => x.Id != first.Id);
        Assert.Contains(Path.Combine("Falcons", "Player 1"), second.DeliveredPath!);
        await env.Service.SetPrimaryAsync(second.Id, env.Token);
        photos = (await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Images;
        Assert.Null(photos.Single(x => x.Id == second.Id).DeliveredPath);
        Assert.True(File.Exists(photos.Single(x => x.Id == first.Id).DeliveredPath));
        await env.Service.NextPlayerAsync(env.JobId, env.Token);
        var snapshot = await env.Service.GetSnapshotAsync(env.JobId, env.Token);
        var released = snapshot.Images.Single(x => x.Id == second.Id);
        Assert.EndsWith("_primary.jpg", released.DeliveredPath!); Assert.True(File.Exists(released.DeliveredPath));
        Assert.Equal(2, Assert.Single(snapshot.Teams).CurrentPlayer); Assert.Equal(new[] { 1 }, snapshot.UsedPlayers);
        Assert.False(Directory.Exists(Path.Combine(env.Root, "individuals", "Falcons", "Player 2")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Service.SetPrimaryAsync(first.Id, env.Token));
        await using var restarted = new PilotCaptureDbContext(new DbContextOptionsBuilder<PilotCaptureDbContext>().UseSqlite(env.Connection).Options);
        var service = new FastCaptureService(restarted, Path.Combine(env.Root, "media"));
        await service.ScanAsync(env.JobId, env.Token);
        Assert.Equal(2, Assert.Single((await service.GetSnapshotAsync(env.JobId, env.Token)).Teams).CurrentPlayer);
        await env.ShootAsync("third.jpg"); await service.ScanAsync(env.JobId, env.Token);
        Assert.Contains((await service.GetSnapshotAsync(env.JobId, env.Token)).Images, x => x.PlayerNumber == 2 && x.IsPrimary);
    }
    [Fact]
    public async Task Open_writers_prevent_attribution_changes_and_overlapping_folders_are_rejected()
    {
        await using var env = await Environment.CreateAsync(CaptureJobType.PrintOnSiteTeamAndIndividual);
        var path = Path.Combine(env.Root, "incoming", "writing.jpg");
        await using (var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            await writer.WriteAsync(new byte[] {255,216,255,1}, env.Token); await writer.FlushAsync(env.Token);
            Assert.False(await env.Service.ScanAsync(env.JobId, env.Token));
            await Assert.ThrowsAsync<IOException>(() => env.Service.NextPlayerAsync(env.JobId, env.Token));
        }
        Assert.True(await env.Service.ScanAsync(env.JobId, env.Token));
        Assert.Single((await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Images);
        await Assert.ThrowsAsync<ArgumentException>(() => env.Service.CreateJobAsync(new(CaptureJobType.ActionPhotography,
            "Bad", Path.Combine(env.Root, "incoming"), Path.Combine(env.Root, "incoming"), null), env.Token));
    }
    private sealed class Environment : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "vp-fast-" + Guid.NewGuid().ToString("N"));
        public SqliteConnection Connection { get; } = new("Data Source=:memory:");
        public PilotCaptureDbContext Db { get; private set; } = null!;
        public FastCaptureService Service { get; private set; } = null!;
        public string JobId { get; private set; } = "";
        public CancellationToken Token => TestContext.Current.CancellationToken;
        public static async Task<Environment> CreateAsync(CaptureJobType type)
        {
            var env = new Environment(); await env.Connection.OpenAsync(env.Token);
            env.Db = new(new DbContextOptionsBuilder<PilotCaptureDbContext>().UseSqlite(env.Connection).Options);
            await new DatabaseInitializer(env.Db).InitializeAsync(env.Token);
            foreach (var folder in new[] { "incoming", "teams", "individuals", "media" }) Directory.CreateDirectory(Path.Combine(env.Root, folder));
            env.Service = new(env.Db, Path.Combine(env.Root, "media"));
            env.JobId = await env.Service.CreateJobAsync(new(type, "Fast test", Path.Combine(env.Root, "incoming"), Path.Combine(env.Root, "teams"), Path.Combine(env.Root, "individuals")), env.Token);
            await env.Service.SelectTeamAsync(env.JobId, "Falcons", env.Token);
            return env;
        }
        public async Task ShootAsync(string filename) => await File.WriteAllBytesAsync(Path.Combine(Root, "incoming", filename), [255,216,255,1], Token);
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); Directory.Delete(Root, true); }
    }
}
