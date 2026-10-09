using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace PilotCapture.Desktop;

public static class PortraitImageDecoder
{
    public static async Task<byte[]> DecodeAsync(byte[] bytes, uint maximumDimension = 2400)
    {
        using var input = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(input))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            writer.DetachStream();
        }
        input.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(input);
        var scale = Math.Min(1, maximumDimension / (double)Math.Max(decoder.PixelWidth, decoder.PixelHeight));
        var transform = new BitmapTransform
        {
            ScaledWidth = Math.Max(1U, (uint)(decoder.PixelWidth * scale)),
            ScaledHeight = Math.Max(1U, (uint)(decoder.PixelHeight * scale)),
            Rotation = decoder.OrientedPixelWidth > decoder.OrientedPixelHeight ? BitmapRotation.Clockwise90Degrees : BitmapRotation.None
        };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();
        output.Seek(0);
        using var reader = new DataReader(output.GetInputStreamAt(0));
        var result = new byte[(int)output.Size];
        await reader.LoadAsync((uint)result.Length);
        reader.ReadBytes(result);
        return result;
    }
}
