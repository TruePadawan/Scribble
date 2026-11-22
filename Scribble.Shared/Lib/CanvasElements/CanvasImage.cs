using System.Text.Json.Serialization;
using SkiaSharp;

namespace Scribble.Shared.Lib.CanvasElements;

public class CanvasImage : CanvasElement, IClonable, ISelectable
{
    public required string ImageBase64String { get; init; }

    public required SKRect Bounds { get; set; }

    public float Rotation { get; set; }
    public bool FlipX { get; set; }
    public bool FlipY { get; set; }
    public bool IsToBeErased = false;

    [JsonIgnore] private SKBitmap? _cachedBitmap;

    private SKSizeI _sourceSize;

    /// <summary>
    /// Each placement has an immutable encoded source. Reuse its pixels while the decode size matches.
    /// </summary>
    public SKBitmap GetBitmap()
    {
        var size = GetDecodeSize(_sourceSize);
        if (_cachedBitmap != null && _cachedBitmap.Width == size.Width && _cachedBitmap.Height == size.Height)
            return _cachedBitmap;

        var decoded = SKBitmap.Decode(Convert.FromBase64String(ImageBase64String))
                      ?? throw new InvalidOperationException("The image could not be decoded.");

        _sourceSize = new SKSizeI(decoded.Width, decoded.Height);
        size = GetDecodeSize(_sourceSize);

        if (decoded.Width != size.Width || decoded.Height != size.Height)
        {
            using var original = decoded;
            decoded = original.Resize(size, new SKSamplingOptions(SKCubicResampler.Mitchell))
                      ?? throw new InvalidOperationException("The image could not be resized.");
        }

        // Recorded frames can share these pixels safely after this placement replaces its cache.
        decoded.SetImmutable();
        _cachedBitmap?.Dispose();
        _cachedBitmap = decoded;
        return decoded;
    }

    /// <summary>
    /// Cap the cached resolution at the original dimensions.
    /// </summary>
    private SKSizeI GetDecodeSize(SKSizeI source)
    {
        var width = (int)Bounds.Width;
        var height = (int)Bounds.Height;
        if (width <= 0 || height <= 0) return source;
        return new SKSizeI(Math.Min(source.Width, width), Math.Min(source.Height, height));
    }

    /// <summary>
    /// Release the decoded bitmap owned by a detached snapshot.
    /// </summary>
    public void ReleaseCachedBitmap()
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
            Bounds = Bounds,
            Rotation = Rotation,
            FlipX = FlipX,
            FlipY = FlipY,
            LayerIndex = LayerIndex,
            CreatorConnectionId = CreatorConnectionId
        };
        return clone;
    }
}