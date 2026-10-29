using System.Security.Cryptography;
using System.Text.Json;
using System.Globalization;
using PilotCapture.Application.Rosters;
using Microsoft.EntityFrameworkCore;
using PilotCapture.Application.FastCapture;
using PilotCapture.Domain;

namespace PilotCapture.Infrastructure.Persistence;

public sealed class FastCaptureService(PilotCaptureDbContext db, string mediaRoot) : IFastCaptureService
{
    public async Task<IReadOnlyList<FastCaptureJob>> GetJobsAsync(CaptureJobType type, CancellationToken token = default) =>
        await db.FastJobs.AsNoTracking().Where(x => x.JobType == type).OrderByDescending(x => x.Id).ToListAsync(token);

    public async Task<FastCaptureSnapshot> GetSnapshotAsync(string jobId, CancellationToken token = default)
    {
        var job = await db.FastJobs.AsNoTracking().SingleAsync(x => x.Id == jobId, token);
        var teams = await db.FastTeams.AsNoTracking().Where(x => x.JobId == jobId).OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync(token);
        var imagesQuery = db.FastImages.AsNoTracking().Where(x => x.JobId == jobId && x.TeamId == job.CurrentTeamId);
        if (job.JobType == CaptureJobType.PrintOnSiteTeamAndIndividual)
        {
            var player = teams.FirstOrDefault(t => t.Id == job.CurrentTeamId)?.CurrentPlayer;
            imagesQuery = imagesQuery.Where(x => x.PlayerNumber == player);
        }
        // Show every image of the selected player. Automatic modes keep their recent-team list.
        var ordered = imagesQuery.OrderByDescending(x => x.Sequence);
        var images = await (job.JobType == CaptureJobType.PrintOnSiteTeamAndIndividual ? ordered : ordered.Take(100)).ToListAsync(token);
        var used = await db.FastImages.Where(x => x.TeamId == job.CurrentTeamId && x.PlayerNumber > 0)
            .Select(x => x.PlayerNumber).Distinct().OrderBy(x => x).ToListAsync(token);
        return new(job, teams, images, used);
    }

    public async Task<string> CreateJobAsync(FastJobSettings settings, CancellationToken token = default)
    {
        if (settings.Type is not (CaptureJobType.PrintOnSiteTeamsOnly or CaptureJobType.PrintOnSiteTeamAndIndividual or CaptureJobType.ActionPhotography))
            throw new ArgumentException("Choose one of the three immediate-output job types.");
        if (string.IsNullOrWhiteSpace(settings.Name)) throw new ArgumentException("Enter a job name.");
        var source = Path.GetFullPath(settings.SourceFolder);
        var output = Path.GetFullPath(settings.OutputFolder);
        var individual = settings.Type == CaptureJobType.PrintOnSiteTeamAndIndividual
            ? Path.GetFullPath(settings.IndividualsFolder ?? throw new ArgumentException("Choose the individual images folder.")) : null;
        if (!Directory.Exists(source) || !Directory.Exists(output) || (individual is not null && !Directory.Exists(individual)))
            throw new DirectoryNotFoundException("Choose existing source and destination folders.");
        var rejects = RejectRoot(output);
        var folders = new[] { source, output, individual, rejects }.Where(x => x is not null).Cast<string>().ToArray();
        for (var i = 0; i < folders.Length; i++)
            for (var j = i + 1; j < folders.Length; j++)
                if (Inside(folders[i], folders[j]) || Inside(folders[j], folders[i]))
                    throw new ArgumentException("Source, team output, individual output and Rejects folders must be separate and cannot contain each other.");
        var job = new FastCaptureJob { Name = settings.Name.Trim(), JobType = settings.Type, SourceFolder = source,
            OutputFolder = output, IndividualsFolder = individual, StartedAtTicks = DateTime.UtcNow.Ticks,
            IgnoredSourceVersionsJson = JsonSerializer.Serialize(Directory.EnumerateFiles(source).Where(IsJpeg).Select(x => new FileInfo(x)).Select(VersionKey).ToArray()) };
        db.FastJobs.Add(job);
        await db.SaveChangesAsync(token);
        return job.Id;
    }

