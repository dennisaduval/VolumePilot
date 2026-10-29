using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PilotCapture.Application.FastCapture;
using PilotCapture.Domain;
using PilotCapture.Infrastructure.Persistence;
using Xunit;

namespace PilotCapture.Infrastructure.Tests;

public sealed class ActionCaptureTests
{
    [Fact]
    public async Task Four_hundred_delayed_frames_from_two_cameras_keep_their_teams_after_multiple_changeovers_and_restart()
    {
        await using var env = await Env.CreateAsync();
        var token = TestContext.Current.CancellationToken;
        var t = DateTime.UtcNow.AddHours(-1).Ticks;
        await env.Action.EnableAsync(env.Job, t, token);
        await env.CameraAsync("r6", t, 0);
        var wrongDate = TimeSpan.FromDays(400).Ticks;
        await env.CameraAsync("other", t, wrongDate);
        await env.Fast.SelectTeamAsync(env.Job, "B", token, t + TimeSpan.FromMinutes(3).Ticks);
        await env.Fast.SelectTeamAsync(env.Job, "C", token, t + TimeSpan.FromMinutes(6).Ticks);
        var photos = new List<Dictionary<string, object?>>();
        for (var i = 0; i < 400; i++)
        {
            var camera = i % 2 == 0 ? "r6" : "other";
            var capture = t + TimeSpan.FromSeconds(i < 200 ? 5 + i * .7 : 185 + (i - 200) * .7).Ticks;
            var path = await env.FileAsync($"frame-{i:0000}.jpg");
            photos.Add(env.Photo(i.ToString(), camera, path, capture - (camera == "other" ? wrongDate : 0)));
        }
        await env.Action.ReceiveSmartShooterAsync(env.Job, JsonSerializer.Serialize(new { msg_id = "Synchronise", PhotoInfo = photos }), DateTime.UtcNow.Ticks, token);
        for (var n = 0; n < 60; n++) await env.Fast.ScanAsync(env.Job, token);
        var teams = await env.Db.FastTeams.ToDictionaryAsync(x => x.Id, token);
        var images = await env.Db.FastImages.ToListAsync(token);
        Assert.Equal(400, images.Count);
        Assert.Equal(200, images.Count(p => teams[p.TeamId].Name == "A")); Assert.Equal(200, images.Count(p => teams[p.TeamId].Name == "B"));
        Assert.DoesNotContain(images, p => teams[p.TeamId].Name == "C");
        Assert.All(images, p => { Assert.False(p.NeedsDelivery); Assert.True(File.Exists(p.DeliveredPath)); Assert.True(File.Exists(p.SourcePath)); });
        Assert.Equal(400, Directory.GetFiles(env.Root, "*.jpg", SearchOption.AllDirectories).Count(p => p.Contains(Path.DirectorySeparatorChar + "output" + Path.DirectorySeparatorChar)));
        var receipt = await env.Db.ActionReceipts.FirstAsync(r => r.CameraKey == "other", token);
        Assert.NotNull(receipt.CorrectedUtcTicks); Assert.Equal("Assigned", receipt.State);
        File.Delete(images[0].DeliveredPath!);
        await using var reopened = new PilotCaptureDbContext(new DbContextOptionsBuilder<PilotCaptureDbContext>().UseSqlite(env.Connection).Options);
        var restarted = new FastCaptureService(reopened, Path.Combine(env.Root, "media"));
        await restarted.ScanAsync(env.Job, token);
        Assert.Equal(400, await reopened.FastImages.CountAsync(token)); Assert.False(File.Exists(images[0].DeliveredPath));
        Assert.Equal(3, await reopened.ActionChangeovers.CountAsync(token));
    }

