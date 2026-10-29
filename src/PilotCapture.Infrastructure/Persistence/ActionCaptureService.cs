using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PilotCapture.Application.FastCapture;
using PilotCapture.Domain;

namespace PilotCapture.Infrastructure.Persistence;

public sealed class ActionCaptureService(PilotCaptureDbContext db, string mediaRoot) : IActionCaptureService
{
    public const string ClockOrigin = "PilotCapture.ClockCheck:";
    public static string PathKey(string path) => Path.GetFullPath(path).ToUpperInvariant();
    public static bool TryCameraTime(string value, out long ticks)
    {
        var ok = DateTime.TryParseExact(value, ["yyyyMMdd HH:mm:ss.fff", "yyyyMMdd HH:mm:ss", "yyyy:MM:dd HH:mm:ss.fff", "yyyy:MM:dd HH:mm:ss"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var time);
        ticks = ok ? time.Ticks : 0; return ok;
    }

    public async Task EnableAsync(string jobId, long utcTicks, CancellationToken token = default)
    {
        var job = await db.FastJobs.SingleAsync(j => j.Id == jobId, token);
        if (job.JobType != CaptureJobType.ActionPhotography) throw new InvalidOperationException("Choose an Action job.");
        job.ActionTimingEnabled = true;
        if (job.CurrentTeamId is not null && !await db.ActionChangeovers.AnyAsync(c => c.JobId == jobId, token))
            db.ActionChangeovers.Add(new() { JobId = jobId, TeamId = job.CurrentTeamId, UtcTicks = utcTicks });
        await db.SaveChangesAsync(token);
    }

    public async Task ReceiveSmartShooterAsync(string jobId, string json, long receivedUtcTicks, CancellationToken token = default)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var job = await db.FastJobs.AsNoTracking().SingleAsync(j => j.Id == jobId, token);
        if (!job.ActionTimingEnabled) return;
        static string Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        var cameras = await db.ActionCameras.Where(c => c.JobId == jobId).ToListAsync(token);
        var packetKeys = new List<string>();
        if (root.TryGetProperty("PhotoInfo", out var info) && info.ValueKind == JsonValueKind.Array)
            packetKeys.AddRange(info.EnumerateArray().Select(p => Text(p, "PhotoKey")));
        var packetKey = Text(root, "PhotoKey"); if (packetKey.Length > 0) packetKeys.Add(packetKey);
        var photos = await db.ActionPhotoMetadatas.Where(p => p.JobId == jobId && packetKeys.Contains(p.PhotoKey)).ToDictionaryAsync(p => p.PhotoKey, token);
        async Task Camera(JsonElement item)
        {
            var key = Text(item, "CameraKey"); if (key.Length == 0) return;
            var camera = cameras.FirstOrDefault(c => c.CameraKey == key);
            if (camera is null) { camera = new() { JobId = jobId, CameraKey = key }; cameras.Add(camera); db.ActionCameras.Add(camera); }
            var name = Text(item, "CameraName"); if (name.Length > 0) camera.Name = name;
            var status = Text(item, "CameraStatus");
            if (status.Length > 0)
            {
                if (camera.Status is "Ready" or "Busy" && status is not ("Ready" or "Busy"))
                    await InvalidateCameraAsync(jobId, key, receivedUtcTicks, token);
                camera.Status = status;
            }
            camera.LastSeenUtcTicks = receivedUtcTicks;
        }
        void Photo(JsonElement item)
        {
            var key = Text(item, "PhotoKey"); if (key.Length == 0) return;
            if (!photos.TryGetValue(key, out var photo))
            {
                photo = new() { JobId = jobId, PhotoKey = key, FirstSeenUtcTicks = receivedUtcTicks };
                photos.Add(key, photo); db.ActionPhotoMetadatas.Add(photo);
            }
            var cameraKey = Text(item, "CameraKey");
            if (cameraKey.Length > 0)
            {
                if (photo.CameraKey.Length > 0 && photo.CameraKey != cameraKey) photo.HasConflict = true;
                else photo.CameraKey = cameraKey;
            }
            var path = Text(item, "PhotoFilename");
            if (path.Length > 0)
            {
                try
                {
                    var full = Path.GetFullPath(path);
                    if (Path.GetDirectoryName(full)?.Equals(Path.GetFullPath(job.SourceFolder), StringComparison.OrdinalIgnoreCase) == true)
                    {
                        var keyPath = PathKey(full);
                        if (photo.PathKey.Length > 0 && photo.PathKey != keyPath) photo.HasConflict = true;
                        else photo.PathKey = keyPath;
                    }
                }
                catch (ArgumentException) { /* Wait for a valid full local path. */ }
            }
            if (TryCameraTime(Text(item, "PhotoDateCaptured"), out var ticks))
            {
                if (photo.CameraTicks is { } prior && prior != ticks) photo.HasConflict = true;
                else photo.CameraTicks = ticks;
            }
            var origin = Text(item, "PhotoOrigin"); if (origin.Length > 0) photo.Origin = origin;
            var location = Text(item, "PhotoLocation"); if (location.Length > 0) photo.Location = location;
            var camera = cameras.FirstOrDefault(c => c.CameraKey == photo.CameraKey);
            if (camera is not null && Text(root, "msg_id") == "PhotoUpdated") camera.LastPhotoUtcTicks = receivedUtcTicks;
        }
        if (root.TryGetProperty("CameraInfo", out var cameraInfo) && cameraInfo.ValueKind == JsonValueKind.Array)
            foreach (var c in cameraInfo.EnumerateArray()) await Camera(c);
        if (root.TryGetProperty("PhotoInfo", out var photoInfo) && photoInfo.ValueKind == JsonValueKind.Array)
            foreach (var p in photoInfo.EnumerateArray()) Photo(p);
        if (Text(root, "msg_id") == "CameraUpdated") await Camera(root);
        if (Text(root, "msg_id") == "PhotoUpdated") Photo(root);
        await db.SaveChangesAsync(token);
    }