    public async Task SelectTeamAsync(string jobId, string name, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter a team name.");
        if (!await ScanAsync(jobId, token)) throw new IOException("An incoming photo is still being written. Wait before switching teams.");
        var job = await db.FastJobs.SingleAsync(x => x.Id == jobId, token);
        var current = await db.FastTeams.FirstOrDefaultAsync(x => x.Id == job.CurrentTeamId, token);
        var day = current?.Day ?? ""; var hall = current?.Hall ?? "";
        var matches = await db.FastTeams.Where(x => x.JobId == jobId && x.Name == name.Trim() && x.Day == day && x.Hall == hall).OrderBy(x => x.SortOrder).ToListAsync(token);
        if (matches.Count > 1) throw new ArgumentException("Several performances use that team name. Select a specific performance / division.");
        var team = matches.SingleOrDefault();
        if (team is not null && team.Id == job.CurrentTeamId)
        {
            if (job.JobType == CaptureJobType.PrintOnSiteTeamAndIndividual) await RepairPrimaryFilenameAsync(jobId, token);
            return;
        }
        if (job.CurrentTeamId is not null && job.JobType == CaptureJobType.PrintOnSiteTeamAndIndividual)
            await NextPlayerAsync(jobId, token);
        if (team is null)
        {
            var teams = await db.FastTeams.Where(x => x.JobId == jobId).ToListAsync(token);
            var next = teams.Select(x => x.SortOrder).DefaultIfEmpty(0).Max() + 1;
            team = new FastCaptureTeam { JobId = jobId, Name = name.Trim(), SortOrder = next, Day = day, Hall = hall };
            team.FolderName = PerformanceFolder(team, teams);
            db.FastTeams.Add(team);
        }
        job.CurrentTeamId = team.Id;
        await db.SaveChangesAsync(token);
    }

    public async Task NextPlayerAsync(string jobId, CancellationToken token = default)
    {
        if (!await ScanAsync(jobId, token)) throw new IOException("An incoming photo is still being written. Wait before Next Player.");
        var job = await db.FastJobs.SingleAsync(x => x.Id == jobId, token);
        if (job.JobType != CaptureJobType.PrintOnSiteTeamAndIndividual) throw new InvalidOperationException("This job does not use player slots.");
        var team = await db.FastTeams.SingleAsync(x => x.Id == job.CurrentTeamId, token);
        var photos = await db.FastImages.Where(x => x.TeamId == team.Id && x.PlayerNumber == team.CurrentPlayer).ToListAsync(token);
        if (photos.Count == 0) return;
        await using var tx = await db.Database.BeginTransactionAsync(token);
        foreach (var photo in photos) { photo.Released = true; photo.NeedsDelivery = true; }
        var used = await db.FastImages.Where(x => x.TeamId == team.Id && x.PlayerNumber > 0).Select(x => x.PlayerNumber).Distinct().ToListAsync(token);
        do { team.CurrentPlayer++; } while (used.Contains(team.CurrentPlayer));
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
        await DeliverAsync(job, token);
    }

    public async Task SetPrimaryAsync(string imageId, CancellationToken token = default)
    {
        var image = await db.FastImages.SingleAsync(x => x.Id == imageId, token);
        var job = await db.FastJobs.SingleAsync(x => x.Id == image.JobId, token);
        var team = await db.FastTeams.SingleAsync(x => x.Id == image.TeamId, token);
        if (job.JobType != CaptureJobType.PrintOnSiteTeamAndIndividual || job.CurrentTeamId != image.TeamId || team.CurrentPlayer != image.PlayerNumber)
            throw new InvalidOperationException("Select that player before changing their primary image.");
        if (image.IsPrimary && !image.IsRejected) { await RepairPrimaryFilenameAsync(job.Id, token); return; }
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var images = await db.FastImages.Where(x => x.TeamId == image.TeamId && x.PlayerNumber == image.PlayerNumber).ToListAsync(token);
        foreach (var photo in images) { photo.IsPrimary = false; photo.NeedsDelivery = true; }
        await db.SaveChangesAsync(token);
        image.IsRejected = false; image.IsPrimary = true; image.Released = false;
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
        await DeliverAsync(job, token);
    }

