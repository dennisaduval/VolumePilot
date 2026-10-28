using Avalonia;
using PilotCapture.Desktop;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Xunit;

namespace PilotCapture.Desktop.Tests;

public sealed class PortraitPreviewTests
{
    internal const string LandscapeJpeg = "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQH/2wBDAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQH/wAARCAAUACgDAREAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD8X6/ynP8Av4CgD9KP+Cef/NXv+5A/93Wv5b+kr/zRf/dx/wDvBP8AhX/01D/nWr/3uP8A/CrH6UV/LZ/wrhQB/Er/AML5+K//AENX/lD8N/8Aynr/ANUT/iiX+zF/6Rm/87N9IL/6a5/3sf8AFXv9ol/0kL/5ybwO/wDpZh/wvn4r/wDQ1f8AlD8N/wDyno/4ol/sxf8ApGb/AM7N9IL/AOmuH/FXv9ol/wBJC/8AnJvA7/6WZ/Uz/wAG0nhrRP2j/wDhtT/hc9l/wmX/AAhv/DOX/CN/6Td+Hf7N/wCEi/4Xx/bH/IqT6H9s+2f2HpX/AB//AGr7P9l/0XyPPufO/wCNn/S2vou+Bf0Bv+Kf/wDxKZwN/wAQo/4iv/xNZ/r/AP8AGTcYcdf2/wD6i/8AEt3+qv8AycriDjH+yv7K/wBceJP+RL/Z317+0f8AhR+ufU8B9V/E/GLOsz/ag/6u/wDE8+J/4jj/AMQO/tf/AIhd+5w/hp/qv/xEv+y/9dv+TP0vD/8Atr+2v+If8I/8lF/a39m/2T/wkfUPr+Z/Xf6mf+GPP2c/+id/+Xd46/8Amnr/AI2f+I4+KP8A0VH/AJhOHf8A50n4n/xS3+gl/wBGM/8AOm+MX/0wQ/4Y8/Zz/wCid/8Al3eOv/mno/4jj4o/9FR/5hOHf/nSH/FLf6CX/RjP/Om+MX/0wT/Jvr/3oD8vCgD+xT/g01/5v7/7tX/9+Or/AIA/9Oc/51df97s//Cjn9UfRn/5rb/u2/wD3vH9ilf8AAGf1QFAH/9k=";
    [Theory]
    [InlineData(1, false, 0, 0, 255)] // Unmarked landscape: default portrait clockwise.
    [InlineData(6, false, 0, 0, 255)] // EXIF clockwise: do not rotate twice.
    [InlineData(8, false, 0, 255, 0)] // EXIF counterclockwise.
    [InlineData(6, true, 0, 255, 0)] // Explicit upside-down correction.
    public async Task Portrait_preview_respects_exif_and_optional_inversion(int orientation, bool invert, int red, int green, int blue)
    {
        var jpeg = Convert.FromBase64String(LandscapeJpeg);
        // APP1 Exif with little-endian TIFF and one SHORT orientation entry.
        byte[] exif = [255,225,0,34,69,120,105,102,0,0,73,73,42,0,8,0,0,0,1,0,18,1,3,0,1,0,0,0,(byte)orientation,0,0,0,0,0,0,0];
        var inputBytes = jpeg[..2].Concat(exif).Concat(jpeg[2..]).ToArray();
        var png = await PortraitImageDecoder.DecodeAsync(inputBytes, 100, invert);
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream)) { writer.WriteBytes(png); await writer.StoreAsync(); writer.DetachStream(); }
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        Assert.Equal(20U, decoder.PixelWidth); Assert.Equal(40U, decoder.PixelHeight);
        var provider = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
            new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        var pixels = provider.DetachPixelData();
        // First pixel of the portrait identifies which quarter-turn was applied.
        Assert.InRange((int)pixels[0], Math.Max(0, blue - 10), Math.Min(255, blue + 10));
        Assert.InRange((int)pixels[1], Math.Max(0, green - 10), Math.Min(255, green + 10));
        Assert.InRange((int)pixels[2], Math.Max(0, red - 10), Math.Min(255, red + 10));
    }

    [Fact]
    public void Tap_coordinates_account_for_letterboxing_and_closeup_stays_inside_portrait()
    {
        Assert.Null(PortraitCropView.ImagePoint(new Size(200,400), new Size(300,400), new Point(10,100)));
        Assert.Equal(new Point(0.5,0.25), PortraitCropView.ImagePoint(new Size(200,400), new Size(300,400), new Point(150,100)));
        foreach (var center in new[] { new Point(0,0), new Point(0.5,0.22), new Point(1,1) })
        {
            var crop = PortraitCropView.CropBounds(new Size(200,400), new Size(300,400), center, 3);
            Assert.True(new Rect(0,0,200,400).Contains(crop));
            Assert.Equal(0.75, crop.Width / crop.Height, 5);
        }
    }
}