    public async Task InvalidateCameraAsync(string jobId, string cameraKey, long utcTicks, CancellationToken token = default)
    {
        foreach (var sample in await db.ActionClockSamples.Where(c => c.JobId == jobId && c.CameraKey == cameraKey && c.UntilUtcTicks > utcTicks).ToListAsync(token))
            sample.UntilUtcTicks = Math.Max(sample.FromUtcTicks, utcTicks);
        var camera = await db.ActionCameras.FirstOrDefaultAsync(c => c.JobId == jobId && c.CameraKey == cameraKey, token);
        if (camera is not null) camera.Issue = "Camera reconnected or disconnected; clock verification needed";
        await db.SaveChangesAsync(token);
    }

    public async Task VerifyClockAsync(string jobId, string photoKey, long requestUtcTicks, long observedUtcTicks, CancellationToken token = default)
    {
        var photo = await db.ActionPhotoMetadatas.SingleAsync(p => p.JobId == jobId && p.PhotoKey == photoKey, token);
        if (!photo.Origin.StartsWith(ClockOrigin, StringComparison.Ordinal) || photo.CameraTicks is null || photo.CameraKey.Length == 0 || photo.HasConflict)
            throw new ArgumentException("A marked SS clock verification photo with capture time and camera identity is required.");
        var elapsed = observedUtcTicks - requestUtcTicks;
        if (elapsed < 0 || elapsed > TimeSpan.FromSeconds(2).Ticks)
            throw new InvalidOperationException("Clock test took too long. Let transfers finish, then verify again.");
        if (await db.ActionClockSamples.AnyAsync(s => s.JobId == jobId && s.VerificationPhotoKey == photoKey, token)) return;
        var offset = requestUtcTicks + elapsed / 2 - photo.CameraTicks.Value;
        var uncertainty = elapsed / 2 + TimeSpan.FromSeconds(1).Ticks;
        var previous = await db.ActionClockSamples.Where(s => s.JobId == jobId && s.CameraKey == photo.CameraKey).OrderByDescending(s => s.FromUtcTicks).FirstOrDefaultAsync(token);
        var changed = previous is not null && Math.Abs((double)offset - previous.OffsetTicks) > uncertainty + previous.UncertaintyTicks + TimeSpan.FromSeconds(2).Ticks;
        if (previous is not null)
            previous.UntilUtcTicks = Math.Min(previous.UntilUtcTicks, changed ? previous.FromUtcTicks : requestUtcTicks);
        db.ActionClockSamples.Add(new() { JobId = jobId, CameraKey = photo.CameraKey, FromUtcTicks = observedUtcTicks,
            UntilUtcTicks = observedUtcTicks + TimeSpan.FromHours(2).Ticks, OffsetTicks = offset, UncertaintyTicks = uncertainty, VerificationPhotoKey = photoKey });
        var camera = await db.ActionCameras.SingleAsync(c => c.JobId == jobId && c.CameraKey == photo.CameraKey, token);
        camera.Issue = changed ? "Clock changed: new captures verified; older unassigned photos need review" : "";
        await db.SaveChangesAsync(token);
    }

