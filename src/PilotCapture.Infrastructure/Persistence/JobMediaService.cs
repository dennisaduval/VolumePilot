using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PilotCapture.Application;
using PilotCapture.Application.Rosters;
using PilotCapture.Domain;

namespace PilotCapture.Infrastructure.Persistence;

public sealed class JobMediaService(PilotCaptureDbContext db, IImageAssetStore store) : IJobMediaService
{
    public async Task<string?> GetMasterPathAsync(CancellationToken cancellationToken = default) =>
        (await db.LocalInstallations.AsNoTracking().SingleAsync(cancellationToken)).MasterMediaPath;

    public async Task SetMasterPathAsync(string path, CancellationToken cancellationToken = default)
    {
        var full = Path.GetFullPath(path);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException("Choose an existing master VP database folder.");
        if (full.Length > 1800) throw new ArgumentException("The master folder path is too long.");
        var installation = await db.LocalInstallations.SingleAsync(cancellationToken);
        installation.MasterMediaPath = full;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<JobMediaResult> PublishOriginalsAsync(string eventId, CancellationToken cancellationToken = default)
    {
        var master = await RequireMasterAsync(cancellationToken);
        var folder = Path.Combine(master, "Original Images", eventId);
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.Combine(master, "Extracted Images", eventId));
        var images = await GetImagesAsync(eventId, cancellationToken);
        foreach (var image in images)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var asset = image.ImageAsset!;
            var target = Path.Combine(folder, asset.Id + ".jpg");
            if (File.Exists(target))
            {
                if (!string.Equals(await HashAsync(target, cancellationToken), asset.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"Master original differs from its recorded checksum: {target}");
                continue;
            }
            await using var source = await store.OpenReadAsync(asset.RelativePath, cancellationToken);
            await CopyNewAsync(source, target, cancellationToken);
        }
        return new(images.Count, folder);
    }