    public async Task ReviewAsync(string imageId, bool reject, CancellationToken token = default)
    {
        var image = await db.FastImages.SingleAsync(x => x.Id == imageId, token);
        var job = await db.FastJobs.SingleAsync(x => x.Id == image.JobId, token);
        image.NeedsDelivery = true;
        image.IsRejected = reject;
        if (reject) image.IsPrimary = false;
        await db.SaveChangesAsync(token);
        var currentTeam = job.JobType == CaptureJobType.PrintOnSiteTeamAndIndividual
            ? await db.FastTeams.SingleAsync(x => x.Id == image.TeamId, token) : null;
        if (currentTeam is not null && job.CurrentTeamId == image.TeamId && currentTeam.CurrentPlayer == image.PlayerNumber)
        {
            var eligible = await db.FastImages.Where(x => x.TeamId == image.TeamId && x.PlayerNumber == image.PlayerNumber && !x.IsRejected)
                .OrderBy(x => x.Sequence).ToListAsync(token);
            if (eligible.Count > 0 && !eligible.Any(x => x.IsPrimary))
            { eligible[0].IsPrimary = true; eligible[0].Released = false; eligible[0].NeedsDelivery = true; }
            await db.SaveChangesAsync(token);
        }
        await DeliverAsync(job, token);
    }

    public async Task<bool> ScanAsync(string jobId, CancellationToken token = default)
    {
        var job = await db.FastJobs.SingleAsync(x => x.Id == jobId, token);
        if (job.CurrentTeamId is null) return true;
        var team = await db.FastTeams.SingleAsync(x => x.Id == job.CurrentTeamId, token);
        var settled = true;
        var imported = await db.FastImages.AsNoTracking().Where(x => x.JobId == jobId)
            .Select(x => new { x.SourcePath, x.SourceTicks, x.SourceLength }).ToListAsync(token);
        var known = imported.Select(x => (x.SourcePath.ToUpperInvariant(), x.SourceTicks, x.SourceLength)).ToHashSet();
        var sequence = await db.FastImages.Where(x => x.TeamId == team.Id).Select(x => (int?)x.Sequence).MaxAsync(token) ?? 0;
        var ignored = job.IgnoredSourceVersionsJson is { } baseline ? JsonSerializer.Deserialize<HashSet<string>>(baseline)! : null;
        var files = Directory.EnumerateFiles(job.SourceFolder).Where(IsJpeg).Select(x => new FileInfo(x))
            .Where(x => ignored is not null ? !ignored.Contains(VersionKey(x)) : x.LastWriteTimeUtc.Ticks >= job.StartedAtTicks)
            .OrderBy(x => x.LastWriteTimeUtc).ThenBy(x => x.Name).ToArray();
        foreach (var info in files)
        {
            token.ThrowIfCancellationRequested();
            var path = info.FullName; var ticks = info.LastWriteTimeUtc.Ticks; var length = info.Length;
            if (known.Contains((path.ToUpperInvariant(), ticks, length))) continue;
            var image = new FastCaptureImage { JobId = jobId, TeamId = team.Id, PlayerNumber = job.JobType == CaptureJobType.PrintOnSiteTeamAndIndividual ? team.CurrentPlayer : 0,
                SourcePath = path, SourceTicks = ticks, SourceLength = length };
            var directory = Path.Combine(mediaRoot, "fast", job.Id);
            Directory.CreateDirectory(directory);
            image.StoredPath = Path.Combine(directory, image.Id + ".jpg");
            try
            {
                // A reader that does not share writes waits for Smart Shooter to close its writer.
                await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
                info.Refresh();
                if (source.Length != length || info.LastWriteTimeUtc.Ticks != ticks) { settled = false; continue; }
                if (source.Length < 4) { settled = false; continue; }
                var header = new byte[3]; await source.ReadExactlyAsync(header, token);
                if (header[0] != 255 || header[1] != 216 || header[2] != 255) throw new InvalidDataException($"Not a JPEG: {info.Name}");
                source.Position = 0;
                await CopyNewAsync(source, image.StoredPath, token);
                image.Sha256 = await HashAsync(image.StoredPath, token);
            }
            catch (IOException) { settled = false; continue; }
            image.Sequence = ++sequence;
            image.IsPrimary = job.JobType == CaptureJobType.PrintOnSiteTeamAndIndividual && !await db.FastImages.AnyAsync(x => x.TeamId == team.Id && x.PlayerNumber == team.CurrentPlayer && x.IsPrimary, token);
            db.FastImages.Add(image);
            try { await db.SaveChangesAsync(token); }
            catch { if (File.Exists(image.StoredPath)) File.Delete(image.StoredPath); throw; }
        }
        await DeliverAsync(job, token);
        return settled;
    }

