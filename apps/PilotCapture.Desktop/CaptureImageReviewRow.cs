using Avalonia.Media.Imaging;
using PilotCapture.Application;
using PilotCapture.Domain;

namespace PilotCapture.Desktop;

public sealed class CaptureImageReviewRow(CaptureImageReviewItem item, Bitmap thumbnail)
{
    public string Id => item.Id;
    public string RelativePath => item.RelativePath;
    public string OriginalFileName => item.OriginalFileName;
    public int SequenceNumber => item.SequenceNumber;
    public bool IsPrimary => item.IsPrimary;
    public bool IsSecondary => item.IsSecondary;
    public bool IsBanner => item.IsBanner;
    public CaptureImageReviewState ReviewState => item.ReviewState;
    public int? PixelWidth => item.PixelWidth;
    public int? PixelHeight => item.PixelHeight;
    public long ByteLength => item.ByteLength;
    public Bitmap Thumbnail { get; } = thumbnail;
    public string Caption
    {
        get
        {
            var flags = new List<string>();
            if (IsPrimary) flags.Add("Primary");
            if (IsSecondary) flags.Add("Secondary");
            if (IsBanner) flags.Add("Banner");
            if (ReviewState == CaptureImageReviewState.Rejected) flags.Add("Rejected");
            var details = flags.Count == 0 ? "Pending" : string.Join(" · ", flags);
            return $"{OriginalFileName}\n{details}";
        }
    }
}