    [Fact]
    public async Task Uncertain_images_are_archived_without_output_and_can_be_explicitly_assigned()
    {
        await using var env = await Env.CreateAsync(); var token = TestContext.Current.CancellationToken;
        var t = DateTime.UtcNow.AddMinutes(-10).Ticks;
        await env.Action.EnableAsync(env.Job, t, token); await env.CameraAsync("r6", t, 0);
        await env.Fast.SelectTeamAsync(env.Job, "B", token, t + TimeSpan.FromMinutes(3).Ticks);
        var missing = await env.FileAsync("missing.jpg"); var boundary = await env.FileAsync("boundary.jpg");
        await env.Action.ReceiveSmartShooterAsync(env.Job, JsonSerializer.Serialize(new { msg_id = "PhotoUpdated", PhotoKey = "boundary", CameraKey = "r6", PhotoFilename = boundary,
            PhotoDateCaptured = new DateTime(t + TimeSpan.FromMinutes(3).Ticks).ToString("yyyyMMdd HH:mm:ss.fff") }), DateTime.UtcNow.Ticks, token);
        await env.Fast.ScanAsync(env.Job, token);
        Assert.Empty(await env.Db.FastImages.ToListAsync(token));
        var state = await env.Action.GetStatusAsync(env.Job, token);
        Assert.Equal(2, state.HeldCount); Assert.All(state.Held, r => Assert.True(File.Exists(r.StoredPath)));
        Assert.Contains(state.Held, r => r.Issue.Contains("close")); Assert.Contains(state.Held, r => r.Issue.Contains("metadata"));
        var teamA = await env.Db.FastTeams.SingleAsync(x => x.Name == "A", token);
        await env.Action.AssignHeldAsync(env.Job, state.Held.Single(r => r.SourcePath == missing).Id, teamA.Id, token);
        await env.Fast.ScanAsync(env.Job, token);
        Assert.True(File.Exists((await env.Db.FastImages.SingleAsync(token)).DeliveredPath));
        Assert.Equal(1, (await env.Action.GetStatusAsync(env.Job, token)).HeldCount);
    }

    [Fact]
    public async Task Changeovers_do_not_wait_for_open_camera_files_and_keep_the_button_timestamp()
    {
        await using var env = await Env.CreateAsync(); var token = TestContext.Current.CancellationToken;
        var t = DateTime.UtcNow.Ticks; await env.Action.EnableAsync(env.Job, t, token);
        var path = await env.FileAsync("still-writing.jpg");
        await using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        var boundary = t + TimeSpan.FromSeconds(20).Ticks;
        await env.Fast.SelectTeamAsync(env.Job, "B", token, boundary);
        Assert.Equal(boundary, await env.Db.ActionChangeovers.MaxAsync(x => x.UtcTicks, token));
        Assert.Equal("B", (await env.Fast.GetSnapshotAsync(env.Job, token)).Teams.Single(t => t.Id == env.Db.FastJobs.Single().CurrentTeamId).Name);
    }

    [Fact]
    public async Task Calibration_is_excluded_slow_tests_fail_and_reconnect_requires_a_new_sample()
    {
        await using var env = await Env.CreateAsync(); var token = TestContext.Current.CancellationToken;
        var t = DateTime.UtcNow.AddMinutes(-10).Ticks; await env.Action.EnableAsync(env.Job, t, token);
        var clockFile = await env.FileAsync("clock.jpg");
        await env.Action.ReceiveSmartShooterAsync(env.Job, JsonSerializer.Serialize(new { msg_id = "CameraUpdated", CameraKey = "r6", CameraStatus = "Ready" }), t, token);
        var test = env.Photo("test", "r6", clockFile, t); test["PhotoOrigin"] = ActionCaptureService.ClockOrigin + "test";
        await env.Action.ReceiveSmartShooterAsync(env.Job, JsonSerializer.Serialize(new { msg_id = "Synchronise", PhotoInfo = new[] { test } }), t, token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Action.VerifyClockAsync(env.Job, "test", t, t + TimeSpan.FromSeconds(5).Ticks, token));
        await env.Action.VerifyClockAsync(env.Job, "test", t, t + TimeSpan.FromMilliseconds(100).Ticks, token);
        await env.Fast.ScanAsync(env.Job, token); Assert.Equal("Calibration", (await env.Db.ActionReceipts.SingleAsync(token)).State);
        Assert.False(Directory.EnumerateFiles(Path.Combine(env.Root, "output"), "*.jpg", SearchOption.AllDirectories).Any());
        await env.Action.InvalidateCameraAsync(env.Job, "r6", t + TimeSpan.FromMinutes(1).Ticks, token);
        var shot = await env.FileAsync("after-reconnect.jpg");
        await env.Action.ReceiveSmartShooterAsync(env.Job, JsonSerializer.Serialize(new { msg_id = "Synchronise", PhotoInfo = new[] { env.Photo("shot", "r6", shot, t + TimeSpan.FromMinutes(2).Ticks) } }), t, token);
        await env.Fast.ScanAsync(env.Job, token); Assert.Equal(1, (await env.Action.GetStatusAsync(env.Job, token)).HeldCount);
    }