    public async Task IncludeExistingAsync(string jobId, CancellationToken token = default)
    {
        var job = await db.FastJobs.SingleAsync(x => x.Id == jobId, token);
        job.IgnoredSourceVersionsJson = "[]";
        await db.SaveChangesAsync(token);
    }

    public async Task SelectScheduleAsync(string jobId, string day, string hall, CancellationToken token = default)
    {
        var snapshot = await GetSnapshotAsync(jobId, token);
        var schedule = FastPerformanceSearch.Schedule(snapshot.Teams, day, hall);
        var team = schedule.FirstOrDefault(t => t.Id == snapshot.Job.CurrentTeamId) ?? schedule.FirstOrDefault()
            ?? throw new ArgumentException("Choose a day and hall from this job's schedule.");
        await SelectPerformanceAsync(jobId, team.Id, token);
    }

    public async Task NextPerformanceAsync(string jobId, CancellationToken token = default)
    {
        var snapshot = await GetSnapshotAsync(jobId, token);
        var current = snapshot.Teams.FirstOrDefault(t => t.Id == snapshot.Job.CurrentTeamId)
            ?? throw new ArgumentException("Select a performance first.");
        var schedule = FastPerformanceSearch.Schedule(snapshot.Teams, current.Day, current.Hall).ToList();
        var index = schedule.FindIndex(t => t.Id == current.Id);
        if (index + 1 >= schedule.Count) throw new InvalidOperationException("This is the last performance in the selected day and hall.");
        await SelectPerformanceAsync(jobId, schedule[index + 1].Id, token);
    }

    public async Task SelectPerformanceAsync(string jobId, string teamId, CancellationToken token = default)
    {
        if (!await ScanAsync(jobId, token)) throw new IOException("An incoming photo is still being written. Wait before switching performances.");
        var job = await db.FastJobs.SingleAsync(x => x.Id == jobId, token);
        if (!await db.FastTeams.AnyAsync(x => x.Id == teamId && x.JobId == jobId, token)) throw new ArgumentException("Choose a performance from this job.");
        if (job.CurrentTeamId == teamId)
        {
            if (job.JobType == CaptureJobType.PrintOnSiteTeamAndIndividual) await RepairPrimaryFilenameAsync(jobId, token);
            return;
        }
        if (job.CurrentTeamId is not null && job.JobType == CaptureJobType.PrintOnSiteTeamAndIndividual) await NextPlayerAsync(jobId, token);
        job.CurrentTeamId = teamId;
        await db.SaveChangesAsync(token);
        if (job.JobType == CaptureJobType.PrintOnSiteTeamAndIndividual) await RepairPrimaryFilenameAsync(jobId, token);
    }