    public async Task<ActionCaptureStatus> GetStatusAsync(string jobId, CancellationToken token = default)
    {
        var teams = await db.FastTeams.AsNoTracking().Where(t => t.JobId == jobId).ToDictionaryAsync(t => t.Id, token);
        var counts = await db.FastImages.AsNoTracking().Where(p => p.JobId == jobId).GroupBy(p => new { p.TeamId, p.NeedsDelivery })
            .Select(g => new { g.Key.TeamId, g.Key.NeedsDelivery, Count = g.Count() }).ToListAsync(token);
        var progress = counts.GroupBy(c => c.TeamId).Select(g => new ActionTeamProgress(teams[g.Key].Name,
            g.Sum(c => c.Count), g.Where(c => c.NeedsDelivery).Sum(c => c.Count), g.Where(c => !c.NeedsDelivery).Sum(c => c.Count))).ToArray();
        return new(await db.ActionCameras.AsNoTracking().Where(c => c.JobId == jobId).OrderBy(c => c.Name).ToListAsync(token),
            await db.ActionClockSamples.AsNoTracking().Where(c => c.JobId == jobId).ToListAsync(token),
            await db.ActionReceipts.AsNoTracking().Where(r => r.JobId == jobId && r.State == "Held").OrderBy(r => r.Id).Take(100).ToListAsync(token),
            await db.ActionReceipts.CountAsync(r => r.JobId == jobId && r.State == "Held", token), progress);
    }

    public Task<bool> HasCameraTransfersAsync(string jobId, CancellationToken token = default) =>
        db.ActionPhotoMetadatas.AnyAsync(p => p.JobId == jobId && p.Location == "Camera", token);

    public async Task AssignHeldAsync(string jobId, string receiptId, string teamId, CancellationToken token = default)
    {
        var receipt = await db.ActionReceipts.SingleAsync(r => r.JobId == jobId && r.Id == receiptId, token);
        if (receipt.State != "Held") throw new InvalidOperationException("Only a held photo can be assigned.");
        if (!await db.FastTeams.AnyAsync(t => t.JobId == jobId && t.Id == teamId, token)) throw new ArgumentException("Choose a performance from this job.");
        await RouteAsync(receipt, teamId, token);
    }

    private async Task RouteAsync(ActionReceipt receipt, string teamId, CancellationToken token)
    {
        if (await db.FastImages.AnyAsync(i => i.Id == receipt.Id, token)) return;
        var sequence = (await db.FastImages.Where(i => i.TeamId == teamId).Select(i => (int?)i.Sequence).MaxAsync(token) ?? 0) + 1;
        db.FastImages.Add(new() { Id = receipt.Id, JobId = receipt.JobId, TeamId = teamId, PlayerNumber = 0, Sequence = sequence,
            SourcePath = receipt.SourcePath, SourceTicks = receipt.SourceTicks, SourceLength = receipt.SourceLength, StoredPath = receipt.StoredPath, Sha256 = receipt.Sha256 });
        receipt.TeamId = teamId; receipt.State = "Assigned"; receipt.Issue = "";
        await db.SaveChangesAsync(token);
    }

