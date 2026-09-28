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
        string captureSetId,
        string sourcePath,
        CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken);
    Task DeleteAsync(string relativePath, CancellationToken cancellationToken);
}

public sealed record StoredImageAsset(
    string ImageAssetId,
    string RelativePath,
    string OriginalFileName,
    long ByteLength,
    string Sha256,
    int? PixelWidth,
    int? PixelHeight);

public interface IImageIngestService
{
    Task<ImageIngestResult> ImportJpegAsync(
        string captureSessionId,
        string captureSetId,
        string sourcePath,
        CancellationToken cancellationToken = default);
}

public sealed record ImageIngestResult(
    string CaptureImageId,
    string OriginalFileName,
    string Sha256,
    int SequenceNumber,
    bool AlreadyImported);
