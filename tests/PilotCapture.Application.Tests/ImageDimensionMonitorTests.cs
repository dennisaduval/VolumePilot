using PilotCapture.Application.Capture;
using Xunit;

namespace PilotCapture.Application.Tests;

public sealed class ImageDimensionMonitorTests
{
    [Fact]
    public void Portrait_and_landscape_versions_of_the_same_size_are_one_camera_output_size()
    {
        var sizes = ImageDimensionMonitor.GetDistinctCaptureSizes(
        [
            (6000, 4000),
            (4000, 6000),
            (6000, 4000)
        ]);

        Assert.Equal([new ImagePixelDimensions(6000, 4000)], sizes);
    }

    [Fact]
    public void Different_sizes_are_reported_and_missing_metadata_is_ignored()
    {
        var sizes = ImageDimensionMonitor.GetDistinctCaptureSizes(
        [
            (6000, 4000),
            (3000, 2000),
            ((int?)null, null),
            (0, 0)
        ]);

        Assert.Equal(
            [new ImagePixelDimensions(6000, 4000), new ImagePixelDimensions(3000, 2000)],
            sizes);
    }
}
