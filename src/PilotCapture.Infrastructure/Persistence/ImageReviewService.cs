using Microsoft.EntityFrameworkCore;
using PilotCapture.Application;

namespace PilotCapture.Infrastructure.Persistence;

public sealed class ImageReviewService(
    PilotCaptureDbContext dbContext,
    ICaptureDataStore captureDataStore) : IImageReviewService
{
    public async Task<IReadOnlyList<CaptureImageReviewItem>> GetImagesAsync(
        string captureSetId,
        CancellationToken cancellationToken = default) =>
        await dbContext.CaptureImages.AsNoTracking()
            .Where(image => image.CaptureSetId == captureSetId)
            .OrderBy(image => image.SequenceNumber)
            .Select(image => new CaptureImageReviewItem(
                image.Id,
                image.ImageAsset!.RelativePath,
                image.ImageAsset!.OriginalFileName,
                image.SequenceNumber,
                image.ReviewState,
                image.IsPrimary,
                image.IsBanner,
                image.ImageAsset!.PixelWidth,
                image.ImageAsset!.PixelHeight,
                image.ImageAsset!.ByteLength))
            .ToListAsync(cancellationToken);

    public Task ApplyActionAsync(
        string captureImageId,
        CaptureImageReviewAction action,
        CancellationToken cancellationToken = default) =>
        captureDataStore.ApplyReviewActionAsync(captureImageId, action, cancellationToken);
}
