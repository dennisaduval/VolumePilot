using Microsoft.EntityFrameworkCore;
using PilotCapture.Application;
using PilotCapture.Domain;

namespace PilotCapture.Infrastructure.Persistence;

public sealed class CaptureDataStore(PilotCaptureDbContext dbContext) : ICaptureDataStore
{
    public async Task AddImageAsync(CaptureImage image, ImageAsset asset, CancellationToken cancellationToken)
    {
        dbContext.ImageAssets.Add(asset);
        dbContext.CaptureImages.Add(image);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveReviewStateAsync(
        string captureImageId,
        CaptureImageReviewState reviewState,
        CancellationToken cancellationToken)
    {
        var image = await dbContext.CaptureImages.SingleOrDefaultAsync(
            item => item.Id == captureImageId,
            cancellationToken)
            ?? throw new KeyNotFoundException($"Capture image '{captureImageId}' was not found.");
        image.ReviewState = reviewState;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
