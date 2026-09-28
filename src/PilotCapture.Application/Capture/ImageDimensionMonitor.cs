namespace PilotCapture.Application.Capture;

public sealed record ImagePixelDimensions(int Width, int Height);

public static class ImageDimensionMonitor
{
    /// <summary>
    /// Returns the distinct image sizes seen in a capture set. Orientation is
    /// normalized so turning a camera between portrait and landscape does not
    /// look like a camera image-size setting change.
    /// </summary>
    public static IReadOnlyList<ImagePixelDimensions> GetDistinctCaptureSizes(
        IEnumerable<(int? Width, int? Height)> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);

        return observations
            .Where(item => item.Width is > 0 && item.Height is > 0)
            .Select(item =>
            {
                var width = item.Width.GetValueOrDefault();
                var height = item.Height.GetValueOrDefault();
                return new ImagePixelDimensions(Math.Max(width, height), Math.Min(width, height));
            })
            .Distinct()
            .OrderByDescending(item => item.Width)
            .ThenByDescending(item => item.Height)
            .ToArray();
    }
}
