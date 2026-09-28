using System.Security.Cryptography;
using PilotCapture.Application;
using PilotCapture.Domain;

namespace PilotCapture.Infrastructure.Files;

public sealed class FileSystemImageAssetStore(string mediaRootPath) : IImageAssetStore
{
    private readonly string _mediaRootPath = Path.GetFullPath(mediaRootPath);

    public async Task<StoredImageAsset> StoreJpegAsync(
        string eventId,
        string sessionId,
        string captureSetId,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetExtension(sourcePath), ".jpg", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Path.GetExtension(sourcePath), ".jpeg", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only JPEG files from Smart Shooter can be ingested.");

        var absoluteSourcePath = Path.GetFullPath(sourcePath);
        if (absoluteSourcePath.StartsWith(_mediaRootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The Smart Shooter output folder cannot be inside Pilot Capture's managed media folder.");
        await WaitUntilStableAsync(absoluteSourcePath, cancellationToken);
        var imageAssetId = Ids.New();
        var relativePath = Path.Combine(eventId, sessionId, captureSetId, imageAssetId + ".jpg");
        var destinationPath = Path.Combine(_mediaRootPath, relativePath);
        var destinationDirectory = Path.GetDirectoryName(destinationPath)!;
        Directory.CreateDirectory(destinationDirectory);
        var temporaryPath = destinationPath + ".tmp";

        try
        {
            await using (var source = new FileStream(absoluteSourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true))
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true))
            {
                await source.CopyToAsync(destination, cancellationToken);
                await destination.FlushAsync(cancellationToken);
            }

            var byteLength = new FileInfo(temporaryPath).Length;
            await using (var content = new FileStream(temporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
            {
                var header = new byte[3];
                if (await content.ReadAsync(header, cancellationToken) != header.Length
                    || header[0] != 0xFF || header[1] != 0xD8 || header[2] != 0xFF)
                    throw new InvalidDataException("The selected file does not contain a valid JPEG header.");
            }

            string sha256;
            await using (var content = new FileStream(temporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true))
                sha256 = Convert.ToHexString(await SHA256.HashDataAsync(content, cancellationToken));

            File.Move(temporaryPath, destinationPath);
            return new StoredImageAsset(
                imageAssetId,
                relativePath.Replace(Path.DirectorySeparatorChar, '/'),
                Path.GetFileName(absoluteSourcePath),
                byteLength,
                sha256,
                null,
                null);
        }
        catch
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
            throw;
        }
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveManagedPath(relativePath);
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveManagedPath(relativePath);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private string ResolveManagedPath(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_mediaRootPath, relativePath));
        if (!fullPath.StartsWith(_mediaRootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The image path is outside the managed media directory.", nameof(relativePath));
        return fullPath;
    }

    private static async Task WaitUntilStableAsync(string path, CancellationToken cancellationToken)
    {
        long previousLength = -1;
        DateTime previousModified = DateTime.MinValue;
        var stableSamples = 0;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            if (info.Exists && info.Length > 0 && info.Length == previousLength && info.LastWriteTimeUtc == previousModified)
                stableSamples++;
            else
                stableSamples = 0;
            if (stableSamples >= 2)
                return;

            previousLength = info.Exists ? info.Length : -1;
            previousModified = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        throw new IOException($"JPEG file '{Path.GetFileName(path)}' did not finish writing in time.");
    }
}
