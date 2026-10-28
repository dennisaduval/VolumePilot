using System.Security.Cryptography;
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
        var teams = await db.FastTeams.AsNoTracking().Where(x => x.JobId == jobId).OrderBy(x => x.Name).ToListAsync(token);
        var images = await db.FastImages.AsNoTracking().Where(x => x.JobId == jobId && x.TeamId == job.CurrentTeamId)
            .OrderByDescending(x => x.Sequence).Take(100).ToListAsync(token);
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
            OutputFolder = output, IndividualsFolder = individual, StartedAtTicks = DateTime.UtcNow.Ticks };
        db.FastJobs.Add(job);
        await db.SaveChangesAsync(token);
        return job.Id;
    }

    public async Task SelectTeamAsync(string jobId, string name, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter a team name.");
        if (!await ScanAsync(jobId, token)) throw new IOException("An incoming photo is still being written. Wait before switching teams.");
        var job = await db.FastJobs.SingleAsync(x => x.Id == jobId, token);
        var team = await db.FastTeams.SingleOrDefaultAsync(x => x.JobId == jobId && x.Name == name.Trim(), token);
        if (team?.Id == job.CurrentTeamId) return;
        if (job.CurrentTeamId is not null && job.JobType == CaptureJobType.PrintOnSiteTeamAndIndividual)
            await NextPlayerAsync(jobId, token);
        if (team is null)
        {
            var folder = JobMediaService.SafeName(name.Trim());
            if (await db.FastTeams.AnyAsync(x => x.JobId == jobId && x.FolderName == folder, token)) folder += "_" + Ids.New();
            team = new FastCaptureTeam { JobId = jobId, Name = name.Trim(), FolderName = folder };
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
        team.CurrentPlayer++;
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
        await DeliverAsync(job, token);
    }

    public async Task SetPrimaryAsync(string imageId, CancellationToken token = default)
    {
        var image = await db.FastImages.SingleAsync(x => x.Id == imageId, token);
        var job = await db.FastJobs.SingleAsync(x => x.Id == image.JobId, token);
        if (job.JobType != CaptureJobType.PrintOnSiteTeamAndIndividual || image.Released)
            throw new InvalidOperationException("Primary selection is available for the current, unfinished player only.");
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var images = await db.FastImages.Where(x => x.TeamId == image.TeamId && x.PlayerNumber == image.PlayerNumber).ToListAsync(token);
        foreach (var photo in images) { photo.IsPrimary = false; photo.NeedsDelivery = true; }
        await db.SaveChangesAsync(token);
        image.IsRejected = false; image.IsPrimary = true;
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
        if (job.JobType == CaptureJobType.PrintOnSiteTeamAndIndividual && !image.Released)
        {
            var eligible = await db.FastImages.Where(x => x.TeamId == image.TeamId && x.PlayerNumber == image.PlayerNumber && !x.IsRejected)
                .OrderBy(x => x.Sequence).ToListAsync(token);
            if (eligible.Count > 0 && !eligible.Any(x => x.IsPrimary)) { eligible[0].IsPrimary = true; eligible[0].NeedsDelivery = true; }
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
        var files = Directory.EnumerateFiles(job.SourceFolder).Where(x => Path.GetExtension(x).Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(x).Equals(".jpeg", StringComparison.OrdinalIgnoreCase)).Select(x => new FileInfo(x))
            .Where(x => x.LastWriteTimeUtc.Ticks >= job.StartedAtTicks).OrderBy(x => x.LastWriteTimeUtc).ThenBy(x => x.Name).ToArray();
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

    private async Task DeliverAsync(FastCaptureJob job, CancellationToken token)
    {
        var images = await db.FastImages.Where(x => x.JobId == job.Id && x.NeedsDelivery).OrderBy(x => x.Sequence).ToListAsync(token);
        var teams = await db.FastTeams.Where(x => x.JobId == job.Id).ToDictionaryAsync(x => x.Id, token);
        foreach (var image in images)
        {
            var team = teams[image.TeamId];
            var player = image.PlayerNumber > 0 ? $"_Player_{image.PlayerNumber:000}" : "";
            var stem = $"{team.FolderName}{player}_{image.Sequence:000000}_{image.Id}";
            string? target;
            if (image.IsRejected) target = Path.Combine(RejectRoot(job.OutputFolder), team.FolderName, stem + ".jpg");
            else if (job.JobType != CaptureJobType.PrintOnSiteTeamAndIndividual)
                target = Path.Combine(job.OutputFolder, team.FolderName, stem + ".jpg");
            else if (image.IsPrimary)
                target = image.Released ? Path.Combine(job.OutputFolder, team.FolderName, stem + "_primary.jpg") : null;
            else target = Path.Combine(job.IndividualsFolder!, team.FolderName, $"Player {image.PlayerNumber}", stem + ".jpg");
            if (string.Equals(target, image.DeliveredPath, StringComparison.OrdinalIgnoreCase))
            { image.NeedsDelivery = false; await db.SaveChangesAsync(token); continue; }
            if (target is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target))
                {
                    if (await HashAsync(target, token) != image.Sha256) throw new IOException($"Destination file differs; it was not overwritten: {target}");
                }
                else { await using var source = File.OpenRead(image.StoredPath); await CopyNewAsync(source, target, token); }
            }
            if (image.DeliveredPath is { } previous && File.Exists(previous))
            {
                if (await HashAsync(previous, token) != image.Sha256) throw new IOException($"Previously delivered image changed; it was not removed: {previous}");
                File.Delete(previous);
            }
            image.DeliveredPath = target;
            image.NeedsDelivery = false;
            await db.SaveChangesAsync(token);
        }
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
