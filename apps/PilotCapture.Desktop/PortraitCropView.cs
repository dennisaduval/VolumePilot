using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace PilotCapture.Desktop;

// Display-only sampling of the already decoded portrait. No file crop or face analysis.
public sealed class PortraitCropView : Control
{
    private Bitmap? _source;
    private Point _center = new(0.5, 0.22);
    private double _zoom = 3;
    public Bitmap? Source { get => _source; set { _source = value; InvalidateVisual(); } }
    public Point Center { get => _center; set { _center = value; InvalidateVisual(); } }
    public double Zoom { get => _zoom; set { _zoom = value; InvalidateVisual(); } }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_source is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var height = Math.Min(Bounds.Height, Bounds.Width * 1.5);
        var size = new Size(height / 1.5, height);
        var destination = new Rect((Bounds.Width - size.Width) / 2, (Bounds.Height - size.Height) / 2, size.Width, size.Height);
        var crop = CropBounds(_source.Size, size, _center, _zoom);
        context.DrawImage(_source, crop, destination);
    }

    public static Rect CropBounds(Size image, Size viewport, Point center, double zoom)
    {
        if (image.Width <= 0 || image.Height <= 0 || viewport.Width <= 0 || viewport.Height <= 0) return default;
        var width = image.Width / Math.Clamp(zoom, 1, 6);
        var height = image.Height / Math.Clamp(zoom, 1, 6);
        var aspect = viewport.Width / viewport.Height;
        if (width / height > aspect) width = height * aspect;
        else height = width / aspect;
        return new Rect(Math.Clamp(center.X * image.Width - width / 2, 0, image.Width - width),
            Math.Clamp(center.Y * image.Height - height / 2, 0, image.Height - height), width, height);
    }

    // Returns null for taps in the letterboxing around a Uniform full-image preview.
    public static Point? ImagePoint(Size image, Size viewport, Point point)
    {
        if (image.Width <= 0 || image.Height <= 0 || viewport.Width <= 0 || viewport.Height <= 0) return null;
        var scale = Math.Min(viewport.Width / image.Width, viewport.Height / image.Height);
        var width = image.Width * scale; var height = image.Height * scale;
        var x = (point.X - (viewport.Width - width) / 2) / width;
        var y = (point.Y - (viewport.Height - height) / 2) / height;
        return x is >= 0 and <= 1 && y is >= 0 and <= 1 ? new Point(x, y) : null;
    }
}
