using Avalonia;
using Windows.Graphics.Imaging;
using Windows.Media.FaceAnalysis;
using Windows.Storage.Streams;

namespace PilotCapture.Desktop;

public sealed class WindowsPortraitFaceDetector
{
    private const uint MaximumDetectionDimension = 1600;
    private readonly SemaphoreSlim _detectionLock = new(1, 1);
    private FaceDetector? _faceDetector;

    public async Task<Rect?> DetectLargestFaceAsync(byte[] jpegBytes, CancellationToken cancellationToken = default)
    {
        await _detectionLock.WaitAsync(cancellationToken);
        try
        {
            return await DetectCoreAsync(jpegBytes, cancellationToken);
        }
        finally
        {
            _detectionLock.Release();
        }
    }

    private async Task<Rect?> DetectCoreAsync(byte[] jpegBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!FaceDetector.IsSupported)
            return null;

        _faceDetector ??= await FaceDetector.CreateAsync();
        if (_faceDetector is null)
            return null;

        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(jpegBytes);
            await writer.StoreAsync();
            writer.DetachStream();
        }
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream);
        var originalWidth = decoder.PixelWidth;
        var originalHeight = decoder.PixelHeight;
        var scale = Math.Min(1.0, MaximumDetectionDimension / (double)Math.Max(originalWidth, originalHeight));
        var scaledWidth = Math.Max(1U, (uint)Math.Round(originalWidth * scale));
        var scaledHeight = Math.Max(1U, (uint)Math.Round(originalHeight * scale));
        var transform = new BitmapTransform
        {
            ScaledWidth = scaledWidth,
            ScaledHeight = scaledHeight
        };

        var supportedFormats = FaceDetector.GetSupportedBitmapPixelFormats();
        var pixelFormat = supportedFormats.Contains(BitmapPixelFormat.Bgra8)
            ? BitmapPixelFormat.Bgra8
            : supportedFormats.Contains(BitmapPixelFormat.Gray8)
                ? BitmapPixelFormat.Gray8
                : (BitmapPixelFormat?)null;
        if (pixelFormat is null)
            return null;
        var alphaMode = pixelFormat == BitmapPixelFormat.Bgra8
            ? BitmapAlphaMode.Premultiplied
            : BitmapAlphaMode.Ignore;
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
            pixelFormat.Value,
            alphaMode,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.DoNotColorManage);

        cancellationToken.ThrowIfCancellationRequested();
        var faces = await _faceDetector.DetectFacesAsync(softwareBitmap);
        var largest = faces
            .OrderByDescending(face => (double)face.FaceBox.Width * face.FaceBox.Height)
            .FirstOrDefault();
        if (largest is null)
            return null;

        var faceBox = largest.FaceBox;
        var detectionWidth = softwareBitmap.PixelWidth;
        var detectionHeight = softwareBitmap.PixelHeight;
        return new Rect(
            (double)faceBox.X / detectionWidth,
            (double)faceBox.Y / detectionHeight,
            (double)faceBox.Width / detectionWidth,
            (double)faceBox.Height / detectionHeight);
    }
}
