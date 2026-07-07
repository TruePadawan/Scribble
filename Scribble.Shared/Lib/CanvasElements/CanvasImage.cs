using System.Text.Json.Serialization;
using SkiaSharp;

namespace Scribble.Shared.Lib.CanvasElements;

public class CanvasImage : CanvasElement, IClonable
{
    public required string ImageBase64String { get; init; }

    [JsonIgnore] private SKRect _bounds;

    public required SKRect Bounds
    {
        get => _bounds;
        set
        {
            _bounds = value;
            _cachedBitmap?.Dispose();
            _cachedBitmap = null;
        }
    }

    public float Rotation { get; set; }
    public bool FlipX { get; set; }
    public bool FlipY { get; set; }
    public bool IsToBeErased = false;

    [JsonIgnore] private SKBitmap? _cachedBitmap;

    public SKBitmap GetBitmap()
    {
        if (_cachedBitmap is not null) return _cachedBitmap;

        var bitmap = SKBitmap.Decode(Convert.FromBase64String(ImageBase64String));
        var targetWidth = (int)Bounds.Width;
        var targetHeight = (int)Bounds.Height;

        if (targetWidth > 0 && targetHeight > 0 &&
            (bitmap.Width > targetWidth || bitmap.Height > targetHeight))
        {
            var downscaledBitmap = new SKBitmap(
                Math.Min(bitmap.Width, targetWidth),
                Math.Min(bitmap.Height, targetHeight));
            bitmap.ScalePixels(downscaledBitmap, SKFilterQuality.High);
            bitmap.Dispose();
            _cachedBitmap = downscaledBitmap;
        }
        else
        {
            _cachedBitmap = bitmap;
        }

        return _cachedBitmap;
    }

    private void DisposeBitmap()
    {
        _cachedBitmap?.Dispose();
        _cachedBitmap = null;
    }

    public CanvasElement Clone(bool preserveId = false)
    {
        var clone = new CanvasImage
        {
            Id = preserveId ? Id : Guid.NewGuid(),
            ImageBase64String = ImageBase64String,
            Bounds = _bounds,
            Rotation = Rotation,
            FlipX = FlipX,
            FlipY = FlipY,
            LayerIndex = LayerIndex,
            CreatorConnectionId = CreatorConnectionId
        };
        return clone;
    }
}