    public async Task SelectPlayerAsync(string jobId, int player, CancellationToken token = default)
    {
        if (player < 1 || player > 10000) throw new ArgumentOutOfRangeException(nameof(player));
        var job = await db.FastJobs.SingleAsync(x => x.Id == jobId, token);
        if (job.JobType != CaptureJobType.PrintOnSiteTeamAndIndividual) throw new InvalidOperationException("This job does not use player slots.");
        if (!await ScanAsync(jobId, token)) throw new IOException("An incoming photo is still being written. Wait before switching players.");
        var team = await db.FastTeams.SingleAsync(x => x.Id == job.CurrentTeamId, token);
        if (team.CurrentPlayer == player) { await RepairPrimaryFilenameAsync(jobId, token); return; }
        await NextPlayerAsync(jobId, token);
        team.CurrentPlayer = player;
        await db.SaveChangesAsync(token);
        await RepairPrimaryFilenameAsync(jobId, token);
    }

    public async Task RepairPrimaryFilenameAsync(string jobId, CancellationToken token = default)
    {
        var job = await db.FastJobs.SingleAsync(x => x.Id == jobId, token);
        if (job.JobType != CaptureJobType.PrintOnSiteTeamAndIndividual || job.CurrentTeamId is null) return;
        var team = await db.FastTeams.SingleAsync(x => x.Id == job.CurrentTeamId, token);
        var primary = await db.FastImages.SingleOrDefaultAsync(x => x.TeamId == team.Id && x.PlayerNumber == team.CurrentPlayer && x.IsPrimary && !x.IsRejected, token);
        if (primary is null) return;
        if (!Directory.Exists(job.IndividualsFolder)) throw new IOException("Individual output is unavailable; primary filename repair remains pending.");
        // Repair only the selected player's individual copy. Do not alter release
        // state or republish the team copy while an operator is reviewing a player.
        primary.IndividualDeliveredPath = await ReconcileCopyAsync(primary, primary.IndividualDeliveredPath,
            IndividualTarget(job, team, primary), token, job.IndividualsFolder);
        await db.SaveChangesAsync(token);
    }

