using PilotCapture.Application.FastCapture;
using Xunit;

namespace PilotCapture.Application.Tests;

public sealed class CaptureFileNamesTests
{
    [Theory]
    [InlineData(1, "01")]
    [InlineData(99, "99")]
    [InlineData(100, "A0")]
    [InlineData(135, "AZ")]
    [InlineData(136, "B0")]
    [InlineData(1035, "ZZ")]
    [InlineData(1036, "A00")]
    public void Serials_never_wrap(long value, string expected) => Assert.Equal(expected, CaptureFileNames.Serial(value));
    [Fact]
    public void File_parts_preserve_requested_color_names_and_remove_spaces()
    {
        Assert.Equal("10", CaptureFileNames.Station("Pilot Capture 10"));
        Assert.Equal("Wild_Cats", CaptureFileNames.Part("Wild Cats"));
        Assert.Equal("AC04", CaptureFileNames.Slot(-14));
        Assert.Equal("M01", CaptureFileNames.Slot(-21));
        Assert.Equal("T04", CaptureFileNames.Slot(-34));
        Assert.Equal("_LimeGreen", CaptureColor.Defaults.Single(c => c.Name == "Lime Green").FileSuffix);
        Assert.Equal("", CaptureColor.Defaults[0].FileSuffix);
        Assert.All(CaptureColor.Defaults, c => Assert.True(CaptureColor.IsValid(c)));
    }
}
