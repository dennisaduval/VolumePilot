using Microsoft.EntityFrameworkCore;
using PilotCapture.Application;
using PilotCapture.Domain;

namespace PilotCapture.Infrastructure.Persistence;

public sealed class CaptureDataStore(PilotCaptureDbContext dbContext) : ICaptureDataStore
{
    public async Task AddImageAsync(CaptureImage image, ImageAsset asset, AuditEntry auditEntry, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var set = await dbContext.CaptureSets.SingleAsync(x => x.Id == image.CaptureSetId, cancellationToken);
        image.SelectionScopeId = set.MembershipId ?? set.SubjectId;
        var images = await dbContext.CaptureImages.Where(x => x.SelectionScopeId == image.SelectionScopeId)
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        images.Add(image);
        var roles = NormalizeRoles(images);
        // Clear first, then assign: SQLite unique indexes are checked per statement.
        await SaveRolesAsync(images, roles, cancellationToken, () =>
        {
            dbContext.ImageAssets.Add(asset);
            dbContext.CaptureImages.Add(image);
            dbContext.AuditEntries.Add(auditEntry);
        });
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ApplyReviewActionAsync(string captureImageId, CaptureImageReviewAction action, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action));
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var image = await dbContext.CaptureImages.SingleAsync(x => x.Id == captureImageId, cancellationToken);
        var images = await dbContext.CaptureImages.Where(x => x.SelectionScopeId == image.SelectionScopeId)
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        var primary = images.FirstOrDefault(x => x.IsPrimary);
        var secondary = images.FirstOrDefault(x => x.IsSecondary);
        switch (action)
        {
            case CaptureImageReviewAction.Reject:
                image.ReviewState = image.ReviewState == CaptureImageReviewState.Rejected
                    ? CaptureImageReviewState.Pending : CaptureImageReviewState.Rejected;
                break;
            case CaptureImageReviewAction.SetPrimary:
                image.ReviewState = CaptureImageReviewState.Accepted;
                foreach (var x in images) x.IsPrimary = x == image;
                if (secondary == image && primary != image)
                    foreach (var x in images) x.IsSecondary = x == primary;
                break;
            case CaptureImageReviewAction.SetSecondary:
                image.ReviewState = CaptureImageReviewState.Accepted;
                foreach (var x in images) x.IsSecondary = x == image;
                if (primary == image && secondary != image)
                    foreach (var x in images) x.IsPrimary = x == secondary;
                break;
            case CaptureImageReviewAction.ToggleBanner:
                // Legacy API name retained; the banner button now selects exactly one banner.
                image.ReviewState = CaptureImageReviewState.Accepted;
                foreach (var x in images) x.IsBanner = x == image;
                break;
        }
        var roles = NormalizeRoles(images);
        await SaveRolesAsync(images, roles, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static (CaptureImage? Primary, CaptureImage? Secondary, CaptureImage? Banner) NormalizeRoles(List<CaptureImage> images)
    {
        var eligible = images.Where(x => x.ReviewState != CaptureImageReviewState.Rejected).ToList();
        var primary = eligible.FirstOrDefault(x => x.IsPrimary) ?? eligible.FirstOrDefault();
        var secondary = eligible.FirstOrDefault(x => x.IsSecondary && x != primary)
            ?? eligible.FirstOrDefault(x => x != primary) ?? primary;
        var banner = eligible.FirstOrDefault(x => x.IsBanner) ?? primary;
        return (primary, secondary, banner);
    }

    private async Task SaveRolesAsync(List<CaptureImage> images,
        (CaptureImage? Primary, CaptureImage? Secondary, CaptureImage? Banner) roles,
        CancellationToken cancellationToken, Action? beforeSave = null)
    {
        foreach (var image in images) image.IsPrimary = image.IsSecondary = image.IsBanner = false;
        beforeSave?.Invoke();
        await dbContext.SaveChangesAsync(cancellationToken);
        foreach (var image in images)
        {
            image.IsPrimary = image == roles.Primary;
            image.IsSecondary = image == roles.Secondary;
            image.IsBanner = image == roles.Banner;
            if (image.IsPrimary || image.IsSecondary || image.IsBanner) image.ReviewState = CaptureImageReviewState.Accepted;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
