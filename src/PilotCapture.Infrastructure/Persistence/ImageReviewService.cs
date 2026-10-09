using Microsoft.EntityFrameworkCore;
using PilotCapture.Application;

namespace PilotCapture.Infrastructure.Persistence;

public sealed class ImageReviewService(PilotCaptureDbContext dbContext, ICaptureDataStore captureDataStore) : IImageReviewService
{
    public async Task<IReadOnlyList<CaptureImageReviewItem>> GetImagesAsync(string captureSetId, CancellationToken cancellationToken = default)
    {
        var set = await dbContext.CaptureSets.AsNoTracking().SingleAsync(x => x.Id == captureSetId, cancellationToken);
        var scope = set.MembershipId ?? set.SubjectId;
        return await dbContext.CaptureImages.AsNoTracking()
            .Where(x => x.SelectionScopeId == scope).OrderBy(x => x.Id)
            .Select(x => new CaptureImageReviewItem(x.Id, x.ImageAsset!.RelativePath, x.ImageAsset.OriginalFileName,
                x.SequenceNumber, x.ReviewState, x.IsPrimary, x.IsBanner, x.ImageAsset.PixelWidth,
                x.ImageAsset.PixelHeight, x.ImageAsset.ByteLength, x.IsSecondary)).ToListAsync(cancellationToken);
    }
    public Task ApplyActionAsync(string captureImageId, CaptureImageReviewAction action, CancellationToken cancellationToken = default) =>
        captureDataStore.ApplyReviewActionAsync(captureImageId, action, cancellationToken);
}
