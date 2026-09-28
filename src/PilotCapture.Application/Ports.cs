using PilotCapture.Domain;

namespace PilotCapture.Application;

public interface ICaptureDataStore
{
    Task AddImageAsync(CaptureImage image, ImageAsset asset, CancellationToken cancellationToken);
    Task SaveReviewStateAsync(string captureImageId, CaptureImageReviewState reviewState, CancellationToken cancellationToken);
}

public interface IImageAssetStore
{
    Task<StoredImageAsset> StoreJpegAsync(
        string eventId,
        string sessionId,
        string sourcePath,
        CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken);
}

public sealed record StoredImageAsset(
    string RelativePath,
    string OriginalFileName,
    long ByteLength,
    string Sha256,
    int? PixelWidth,
    int? PixelHeight);