    // Archive a bounded batch. A slow output destination never owns the changeover lock.
    public async Task<bool> ScanAsync(FastCaptureJob job, CancellationToken token, IReadOnlyList<string>? paths = null)
    {
        var incoming = (paths ?? Directory.GetFiles(job.SourceFolder)).Where(File.Exists).Where(p =>
            Path.GetDirectoryName(Path.GetFullPath(p))?.Equals(Path.GetFullPath(job.SourceFolder), StringComparison.OrdinalIgnoreCase) == true &&
            (Path.GetExtension(p).Equals(".jpg", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(p).Equals(".jpeg", StringComparison.OrdinalIgnoreCase))).ToArray();
        var keys = incoming.Select(PathKey).ToArray();
        var legacy = await db.FastImages.AsNoTracking().Where(i => i.JobId == job.Id && incoming.Contains(i.SourcePath)).Select(i => new { i.SourcePath, i.SourceTicks, i.SourceLength }).ToListAsync(token);
        var known = (await db.ActionReceipts.AsNoTracking().Where(r => r.JobId == job.Id && keys.Contains(r.PathKey)).Select(r => new { r.SourcePath, r.SourceTicks, r.SourceLength }).ToListAsync(token))
            .Select(r => $"{PathKey(r.SourcePath)}|{r.SourceTicks}|{r.SourceLength}").ToHashSet();
        foreach (var i in legacy) known.Add($"{PathKey(i.SourcePath)}|{i.SourceTicks}|{i.SourceLength}");
        var ignored = job.IgnoredSourceVersionsJson is { } baseline ? JsonSerializer.Deserialize<HashSet<string>>(baseline)! : [];
        var files = incoming
            .Select(p => new FileInfo(p)).Where(f => !known.Contains($"{PathKey(f.FullName)}|{f.LastWriteTimeUtc.Ticks}|{f.Length}")
                && !ignored.Contains($"{PathKey(f.FullName)}|{f.LastWriteTimeUtc.Ticks}|{f.Length}"))
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Take(9).ToArray();
        var settled = files.Length <= 8;
        foreach (var file in files.Take(8))
        {
            token.ThrowIfCancellationRequested();
            var receipt = new ActionReceipt { JobId = job.Id, SourcePath = file.FullName, PathKey = PathKey(file.FullName), SourceTicks = file.LastWriteTimeUtc.Ticks, SourceLength = file.Length };
            var directory = Path.Combine(mediaRoot, "fast", job.Id); Directory.CreateDirectory(directory);
            receipt.StoredPath = Path.Combine(directory, receipt.Id + ".jpg");
            try
            {
                await using var source = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
                file.Refresh();
                if (source.Length < 4 || source.Length != receipt.SourceLength || file.LastWriteTimeUtc.Ticks != receipt.SourceTicks) { settled = false; continue; }
                var header = new byte[3]; await source.ReadExactlyAsync(header, token);
                if (header[0] != 255 || header[1] != 216 || header[2] != 255) { settled = false; continue; }
                source.Position = 0;
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using (var destination = new FileStream(receipt.StoredPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true))
                {
                    var buffer = new byte[128 * 1024]; int count;
                    while ((count = await source.ReadAsync(buffer, token)) > 0)
                    { hash.AppendData(buffer, 0, count); await destination.WriteAsync(buffer.AsMemory(0, count), token); }
                    await destination.FlushAsync(token);
                }
                receipt.Sha256 = Convert.ToHexString(hash.GetHashAndReset());
                db.ActionReceipts.Add(receipt); await db.SaveChangesAsync(token);
            }
            catch (IOException) { if (File.Exists(receipt.StoredPath)) File.Delete(receipt.StoredPath); settled = false; }
        }
        var held = await db.ActionReceipts.Where(r => r.JobId == job.Id && r.State == "Held").ToListAsync(token);
        var heldKeys = held.Select(r => r.PathKey).ToArray();
        var metadata = await db.ActionPhotoMetadatas.AsNoTracking().Where(m => m.JobId == job.Id && heldKeys.Contains(m.PathKey)).ToListAsync(token);
        var clocks = await db.ActionClockSamples.AsNoTracking().Where(c => c.JobId == job.Id).ToListAsync(token);
        var changes = await db.ActionChangeovers.AsNoTracking().Where(c => c.JobId == job.Id).ToListAsync(token);
        foreach (var receipt in held)
        {
            var candidates = metadata.Where(m => m.PathKey == receipt.PathKey).ToArray();
            if (candidates.Length != 1) { receipt.Issue = candidates.Length == 0 ? "Waiting for SS metadata" : "Filename identifies multiple SS photos; select performance"; continue; }
            var photo = candidates[0]; receipt.PhotoKey = photo.PhotoKey; receipt.CameraKey = photo.CameraKey; receipt.CameraTicks = photo.CameraTicks;
            if (await db.ActionReceipts.AnyAsync(r => r.JobId == job.Id && r.Id != receipt.Id && r.PhotoKey == photo.PhotoKey, token))
            { receipt.Issue = "Source filename reused; select the correct performance"; continue; }
            if (photo.Origin.StartsWith(ClockOrigin, StringComparison.Ordinal) || clocks.Any(c => c.VerificationPhotoKey == photo.PhotoKey)) { receipt.State = "Calibration"; receipt.Issue = "Clock test: excluded from team output"; continue; }
            if (photo.HasConflict) { receipt.Issue = "SS metadata changed identity, path or capture time; select performance"; continue; }
            if (photo.CameraTicks is null) { receipt.Issue = "Missing original capture time"; continue; }
            var match = ActionTimeRouting.Resolve(photo.CameraTicks.Value, clocks.Where(c => c.CameraKey == photo.CameraKey).ToArray(), changes);
            receipt.CorrectedUtcTicks = match.UtcTicks; receipt.ClockSampleId = match.SampleId; receipt.Issue = match.Issue;
            if (match.TeamId is { } teamId) await RouteAsync(receipt, teamId, token);
        }
        await db.SaveChangesAsync(token);
        return settled;
    }
}