    public async Task<JobMediaResult> AssociateEditedAsync(string eventId, CancellationToken cancellationToken = default)
    {
        var master = await RequireMasterAsync(cancellationToken);
        var folder = Path.Combine(master, "Extracted Images", eventId);
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"Edited images folder is unavailable: {folder}");
        var images = await GetImagesAsync(eventId, cancellationToken);
        var count = 0;
        foreach (var image in images)
        {
            var asset = image.ImageAsset!;
            var edited = Path.Combine(folder, asset.Id + ".png");
            if (!File.Exists(edited)) continue;
            await ValidatePngAsync(edited, cancellationToken);
            asset.EditedPath = edited;
            asset.EditedSha256 = await HashAsync(edited, cancellationToken);
            asset.EditedAtUtc = DateTimeOffset.UtcNow;
            count++;
        }
        await db.SaveChangesAsync(cancellationToken);
        return new(count, folder);
    }

    public async Task<JobMediaResult> ExportAsync(string eventId, string destination, JobImageExportKind kind, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var job = await db.Events.AsNoTracking().SingleAsync(x => x.Id == eventId, cancellationToken);
        var images = (await GetImagesAsync(eventId, cancellationToken))
            .Where(x => x.ReviewState != CaptureImageReviewState.Rejected).ToList();
        if (images.Count == 0) throw new InvalidOperationException("This job has no non-rejected images to export.");
        // Preflight every file before creating an output folder. Never silently substitute originals.
        foreach (var image in images)
        {
            var asset = image.ImageAsset!;
            if (kind == JobImageExportKind.EditedPng)
            {
                if (asset.EditedPath is null || !File.Exists(asset.EditedPath))
                    throw new InvalidOperationException($"Edited PNG missing for {asset.OriginalFileName}. Associate edited images first.");
                if (!string.Equals(await HashAsync(asset.EditedPath, cancellationToken), asset.EditedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Edited PNG changed for {asset.OriginalFileName}. Associate edited images again.");
            }
            else if (!store.Exists(asset.RelativePath)) throw new FileNotFoundException($"Original missing: {asset.OriginalFileName}");
        }
        var root = Path.Combine(Path.GetFullPath(destination), SafeName(job.Name) + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Ids.New()[^6..]);
        Directory.CreateDirectory(root);
        try
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var teamFolders = new Dictionary<string, string>(StringComparer.Ordinal);
            var usedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rowsByFolder = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.Ordinal);
            foreach (var image in images)
            {
                var asset = image.ImageAsset!;
                var set = image.CaptureSet!;
                var member = set.Membership;
                var subject = set.Subject!;
                var fields = member?.SpaDataJson is { } json
                    ? JsonSerializer.Deserialize<Dictionary<string, string>>(json)! : SpaRosterFields.ReadSource(member?.SourceDataJson);
                fields["FIRSTNAME"] = subject.FirstName ?? fields.GetValueOrDefault("FIRSTNAME", "");
                fields["LASTNAME"] = subject.LastName ?? fields.GetValueOrDefault("LASTNAME", "");
                fields.TryAdd("NAME", subject.DisplayName);
                fields["NUMBER"] = member?.RosterNumber ?? fields.GetValueOrDefault("NUMBER", "");
                if (!fields.ContainsKey("CLASS")) fields.TryAdd("TEAMNAME", member?.Group?.Name ?? "");
                var groupKey = member?.GroupId ?? "unassigned";
                if (!teamFolders.TryGetValue(groupKey, out var teamFolder))
                {
                    teamFolder = SafeName(member?.Group?.Name ?? "Unassigned");
                    var baseFolder = teamFolder;
                    for (var n = 2; !usedFolders.Add(teamFolder); n++) teamFolder = baseFolder + "_" + n;
                    teamFolders.Add(groupKey, teamFolder);
                }
                var ext = kind == JobImageExportKind.EditedPng ? ".png" : ".jpg";
                var team = fields.GetValueOrDefault("TEAMNAME", "");
                if (string.IsNullOrWhiteSpace(team)) team = fields.GetValueOrDefault("CLASS", "");
                var parts = new[] { fields["FIRSTNAME"], fields["LASTNAME"], team, fields["NUMBER"] }.Where(x => !string.IsNullOrWhiteSpace(x));
                var stem = SafeName(string.Join("_", parts));
                if (stem == "Unnamed") stem = SafeName(subject.DisplayName);
                var filename = kind == JobImageExportKind.BatchEditingOriginals ? asset.Id + ext : stem + ext;
                var baseName = filename;
                for (var n = 2; !names.Add(teamFolder + "/" + filename); n++) filename = Path.GetFileNameWithoutExtension(baseName) + "_" + n.ToString("00") + ext;
                fields["SPA"] = filename;
                var folders = kind == JobImageExportKind.BatchEditingOriginals
                    ? new[] { "original photos" }
                    : image.IsPrimary ? new[] { Path.Combine("All Images", teamFolder), Path.Combine("Team Photo Images", teamFolder) }
                        : new[] { Path.Combine("All Images", teamFolder) };
                foreach (var folder in folders)
                {
                    var absoluteFolder = Path.Combine(root, folder);
                    Directory.CreateDirectory(absoluteFolder);
                    var target = Path.Combine(absoluteFolder, filename);
                    await using var source = kind == JobImageExportKind.EditedPng
                        ? File.OpenRead(asset.EditedPath!) : await store.OpenReadAsync(asset.RelativePath, cancellationToken);
                    await CopyNewAsync(source, target, cancellationToken);
                    if (!rowsByFolder.TryGetValue(folder, out var rows)) rowsByFolder[folder] = rows = [];
                    rows.Add(new(fields));
                }
            }
            foreach (var (folder, rows) in rowsByFolder)
            {
                var columns = new[] { "SPA" }.Concat(SpaRosterFields.Columns.Where(c => rows.Any(r => !string.IsNullOrWhiteSpace(r.GetValueOrDefault(c))))).ToArray();
                await using var output = new StreamWriter(Path.Combine(root, folder, "SPA.csv"), false, new UTF8Encoding(false));
                await output.WriteLineAsync(string.Join(",", columns));
                foreach (var row in rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await output.WriteLineAsync(string.Join(",", columns.Select(c => Csv(row.GetValueOrDefault(c, "")))));
                }
            }
            return new(images.Count, root);
        }
        catch
        {
            Directory.Delete(root, true);
            throw;
        }
    }

    private async Task<List<CaptureImage>> GetImagesAsync(string eventId, CancellationToken token) =>
        await db.CaptureImages.Include(x => x.ImageAsset).Include(x => x.CaptureSet!).ThenInclude(x => x.Subject)
            .Include(x => x.CaptureSet!).ThenInclude(x => x.Membership!).ThenInclude(x => x.Group)
            .Where(x => x.CaptureSet!.CaptureSession!.EventId == eventId).OrderBy(x => x.Id).ToListAsync(token);

    private async Task<string> RequireMasterAsync(CancellationToken token) =>
        await GetMasterPathAsync(token) ?? throw new InvalidOperationException("Choose the master VP database folder first.");

    public static string SafeName(string value)
    {
        var safe = new string(value.Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        if (safe.Length > 100) safe = safe[..100].TrimEnd(' ', '.');
        if (safe.Length == 0) return "Unnamed";
        var stem = safe.Split('.')[0].ToUpperInvariant();
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem)) safe = "_" + safe;
        return safe;
    }
    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    private static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
    }
    private static async Task ValidatePngAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        var header = new byte[8];
        await stream.ReadExactlyAsync(header, token);
        if (!header.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new InvalidDataException($"Not a PNG: {path}");
    }
    private static async Task CopyNewAsync(Stream source, string target, CancellationToken token)
    {
        var temporary = target + "." + Ids.New() + ".tmp";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true))
            {
                await source.CopyToAsync(output, token);
                await output.FlushAsync(token);
            }
            File.Move(temporary, target);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