    public async Task<FastRosterImportResult> ImportRosterAsync(FastRosterImportCommand command, CancellationToken token = default)
    {
        var job = await db.FastJobs.SingleAsync(x => x.Id == command.JobId, token);
        var plan = FastRosterPlan.Read(command);
        var entries = plan.Entries;
        var teams = await db.FastTeams.Where(x => x.JobId == job.Id).ToListAsync(token);
        var photographed = (await db.FastImages.Where(x => x.JobId == job.Id).Select(x => x.TeamId).Distinct().ToListAsync(token)).ToHashSet();
        var importedIds = new HashSet<string>();
        var rank = 0;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        foreach (var entry in entries)
        {
            var team = teams.FirstOrDefault(x => x.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase) && x.PerformanceKey == entry.Key);
            // Adopt an older group-in-name entry only when it maps to exactly one
            // imported performance. Never guess which division owns existing photos.
            if (team is null)
            {
                var legacy = teams.Where(x => x.Details.Length == 0 && x.Day.Length == 0 && x.Hall.Length == 0 && x.PerformanceKey == entry.Order.ToUpperInvariant()
                    && entry.LegacyNames.Contains(x.Name, StringComparer.OrdinalIgnoreCase) && !importedIds.Contains(x.Id)
                    && entries.Count(e => e.Order.Equals(entry.Order, StringComparison.OrdinalIgnoreCase)
                        && e.LegacyNames.Contains(x.Name, StringComparer.OrdinalIgnoreCase)) == 1).ToArray();
                if (legacy.Length == 1) team = legacy[0];
            }
            if (team is null)
            {
                team = new FastCaptureTeam { JobId = job.Id };
                teams.Add(team); db.FastTeams.Add(team);
            }
            team.Name = entry.Name; team.PerformanceKey = entry.Key; team.Details = entry.Details;
            team.SortOrder = ++rank; team.PerformanceLabel = entry.Order; team.Day = entry.Day; team.Hall = entry.Hall;
            // Once any photo belongs to this performance, never move or reissue its
            // watched outputs merely because a roster is reimported or upgraded.
            if (!photographed.Contains(team.Id)) team.FolderName = PerformanceFolder(team, teams);
            importedIds.Add(team.Id);
        }
        foreach (var extra in teams.Where(x => !importedIds.Contains(x.Id)).OrderBy(x => x.SortOrder)) extra.SortOrder = ++rank;
        db.FastRosterImports.Add(new FastCaptureRosterImport
        {
            JobId = job.Id, SourceName = command.SourceName, Sha256 = Convert.ToHexString(SHA256.HashData(command.Content)),
            SourceDataJson = JsonSerializer.Serialize(plan.Raw),
            MappingJson = JsonSerializer.Serialize(new { command.TeamColumn, command.GroupColumn, command.OrderColumn, command.DetailColumns, command.HeaderRecordNumber, command.DayColumn, command.HallColumn, command.EntryTypeColumn })
        });
        job.CurrentTeamId ??= teams.OrderBy(x => x.SortOrder).First().Id;
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return new(entries.Count, plan.Rows);
    }

    private static string PerformanceFolder(FastCaptureTeam team, IReadOnlyList<FastCaptureTeam> teams)
    {
        var order = team.PerformanceLabel.Length > 0 ? team.PerformanceLabel : team.SortOrder.ToString("000", CultureInfo.InvariantCulture);
        if (long.TryParse(order, NumberStyles.None, CultureInfo.InvariantCulture, out var number)) order = number.ToString("000", CultureInfo.InvariantCulture);
        var folder = JobMediaService.SafeName(string.Join("_", new[] { team.Day, team.Hall, order, team.Name }.Where(v => v.Length > 0)));
        bool Taken(string candidate) => teams.Any(t => t.Id != team.Id && t.FolderName.Equals(candidate, StringComparison.OrdinalIgnoreCase));
        if (Taken(folder) && team.Details.Length > 0)
            folder = JobMediaService.SafeName(folder + "_" + team.Details.Replace(" · ", "_"));
        if (Taken(folder)) folder = JobMediaService.SafeName(folder[..Math.Min(folder.Length, 80)] + "_" + team.Id[^8..]);
        return folder;
    }

    private static bool IsJpeg(string path) => Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    private static string VersionKey(FileInfo info) => $"{info.FullName.ToUpperInvariant()}|{info.LastWriteTimeUtc.Ticks}|{info.Length}";

    private async Task DeliverAsync(FastCaptureJob job, CancellationToken token)
    {
        var images = await db.FastImages.Where(x => x.JobId == job.Id && x.NeedsDelivery).OrderBy(x => x.Sequence).ToListAsync(token);
        var teams = await db.FastTeams.Where(x => x.JobId == job.Id).ToDictionaryAsync(x => x.Id, token);
        foreach (var image in images)
        {
            var team = teams[image.TeamId];
            var stem = ImageStem(team, image);
            string? target;
            if (image.IsRejected) target = Path.Combine(RejectRoot(job.OutputFolder), team.FolderName, stem + ".jpg");
            else if (job.JobType != CaptureJobType.PrintOnSiteTeamAndIndividual)
                target = Path.Combine(job.OutputFolder, team.FolderName, stem + ".jpg");
            else if (image.IsPrimary)
                target = image.Released ? Path.Combine(job.OutputFolder, team.FolderName, stem + "_primary.jpg") : null;
            else target = null;
            // The individual primary is marked immediately. The team copy still
            // waits until the operator completes/leaves this player.
            var individual = job.JobType == CaptureJobType.PrintOnSiteTeamAndIndividual && !image.IsRejected
                ? IndividualTarget(job, team, image) : null;
            image.IndividualDeliveredPath = await ReconcileCopyAsync(image, image.IndividualDeliveredPath, individual, token, job.IndividualsFolder);
            await db.SaveChangesAsync(token); // Acknowledge this destination independently.
            image.DeliveredPath = await ReconcileCopyAsync(image, image.DeliveredPath, target, token);
            image.NeedsDelivery = false;
            await db.SaveChangesAsync(token);
        }
    }

    private static string ImageStem(FastCaptureTeam team, FastCaptureImage image) =>
        $"{team.FolderName}{(image.PlayerNumber > 0 ? $"_Player_{image.PlayerNumber:000}" : "")}_{image.Sequence:000000}_{image.Id}";
    private static string IndividualTarget(FastCaptureJob job, FastCaptureTeam team, FastCaptureImage image) =>
        Path.Combine(job.IndividualsFolder!, team.FolderName, $"Player {image.PlayerNumber}", ImageStem(team, image) + (image.IsPrimary ? "_Primary" : "") + ".jpg");

    private static async Task<string?> ReconcileCopyAsync(FastCaptureImage image, string? previous, string? target, CancellationToken token, string? destinationRoot = null)
    {
        // An acknowledged same-path copy may have been consumed. Do not publish it again.
        if (string.Equals(target, previous, StringComparison.OrdinalIgnoreCase))
        {
            // Older installations can acknowledge the marked name while a bare copy
            // remains on disk. Reconcile that owned file, without restoring a consumed copy.
            if (destinationRoot is not null && target is not null && image.IsPrimary)
            {
                var bare = Path.Combine(Path.GetDirectoryName(target)!, WithoutPrimaryMarker(target) + Path.GetExtension(target));
                if (!string.Equals(bare, target, StringComparison.OrdinalIgnoreCase) && File.Exists(bare))
                    return await ReconcileCopyAsync(image, bare, target, token, destinationRoot);
            }
            return target;
        }
        // Role-only filename changes rename a still-present copy atomically. If
        // a watcher already consumed it, update the acknowledgement without reissuing it.
        if (target is not null && previous is not null &&
            string.Equals(Path.GetDirectoryName(target), Path.GetDirectoryName(previous), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(WithoutPrimaryMarker(target), WithoutPrimaryMarker(previous), StringComparison.OrdinalIgnoreCase))
        {
            if (destinationRoot is not null && !Directory.Exists(destinationRoot))
                throw new IOException($"Individual destination is unavailable; filename change remains pending: {destinationRoot}");
            if (File.Exists(target) && await HashAsync(target, token) != image.Sha256)
                throw new IOException($"Destination file differs; it was not overwritten: {target}");
            if (File.Exists(previous))
            {
                if (await HashAsync(previous, token) != image.Sha256)
                    throw new IOException($"Previously delivered image changed; it was not renamed: {previous}");
                if (File.Exists(target)) File.Delete(previous);
                else File.Move(previous, target);
            }
            return target;
        }
        if (target is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target))
            {
                if (await HashAsync(target, token) != image.Sha256) throw new IOException($"Destination file differs; it was not overwritten: {target}");
            }
            else { await using var source = File.OpenRead(image.StoredPath); await CopyNewAsync(source, target, token); }
        }
        if (previous is not null && File.Exists(previous))
        {
            if (await HashAsync(previous, token) != image.Sha256) throw new IOException($"Previously delivered image changed; it was not removed: {previous}");
            File.Delete(previous);
        }
        return target;
    }

    private static string WithoutPrimaryMarker(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        return stem.EndsWith("_Primary", StringComparison.OrdinalIgnoreCase) ? stem[..^8] : stem;
    }

    private static string RejectRoot(string output) => Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(output))
        ?? throw new ArgumentException("Choose an output folder below a drive or share root."), "Rejects");
    private static bool Inside(string path, string parent) => string.Equals(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(parent), StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var source = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(source, token));
    }
    private static async Task CopyNewAsync(Stream source, string target, CancellationToken token)
    {
        var temporary = target + ".partial";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, true))
            { await source.CopyToAsync(output, token); await output.FlushAsync(token); }
            File.Move(temporary, target);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
