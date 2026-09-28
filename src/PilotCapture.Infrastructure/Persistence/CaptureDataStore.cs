using Microsoft.EntityFrameworkCore;
using PilotCapture.Application;
using PilotCapture.Domain;

namespace PilotCapture.Infrastructure.Persistence;

public sealed class CaptureDataStore(PilotCaptureDbContext dbContext) : ICaptureDataStore
{
    public async Task AddImageAsync(
        CaptureImage image,
        ImageAsset asset,
        AuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!image.IsPrimary && !await dbContext.CaptureImages.AnyAsync(
                item => item.CaptureSetId == image.CaptureSetId && item.IsPrimary,
                cancellationToken))
        {
            image.IsPrimary = true;
            image.ReviewState = CaptureImageReviewState.Accepted;
        }

        dbContext.ImageAssets.Add(asset);
        dbContext.CaptureImages.Add(image);
        dbContext.AuditEntries.Add(auditEntry);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ApplyReviewActionAsync(
        string captureImageId,
        CaptureImageReviewAction action,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(action))
            throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported image review action.");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var image = await dbContext.CaptureImages.SingleOrDefaultAsync(
            item => item.Id == captureImageId,
            cancellationToken)
            ?? throw new KeyNotFoundException($"Capture image '{captureImageId}' was not found.");

        switch (action)
        {
            case CaptureImageReviewAction.SetPrimary:
            {
                var currentPrimary = await dbContext.CaptureImages
                    .Where(item => item.CaptureSetId == image.CaptureSetId && item.IsPrimary)
                    .ToListAsync(cancellationToken);
                foreach (var current in currentPrimary)
                    current.IsPrimary = false;
                await dbContext.SaveChangesAsync(cancellationToken);
                image.IsPrimary = true;
                image.ReviewState = CaptureImageReviewState.Accepted;
                await dbContext.SaveChangesAsync(cancellationToken);
                break;
            }
            case CaptureImageReviewAction.ToggleBanner:
                image.IsBanner = !image.IsBanner;
                if (image.IsBanner && image.ReviewState == CaptureImageReviewState.Rejected)
                    image.ReviewState = CaptureImageReviewState.Accepted;
                await dbContext.SaveChangesAsync(cancellationToken);
                break;
            case CaptureImageReviewAction.Reject:
            {
                var wasPrimary = image.IsPrimary;
                image.IsPrimary = false;
                image.IsBanner = false;
                image.ReviewState = CaptureImageReviewState.Rejected;
                await dbContext.SaveChangesAsync(cancellationToken);
                if (wasPrimary)
                {
                    var replacement = await dbContext.CaptureImages
                        .Where(item => item.CaptureSetId == image.CaptureSetId
                            && item.Id != image.Id
                            && item.ReviewState != CaptureImageReviewState.Rejected)
                        .OrderBy(item => item.SequenceNumber)
                        .FirstOrDefaultAsync(cancellationToken);
                    if (replacement is not null)
                    {
                        replacement.IsPrimary = true;
                        replacement.ReviewState = CaptureImageReviewState.Accepted;
                        await dbContext.SaveChangesAsync(cancellationToken);
                    }
                }
                break;
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