    [Fact]
    public async Task Partial_SS_updates_are_merged_and_reused_paths_are_held_instead_of_reusing_old_identity()
    {
        await using var env = await Env.CreateAsync(); var token = TestContext.Current.CancellationToken;
        var t = DateTime.UtcNow.AddMinutes(-10).Ticks; await env.Action.EnableAsync(env.Job, t, token); await env.CameraAsync("r6", t, 0);
        var path = await env.FileAsync("partial.jpg");
        await env.Action.ReceiveSmartShooterAsync(env.Job, JsonSerializer.Serialize(new { msg_id = "PhotoUpdated", PhotoKey = "partial", CameraKey = "r6",
            PhotoDateCaptured = new DateTime(t + TimeSpan.FromSeconds(10).Ticks).ToString("yyyyMMdd HH:mm:ss.fff") }), t, token);
        await env.Fast.ScanAsync(env.Job, token); Assert.Equal(1, (await env.Action.GetStatusAsync(env.Job, token)).HeldCount);
        await env.Action.ReceiveSmartShooterAsync(env.Job, JsonSerializer.Serialize(new { msg_id = "PhotoUpdated", PhotoKey = "partial", PhotoFilename = path, PhotoLocation = "Local Disk" }), t, token);
        await env.Fast.ScanAsync(env.Job, token); Assert.Single(await env.Db.FastImages.ToListAsync(token));
        var timestamp = File.GetLastWriteTimeUtc(path); await File.WriteAllBytesAsync(path, [255,216,255,2], token); File.SetLastWriteTimeUtc(path, timestamp.AddSeconds(1));
        await env.Action.ReceiveSmartShooterAsync(env.Job, JsonSerializer.Serialize(new { msg_id = "Synchronise", PhotoInfo = new[] { env.Photo("reused", "r6", path, t + TimeSpan.FromSeconds(20).Ticks) } }), t, token);
        await env.Fast.ScanAsync(env.Job, token);
        Assert.Single(await env.Db.FastImages.ToListAsync(token)); Assert.Equal(1, (await env.Action.GetStatusAsync(env.Job, token)).HeldCount);
        Assert.Contains("multiple", (await env.Action.GetStatusAsync(env.Job, token)).Held[0].Issue);
    }

