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
        Assert.Equal("001_Falcons", Path.GetFileName(Path.GetDirectoryName(image.DeliveredPath)));
        Assert.True(File.Exists(image.SourcePath));
        Assert.Empty(Directory.GetFiles(env.Root, "*.csv", SearchOption.AllDirectories));
        await env.Service.ReviewAsync(image.Id, true, env.Token);
        var rejected = Assert.Single((await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Images);
        Assert.Contains(Path.Combine("Rejects", "001_Falcons"), rejected.DeliveredPath!);
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
        Assert.False(Directory.Exists(Path.Combine(env.Root, "individuals", "001_Falcons", "Player 1")));
        await env.ShootAsync("first.jpg"); await env.Service.ScanAsync(env.JobId, env.Token);
        var first = Assert.Single((await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Images);
        Assert.True(first.IsPrimary); Assert.Null(first.DeliveredPath);
        Assert.True(File.Exists(first.IndividualDeliveredPath)); Assert.EndsWith("_Primary.jpg", first.IndividualDeliveredPath!);
        await env.ShootAsync("second.jpg"); await env.Service.ScanAsync(env.JobId, env.Token);
        var photos = (await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Images;
        var second = photos.Single(x => x.Id != first.Id);
        Assert.Contains(Path.Combine("001_Falcons", "Player 1"), second.IndividualDeliveredPath!);
        await env.Service.SetPrimaryAsync(second.Id, env.Token);
        photos = (await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Images;
        Assert.Null(photos.Single(x => x.Id == second.Id).DeliveredPath);
        Assert.True(File.Exists(photos.Single(x => x.Id == first.Id).IndividualDeliveredPath));
        await env.Service.NextPlayerAsync(env.JobId, env.Token);
        var snapshot = await env.Service.GetSnapshotAsync(env.JobId, env.Token);
        Assert.Empty(snapshot.Images); // Player 2 is selected; Player 1's images stay hidden.
        var released = await env.Db.FastImages.AsNoTracking().SingleAsync(x => x.Id == second.Id, env.Token);
        Assert.EndsWith("_primary.jpg", released.DeliveredPath!); Assert.True(File.Exists(released.DeliveredPath));
        Assert.True(File.Exists(released.IndividualDeliveredPath));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(env.Root, "individuals", "001_Falcons", "Player 1"), "*.jpg").Length);
        // Both acknowledged copies can be consumed independently without being reissued.
        File.Delete(released.DeliveredPath!); File.Delete(released.IndividualDeliveredPath!);
        await env.Service.ScanAsync(env.JobId, env.Token);
        Assert.False(File.Exists(released.DeliveredPath)); Assert.False(File.Exists(released.IndividualDeliveredPath));
        await env.Service.ReviewAsync(released.Id, true, env.Token);
        var rejected = await env.Db.FastImages.AsNoTracking().SingleAsync(x => x.Id == released.Id, env.Token);
        Assert.Null(rejected.IndividualDeliveredPath); Assert.True(File.Exists(rejected.DeliveredPath));
        await env.Service.ReviewAsync(released.Id, false, env.Token);
        var restored = await env.Db.FastImages.AsNoTracking().SingleAsync(x => x.Id == released.Id, env.Token);
        Assert.True(File.Exists(restored.IndividualDeliveredPath)); Assert.Null(restored.DeliveredPath);
        Assert.Equal(2, Assert.Single(snapshot.Teams).CurrentPlayer); Assert.Equal(new[] { 1 }, snapshot.UsedPlayers);
        Assert.False(Directory.Exists(Path.Combine(env.Root, "individuals", "001_Falcons", "Player 2")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Service.SetPrimaryAsync(first.Id, env.Token));
        await using var restarted = new PilotCaptureDbContext(new DbContextOptionsBuilder<PilotCaptureDbContext>().UseSqlite(env.Connection).Options);
        var service = new FastCaptureService(restarted, Path.Combine(env.Root, "media"));
        await service.ScanAsync(env.JobId, env.Token);
        Assert.Equal(2, Assert.Single((await service.GetSnapshotAsync(env.JobId, env.Token)).Teams).CurrentPlayer);
        await env.ShootAsync("third.jpg"); await service.ScanAsync(env.JobId, env.Token);
        Assert.Contains((await service.GetSnapshotAsync(env.JobId, env.Token)).Images, x => x.PlayerNumber == 2 && x.IsPrimary);
    }
    [Fact]
    public async Task Primary_copies_retry_independently_and_rejection_removes_both_owned_copies()
    {
        await using var env = await Environment.CreateAsync(CaptureJobType.PrintOnSiteTeamAndIndividual);
        await env.ShootAsync("only.jpg"); await env.Service.ScanAsync(env.JobId, env.Token);
        var image = Assert.Single((await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Images);
        Assert.True(File.Exists(image.IndividualDeliveredPath));
        // A failed team delivery must not recreate an individual copy already consumed.
        File.Delete(image.IndividualDeliveredPath!);
        File.WriteAllText(Path.Combine(env.Root, "teams", "001_Falcons"), "blocked folder");
        await Assert.ThrowsAsync<IOException>(() => env.Service.NextPlayerAsync(env.JobId, env.Token));
        File.Delete(Path.Combine(env.Root, "teams", "001_Falcons"));
        await env.Service.ScanAsync(env.JobId, env.Token);
        Assert.Empty((await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Images);
        image = await env.Db.FastImages.AsNoTracking().SingleAsync(env.Token);
        Assert.True(File.Exists(image.DeliveredPath)); Assert.False(File.Exists(image.IndividualDeliveredPath));
        // Simulate the individual file still being present for the reject ownership check.
        File.Copy(image.StoredPath, image.IndividualDeliveredPath!);
        var teamCopy = image.DeliveredPath; var individualCopy = image.IndividualDeliveredPath;
        await env.Service.ReviewAsync(image.Id, true, env.Token);
        var rejected = await env.Db.FastImages.AsNoTracking().SingleAsync(env.Token);
        Assert.False(File.Exists(teamCopy)); Assert.False(File.Exists(individualCopy));
        Assert.True(File.Exists(rejected.DeliveredPath)); Assert.Null(rejected.IndividualDeliveredPath);
        Assert.True(File.Exists(image.StoredPath)); Assert.True(File.Exists(image.SourcePath));
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
    [Theory]
    [InlineData(CaptureJobType.PrintOnSiteTeamsOnly)]
    [InlineData(CaptureJobType.PrintOnSiteTeamAndIndividual)]
    [InlineData(CaptureJobType.ActionPhotography)]
    public async Task Every_mode_imports_and_preserves_rosters_and_numeric_performance_order(CaptureJobType type)
    {
        await using var env = await Environment.CreateAsync(type);
        var csv = System.Text.Encoding.UTF8.GetBytes("TEAMNAME,ORDER,FIRSTNAME,EXTRA\r\nFalcons,10,Dave,keep this\r\nBears,2,Jane,raw text\r\nFalcons,10,Sam,duplicate team row\r\nFalcons,20,Pat,later performance\r\n");
        var command = new FastRosterImportCommand(env.JobId, "schedule.csv", csv, 0, null, 1);
        var result = await env.Service.ImportRosterAsync(command, env.Token);
        Assert.Equal(3, result.Teams); Assert.Equal(4, result.Rows);
        var snapshot = await env.Service.GetSnapshotAsync(env.JobId, env.Token);
        Assert.Equal(new[] { "Bears", "Falcons", "Falcons" }, snapshot.Teams.Take(3).Select(x => x.Name));
        Assert.Equal(new[] { "2", "10", "20" }, snapshot.Teams.Take(3).Select(x => x.PerformanceLabel));
        var ids = snapshot.Teams.Select(x => x.Id).ToArray();
        await env.Service.ImportRosterAsync(command, env.Token);
        Assert.Equal(ids, (await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Teams.Select(x => x.Id));
        var import = await env.Db.FastRosterImports.FirstAsync(env.Token);
        Assert.Contains("duplicate team row", import.SourceDataJson);
        Assert.Contains("EXTRA", import.SourceDataJson);
        await env.Service.SelectPerformanceAsync(env.JobId, snapshot.Teams[2].Id, env.Token);
        Assert.Equal(snapshot.Teams[2].Id, (await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Job.CurrentTeamId);
    }

    [Fact]
    public async Task Unused_and_photographed_slots_are_selectable_without_mixing_their_images()
    {
        await using var env = await Environment.CreateAsync(CaptureJobType.PrintOnSiteTeamAndIndividual);
        await env.Service.SelectPlayerAsync(env.JobId, 8, env.Token);
        Assert.Empty(Directory.GetDirectories(Path.Combine(env.Root, "individuals")));
        await env.ShootAsync("player8.jpg"); await env.Service.ScanAsync(env.JobId, env.Token);
        await env.Service.SelectPlayerAsync(env.JobId, 3, env.Token);
        var snapshot = await env.Service.GetSnapshotAsync(env.JobId, env.Token);
        Assert.Equal(3, snapshot.Teams.Single().CurrentPlayer);
        Assert.Contains(8, snapshot.UsedPlayers);
        Assert.Empty(snapshot.Images);
        await env.Service.SelectPlayerAsync(env.JobId, 8, env.Token);
        snapshot = await env.Service.GetSnapshotAsync(env.JobId, env.Token);
        Assert.Equal(8, snapshot.Teams.Single().CurrentPlayer);
        Assert.True(File.Exists(Assert.Single(snapshot.Images).DeliveredPath));
    }

    [Fact]
    public async Task Arrival_is_detected_even_with_an_old_camera_timestamp_and_existing_files_are_explicit()
    {
        await using var env = await Environment.CreateAsync(CaptureJobType.ActionPhotography);
        await env.ShootAsync("existing.jpg"); File.SetLastWriteTimeUtc(Path.Combine(env.Root, "incoming", "existing.jpg"), DateTime.UtcNow.AddYears(-2));
        var job = await env.Service.CreateJobAsync(new(CaptureJobType.ActionPhotography, "Copied photos", Path.Combine(env.Root, "incoming"), Path.Combine(env.Root, "teams"), null), env.Token);
        await env.Service.SelectTeamAsync(job, "Copied team", env.Token);
        await env.ShootAsync("new-arrival.jpg"); File.SetLastWriteTimeUtc(Path.Combine(env.Root, "incoming", "new-arrival.jpg"), DateTime.UtcNow.AddYears(-1));
        await env.Service.ScanAsync(job, env.Token);
        Assert.Equal("new-arrival.jpg", Path.GetFileName(Assert.Single((await env.Service.GetSnapshotAsync(job, env.Token)).Images).SourcePath));
        await env.Service.IncludeExistingAsync(job, env.Token);
        await env.Service.ScanAsync(job, env.Token);
        Assert.Equal(2, (await env.Service.GetSnapshotAsync(job, env.Token)).Images.Count);
    }

    [Theory]
    [InlineData(CaptureJobType.PrintOnSiteTeamsOnly)]
    [InlineData(CaptureJobType.PrintOnSiteTeamAndIndividual)]
    [InlineData(CaptureJobType.ActionPhotography)]
    public async Task Arbitrary_headers_multiple_details_and_numbered_folders_work_in_every_mode(CaptureJobType type)
    {
        await using var env = await Environment.CreateAsync(type);
        var bytes = System.Text.Encoding.UTF8.GetBytes("Event title,notes\r\nBib,Who,Organization,Band,Tier,Extra,Extra\r\n23,Rockets,Club A,Junior,2,keep,this\r\n24,Rockets,Club A,Senior,3,raw,text\r\n23,Rockets,Club B,Junior,2,also,keep\r\n");
        var command = new FastRosterImportCommand(env.JobId, "unfamiliar.csv", bytes, 1, null, 0, [2,3,4], 2);
        var result = await env.Service.ImportRosterAsync(command, env.Token);
        Assert.Equal(3, result.Teams); Assert.Equal(3, result.Rows);
        var snapshot = await env.Service.GetSnapshotAsync(env.JobId, env.Token);
        var rockets = snapshot.Teams.Where(t => t.Name == "Rockets").ToArray();
        Assert.Equal(3, rockets.Length);
        Assert.Equal(new[] { "23", "23", "24" }, rockets.Select(t => t.PerformanceLabel));
        Assert.StartsWith("023_Rockets", rockets[0].FolderName); Assert.StartsWith("024_Rockets", rockets[2].FolderName);
        Assert.Equal(3, rockets.Select(t => t.FolderName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("Band: Junior", rockets[0].Details);
        Assert.Contains("Organization: Club B", rockets[1].Details);
        var ids = rockets.Select(t => t.Id).ToArray();
        var folders = rockets.Select(t => t.FolderName).ToArray();
        await env.Service.SelectPerformanceAsync(env.JobId, rockets[0].Id, env.Token);
        await env.ShootAsync("first.jpg"); await env.Service.ScanAsync(env.JobId, env.Token);
        if (type == CaptureJobType.PrintOnSiteTeamAndIndividual) await env.Service.NextPlayerAsync(env.JobId, env.Token);
        var image = await env.Db.FastImages.AsNoTracking().SingleAsync(x => x.TeamId == rockets[0].Id, env.Token);
        Assert.Equal(rockets[0].FolderName, Path.GetFileName(Path.GetDirectoryName(image.DeliveredPath)));
        File.Delete(image.DeliveredPath!);
        await env.Service.ImportRosterAsync(command, env.Token); await env.Service.ScanAsync(env.JobId, env.Token);
        snapshot = await env.Service.GetSnapshotAsync(env.JobId, env.Token);
        Assert.Equal(ids, snapshot.Teams.Where(t => t.Name == "Rockets").Select(t => t.Id));
        Assert.Equal(folders, snapshot.Teams.Where(t => t.Name == "Rockets").Select(t => t.FolderName));
        Assert.False(File.Exists(image.DeliveredPath));
        var import = await env.Db.FastRosterImports.FirstAsync(x => x.SourceName == "unfamiliar.csv", env.Token);
        Assert.Contains("Event title", import.SourceDataJson); Assert.Contains("also", import.SourceDataJson);
        Assert.Contains("HeaderRecordNumber", import.MappingJson); Assert.Contains("DetailColumns", import.MappingJson);
    }

    [Fact]
    public async Task Name_and_order_are_optional_and_legacy_photographed_folders_are_preserved()
    {
        await using var env = await Environment.CreateAsync(CaptureJobType.ActionPhotography);
        var bytes = System.Text.Encoding.UTF8.GetBytes("Something,Other\r\n23,Junior\r\n24,Senior\r\n");
        await env.Service.ImportRosterAsync(new(env.JobId, "numbers.csv", bytes, null, null, 0), env.Token);
        var snapshot = await env.Service.GetSnapshotAsync(env.JobId, env.Token);
        Assert.Equal("Performance 23", snapshot.Teams[0].Name);
        await env.Service.ImportRosterAsync(new(env.JobId, "nothing-mapped.csv", bytes, null, null, null), env.Token);
        Assert.Contains((await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Teams, t => t.Name == "Entry 2");
        var legacy = new FastCaptureTeam { JobId = env.JobId, Name = "Rockets / Junior", FolderName = "Rockets_Junior", PerformanceKey = "23", PerformanceLabel = "23", SortOrder = 9 };
        env.Db.FastTeams.Add(legacy); await env.Db.SaveChangesAsync(env.Token);
        await env.Service.SelectPerformanceAsync(env.JobId, legacy.Id, env.Token);
        await env.ShootAsync("legacy.jpg"); await env.Service.ScanAsync(env.JobId, env.Token);
        var delivered = Assert.Single((await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Images).DeliveredPath;
        var updated = System.Text.Encoding.UTF8.GetBytes("Run,Squad,Division,Club\r\n23,Rockets,Junior,Club A\r\n");
        await env.Service.ImportRosterAsync(new(env.JobId, "updated.csv", updated, 1, null, 0, [2,3]), env.Token);
        snapshot = await env.Service.GetSnapshotAsync(env.JobId, env.Token);
        var retained = snapshot.Teams.Single(t => t.Id == legacy.Id);
        Assert.Equal("Rockets", retained.Name); Assert.Contains("Division: Junior", retained.Details); Assert.Contains("Club: Club A", retained.Details);
        Assert.Equal("Rockets_Junior", retained.FolderName);
        Assert.Equal(delivered, Assert.Single(snapshot.Images).DeliveredPath); Assert.True(File.Exists(delivered));
        var ambiguous = new FastCaptureTeam { JobId = env.JobId, Name = "Stars / Junior", FolderName = "Old_Stars", PerformanceKey = "77", PerformanceLabel = "77", SortOrder = 10 };
        env.Db.FastTeams.Add(ambiguous); await env.Db.SaveChangesAsync(env.Token);
        await env.Service.SelectPerformanceAsync(env.JobId, ambiguous.Id, env.Token);
        await env.ShootAsync("ambiguous.jpg"); await env.Service.ScanAsync(env.JobId, env.Token);
        var split = System.Text.Encoding.UTF8.GetBytes("Run,Squad,Division,Club\r\n77,Stars,Junior,Club A\r\n77,Stars,Junior,Club B\r\n");
        await env.Service.ImportRosterAsync(new(env.JobId, "ambiguous.csv", split, 1, null, 0, [2,3]), env.Token);
        snapshot = await env.Service.GetSnapshotAsync(env.JobId, env.Token);
        Assert.Equal("Stars / Junior", snapshot.Teams.Single(t => t.Id == ambiguous.Id).Name);
        Assert.Equal(2, snapshot.Teams.Count(t => t.Name == "Stars"));
        Assert.Equal(ambiguous.Id, Assert.Single(snapshot.Images).TeamId);
    }

    [Fact]
    public async Task Reopened_players_keep_their_images_accept_more_shots_and_allow_a_new_primary()
    {
        await using var env = await Environment.CreateAsync(CaptureJobType.PrintOnSiteTeamAndIndividual);
        await env.ShootAsync("p1-first.jpg"); await env.Service.ScanAsync(env.JobId, env.Token);
        await env.ShootAsync("p1-second.jpg"); await env.Service.ScanAsync(env.JobId, env.Token);
        var first = (await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Images.Single(x => x.IsPrimary);
        await env.Service.NextPlayerAsync(env.JobId, env.Token);
        Assert.Empty((await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Images);
        await env.ShootAsync("p2-first.jpg"); await env.Service.ScanAsync(env.JobId, env.Token);
        Assert.Equal(2, Assert.Single((await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Images).PlayerNumber);
        await env.Service.SelectPlayerAsync(env.JobId, 1, env.Token);
        var returned = await env.Service.GetSnapshotAsync(env.JobId, env.Token);
        Assert.Equal(2, returned.Images.Count); Assert.All(returned.Images, x => Assert.Equal(1, x.PlayerNumber));
        Assert.Equal(first.Id, returned.Images.Single(x => x.IsPrimary).Id);
        await env.ShootAsync("p1-extra.jpg"); await env.Service.ScanAsync(env.JobId, env.Token);
        returned = await env.Service.GetSnapshotAsync(env.JobId, env.Token);
        Assert.Equal(3, returned.Images.Count); Assert.Equal(first.Id, returned.Images.Single(x => x.IsPrimary).Id);
        var extra = returned.Images.Single(x => Path.GetFileName(x.SourcePath) == "p1-extra.jpg");
        await env.Service.SetPrimaryAsync(extra.Id, env.Token);
        returned = await env.Service.GetSnapshotAsync(env.JobId, env.Token);
        Assert.Equal(extra.Id, returned.Images.Single(x => x.IsPrimary).Id);
        var primary = returned.Images.Single(x => x.Id == extra.Id);
        Assert.EndsWith("_Primary.jpg", primary.IndividualDeliveredPath!); Assert.True(File.Exists(primary.IndividualDeliveredPath));
        Assert.Null(primary.DeliveredPath); // Changed team primary still waits for completion.
        var original = returned.Images.Single(x => x.Id == first.Id);
        Assert.DoesNotContain("_Primary.jpg", original.IndividualDeliveredPath!); Assert.True(File.Exists(original.IndividualDeliveredPath));
        Assert.False(File.Exists(first.IndividualDeliveredPath));
        Assert.Single(Directory.GetFiles(Path.Combine(env.Root, "individuals", "001_Falcons", "Player 1"), "*_Primary.jpg"));
        await env.Service.NextPlayerAsync(env.JobId, env.Token);
        Assert.Equal(3, (await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Teams.Single().CurrentPlayer);
        primary = await env.Db.FastImages.AsNoTracking().SingleAsync(x => x.Id == extra.Id, env.Token);
        Assert.True(File.Exists(primary.DeliveredPath));
        // Revisiting without changing the primary cannot reprint an already consumed team copy.
        File.Delete(primary.DeliveredPath!);
        await using var restarted = new PilotCaptureDbContext(new DbContextOptionsBuilder<PilotCaptureDbContext>().UseSqlite(env.Connection).Options);
        var service = new FastCaptureService(restarted, Path.Combine(env.Root, "media"));
        await service.SelectPlayerAsync(env.JobId, 1, env.Token);
        returned = await service.GetSnapshotAsync(env.JobId, env.Token);
        Assert.Equal(3, returned.Images.Count); Assert.Equal(extra.Id, returned.Images.Single(x => x.IsPrimary).Id);
        await service.SelectPlayerAsync(env.JobId, 2, env.Token);
        Assert.False(File.Exists(primary.DeliveredPath));
        Assert.Equal(2, Assert.Single((await service.GetSnapshotAsync(env.JobId, env.Token)).Images).PlayerNumber);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetPrimaryAsync(extra.Id, env.Token));
    }

    [Fact]
    public async Task Selected_player_query_includes_all_their_images_even_with_many_newer_team_images()
    {
        await using var env = await Environment.CreateAsync(CaptureJobType.PrintOnSiteTeamAndIndividual);
        await env.ShootAsync("seed.jpg"); await env.Service.ScanAsync(env.JobId, env.Token);
        var seed = Assert.Single((await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Images);
        for (var n = 0; n < 210; n++) env.Db.FastImages.Add(new FastCaptureImage
        {
            JobId = env.JobId, TeamId = seed.TeamId, PlayerNumber = n < 100 ? 1 : 2,
            Sequence = 1000 + n, SourcePath = "metadata-only-" + n + ".jpg", SourceTicks = 1, SourceLength = 4,
            StoredPath = seed.StoredPath, Sha256 = seed.Sha256, NeedsDelivery = false
        });
        await env.Db.SaveChangesAsync(env.Token);
        var snapshot = await env.Service.GetSnapshotAsync(env.JobId, env.Token);
        Assert.Equal(101, snapshot.Images.Count); Assert.All(snapshot.Images, x => Assert.Equal(1, x.PlayerNumber));
        (await env.Db.FastTeams.SingleAsync(env.Token)).CurrentPlayer = 2; await env.Db.SaveChangesAsync(env.Token);
        snapshot = await env.Service.GetSnapshotAsync(env.JobId, env.Token);
        Assert.Equal(110, snapshot.Images.Count); Assert.All(snapshot.Images, x => Assert.Equal(2, x.PlayerNumber));
    }

    [Fact]
    public async Task Primary_marker_upgrade_renames_existing_copies_without_republishing_consumed_files()
    {
        await using var env = await Environment.CreateAsync(CaptureJobType.PrintOnSiteTeamAndIndividual);
        await env.ShootAsync("first.jpg"); await env.Service.ScanAsync(env.JobId, env.Token); await env.Service.NextPlayerAsync(env.JobId, env.Token);
        await env.ShootAsync("second.jpg"); await env.Service.ScanAsync(env.JobId, env.Token); await env.Service.NextPlayerAsync(env.JobId, env.Token);
        var photos = await env.Db.FastImages.OrderBy(x => x.Sequence).ToListAsync(env.Token);
        for (var i = 0; i < photos.Count; i++)
        {
            var photo = photos[i]; var marked = photo.IndividualDeliveredPath!;
            var legacy = marked[..^"_Primary.jpg".Length] + ".jpg";
            if (i == 0) File.Move(marked, legacy); else File.Delete(marked);
            File.Delete(photo.DeliveredPath!); // Both team copies were consumed.
            photo.IndividualDeliveredPath = legacy; photo.NeedsDelivery = false;
        }
        await env.Db.SaveChangesAsync(env.Token);
        // Reconstruct the version-9 schema before exercising the real upgrade path.
        await env.Db.Database.ExecuteSqlRawAsync("DELETE FROM schema_migrations WHERE version >= 10; DROP TABLE action_receipts; DROP TABLE action_photo_metadata; DROP TABLE action_clock_samples; DROP TABLE action_cameras; DROP TABLE action_changeovers; ALTER TABLE fast_capture_jobs DROP COLUMN action_timing_enabled; DROP INDEX ix_fast_pending_delivery; DROP INDEX ix_fast_action_progress; DROP INDEX ix_fast_capture_teams_schedule; ALTER TABLE fast_capture_teams DROP COLUMN day; ALTER TABLE fast_capture_teams DROP COLUMN hall;", env.Token);
        await using var upgraded = new PilotCaptureDbContext(new DbContextOptionsBuilder<PilotCaptureDbContext>().UseSqlite(env.Connection).Options);
        await new DatabaseInitializer(upgraded).InitializeAsync(env.Token);
        var service = new FastCaptureService(upgraded, Path.Combine(env.Root, "media"));
        var individualRoot = Path.Combine(env.Root, "individuals"); var offline = Path.Combine(env.Root, "individuals-offline");
        Directory.Move(individualRoot, offline);
        await Assert.ThrowsAsync<IOException>(() => service.ScanAsync(env.JobId, env.Token));
        Directory.Move(offline, individualRoot);
        await service.ScanAsync(env.JobId, env.Token);
        var updated = await upgraded.FastImages.AsNoTracking().OrderBy(x => x.Sequence).ToListAsync(env.Token);
        Assert.All(updated, x => { Assert.EndsWith("_Primary.jpg", x.IndividualDeliveredPath!); Assert.False(x.NeedsDelivery); Assert.False(File.Exists(x.DeliveredPath)); });
        Assert.True(File.Exists(updated[0].IndividualDeliveredPath)); Assert.False(File.Exists(updated[1].IndividualDeliveredPath));
        // Role changes of a consumed individual copy must not publish a new filename.
        await service.SelectPlayerAsync(env.JobId, 2, env.Token);
        await env.ShootAsync("replacement.jpg"); await service.ScanAsync(env.JobId, env.Token);
        var replacement = (await service.GetSnapshotAsync(env.JobId, env.Token)).Images.Single(x => x.Id != updated[1].Id);
        await service.SetPrimaryAsync(replacement.Id, env.Token);
        var old = await upgraded.FastImages.AsNoTracking().SingleAsync(x => x.Id == updated[1].Id, env.Token);
        Assert.DoesNotContain("_Primary.jpg", old.IndividualDeliveredPath!); Assert.False(File.Exists(old.IndividualDeliveredPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Opening_a_player_repairs_a_bare_primary_on_disk_even_if_its_acknowledgement_already_has_the_marker(bool acknowledgedMarked)
    {
        await using var env = await Environment.CreateAsync(CaptureJobType.PrintOnSiteTeamAndIndividual);
        await env.ShootAsync("camera.jpg"); await env.Service.ScanAsync(env.JobId, env.Token);
        await env.Service.NextPlayerAsync(env.JobId, env.Token);
        var photo = await env.Db.FastImages.SingleAsync(env.Token);
        var marked = photo.IndividualDeliveredPath!;
        var bare = marked[..^"_Primary.jpg".Length] + ".jpg";
        File.Move(marked, bare);
        photo.IndividualDeliveredPath = acknowledgedMarked ? marked : bare;
        photo.NeedsDelivery = false;
        await env.Db.SaveChangesAsync(env.Token);
        File.Delete(photo.DeliveredPath!); // A repair must never repeat the team delivery.
        await env.Service.SelectPlayerAsync(env.JobId, 1, env.Token);
        var current = Assert.Single((await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Images);
        Assert.Equal(marked, current.IndividualDeliveredPath);
        Assert.True(File.Exists(marked)); Assert.False(File.Exists(bare)); Assert.False(File.Exists(current.DeliveredPath));
        Assert.True(current.IsPrimary); Assert.True(current.Released);
        Assert.Equal(await File.ReadAllBytesAsync(current.StoredPath, env.Token), await File.ReadAllBytesAsync(marked, env.Token));
        // Pressing Primary again repairs a late bare copy, including an owned duplicate.
        File.Copy(marked, bare);
        await env.Service.SetPrimaryAsync(current.Id, env.Token);
        Assert.True(File.Exists(marked)); Assert.False(File.Exists(bare));
        // Consumed copies stay consumed, even during explicit marker reconciliation.
        File.Delete(marked);
        await env.Service.RepairPrimaryFilenameAsync(env.JobId, env.Token);
        await env.Service.SetPrimaryAsync(current.Id, env.Token);
        Assert.False(File.Exists(marked)); Assert.False(File.Exists(bare)); Assert.False(File.Exists(current.DeliveredPath));
    }

    [Theory]
    [InlineData(CaptureJobType.PrintOnSiteTeamsOnly)]
    [InlineData(CaptureJobType.PrintOnSiteTeamAndIndividual)]
    [InlineData(CaptureJobType.ActionPhotography)]
    public async Task Day_hall_selection_limits_next_performance_preserves_repeat_appearances_and_routes_separately(CaptureJobType type)
    {
        await using var env = await Environment.CreateAsync(type);
        var command = new FastRosterImportCommand(env.JobId, "weekend.csv", System.Text.Encoding.UTF8.GetBytes(
            "DAY,HALL,PERFORMANCEORDER,PROGRAM,TEAMNAME,DIVISION,ENTRYTYPE\nSaturday,F,10,Club A,Rockets,Junior,Team\nSaturday,F,2,Club B,Stars,Senior,Team\nSaturday,F,3,,Break,,Break\nSaturday,G,2,Club A,Rockets,Junior,Team\nSunday,F,2,Club A,Rockets,Junior,Team\n"), 4, null, 2, [3, 5], DayColumn: 0, HallColumn: 1, EntryTypeColumn: 6);
        await env.Service.ImportRosterAsync(command, env.Token);
        await env.Service.SelectScheduleAsync(env.JobId, "Saturday", "F", env.Token);
        var snapshot = await env.Service.GetSnapshotAsync(env.JobId, env.Token);
        Assert.Equal("Stars", snapshot.Teams.Single(t => t.Id == snapshot.Job.CurrentTeamId).Name);
        await env.Service.NextPerformanceAsync(env.JobId, env.Token);
        await env.ShootAsync("saturday-f.jpg"); await env.Service.ScanAsync(env.JobId, env.Token);
        var first = Assert.Single((await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Images);
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Service.NextPerformanceAsync(env.JobId, env.Token));
        await env.Service.SelectScheduleAsync(env.JobId, "Saturday", "G", env.Token);
        await env.ShootAsync("saturday-g.jpg"); await env.Service.ScanAsync(env.JobId, env.Token);
        var second = Assert.Single((await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Images);
        Assert.NotEqual(first.TeamId, second.TeamId);
        Assert.NotEqual(Path.GetDirectoryName(first.IndividualDeliveredPath ?? first.DeliveredPath), Path.GetDirectoryName(second.IndividualDeliveredPath ?? second.DeliveredPath));
        await env.Service.SelectScheduleAsync(env.JobId, "Sunday", "F", env.Token);
        await env.ShootAsync("sunday-f.jpg"); await env.Service.ScanAsync(env.JobId, env.Token);
        var third = Assert.Single((await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Images);
        Assert.NotEqual(first.TeamId, third.TeamId);
        await Assert.ThrowsAsync<ArgumentException>(() => env.Service.SelectScheduleAsync(env.JobId, "Sunday", "Missing hall", env.Token));
        var ids = (await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Teams.Select(t => t.Id).Order().ToArray();
        await env.Service.ImportRosterAsync(command, env.Token);
        Assert.Equal(ids, (await env.Service.GetSnapshotAsync(env.JobId, env.Token)).Teams.Select(t => t.Id).Order());
        await using var reopened = new PilotCaptureDbContext(new DbContextOptionsBuilder<PilotCaptureDbContext>().UseSqlite(env.Connection).Options);
        var restarted = new FastCaptureService(reopened, Path.Combine(env.Root, "media"));
        snapshot = await restarted.GetSnapshotAsync(env.JobId, env.Token);
        Assert.Equal("Sunday", snapshot.Teams.Single(t => t.Id == snapshot.Job.CurrentTeamId).Day);
        Assert.Equal(third.Id, Assert.Single(snapshot.Images).Id);
        Assert.Equal(4, snapshot.Teams.Count(t => t.Day.Length > 0));
        Assert.Contains("Saturday_F_010_Rockets", snapshot.Teams.Single(t => t.Id == first.TeamId).FolderName);
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