    [Fact]
    public async Task A_material_clock_change_does_not_reinterpret_unassigned_old_frames_with_the_new_offset()
    {
        await using var env = await Env.CreateAsync(); var token = TestContext.Current.CancellationToken;
        var t = DateTime.UtcNow.AddMinutes(-10).Ticks; await env.Action.EnableAsync(env.Job, t, token); await env.CameraAsync("r6", t, 0);
        var shifted = env.Photo("new-clock", "r6", "", t + TimeSpan.FromSeconds(90).Ticks); shifted["PhotoOrigin"] = ActionCaptureService.ClockOrigin + "new";
        await env.Action.ReceiveSmartShooterAsync(env.Job, JsonSerializer.Serialize(new { msg_id = "Synchronise", PhotoInfo = new[] { shifted } }), t, token);
        await env.Action.VerifyClockAsync(env.Job, "new-clock", t + TimeSpan.FromMinutes(2).Ticks, t + TimeSpan.FromMinutes(2).Ticks + TimeSpan.FromMilliseconds(100).Ticks, token);
        var old = await env.FileAsync("uncertain-old.jpg"); var current = await env.FileAsync("verified-new.jpg");
        await env.Action.ReceiveSmartShooterAsync(env.Job, JsonSerializer.Serialize(new { msg_id = "Synchronise", PhotoInfo = new[] {
            env.Photo("old", "r6", old, t + TimeSpan.FromSeconds(60).Ticks), env.Photo("new", "r6", current, t + TimeSpan.FromSeconds(100).Ticks) } }), t, token);
        await env.Fast.ScanAsync(env.Job, token);
        Assert.Equal(current, (await env.Db.FastImages.SingleAsync(token)).SourcePath);
        Assert.Equal(old, Assert.Single((await env.Action.GetStatusAsync(env.Job, token)).Held).SourcePath);
    }

    private sealed class Env : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "vp-action-" + Guid.NewGuid().ToString("N"));
        public SqliteConnection Connection { get; } = new("Data Source=:memory:");
        public PilotCaptureDbContext Db { get; private set; } = null!;
        public FastCaptureService Fast { get; private set; } = null!;
        public ActionCaptureService Action { get; private set; } = null!;
        public string Job { get; private set; } = "";
        private CancellationToken Token => TestContext.Current.CancellationToken;
        public static async Task<Env> CreateAsync()
        {
            var e = new Env(); await e.Connection.OpenAsync(e.Token);
            e.Db = new(new DbContextOptionsBuilder<PilotCaptureDbContext>().UseSqlite(e.Connection).Options);
            await new DatabaseInitializer(e.Db).InitializeAsync(e.Token);
            foreach (var f in new[] { "incoming", "output", "media" }) Directory.CreateDirectory(Path.Combine(e.Root, f));
            e.Fast = new(e.Db, Path.Combine(e.Root, "media")); e.Action = new(e.Db, Path.Combine(e.Root, "media"));
            e.Job = await e.Fast.CreateJobAsync(new(CaptureJobType.ActionPhotography, "Action", Path.Combine(e.Root, "incoming"), Path.Combine(e.Root, "output"), null), e.Token);
            await e.Fast.SelectTeamAsync(e.Job, "A", e.Token); return e;
        }
        public Dictionary<string, object?> Photo(string key, string camera, string path, long ticks) => new()
        { ["PhotoKey"] = key, ["CameraKey"] = camera, ["PhotoFilename"] = path, ["PhotoDateCaptured"] = new DateTime(ticks).ToString("yyyyMMdd HH:mm:ss.fff"), ["PhotoLocation"] = "Local Disk" };
        public async Task CameraAsync(string camera, long start, long offset)
        {
            await Action.ReceiveSmartShooterAsync(Job, JsonSerializer.Serialize(new { msg_id = "CameraUpdated", CameraKey = camera, CameraName = camera, CameraStatus = "Ready" }), start, Token);
            var photo = Photo("clock-" + camera, camera, "", start - offset); photo["PhotoOrigin"] = ActionCaptureService.ClockOrigin + camera;
            await Action.ReceiveSmartShooterAsync(Job, JsonSerializer.Serialize(new { msg_id = "Synchronise", PhotoInfo = new[] { photo } }), start, Token);
            await Action.VerifyClockAsync(Job, "clock-" + camera, start, start + TimeSpan.FromMilliseconds(100).Ticks, Token);
        }
        public async Task<string> FileAsync(string name)
        { var path = Path.Combine(Root, "incoming", name); await File.WriteAllBytesAsync(path, [255,216,255,1], Token); return path; }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); Directory.Delete(Root, true); }
    }
}
