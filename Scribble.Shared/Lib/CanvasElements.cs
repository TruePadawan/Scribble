using System.Text.Json.Serialization;
using Scribble.Shared.Converters;
using SkiaSharp;

namespace Scribble.Shared.Lib;

[JsonDerivedType(typeof(CanvasImage), typeDiscriminator: "CanvasImage")]
public abstract class CanvasElement
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Logical z-order layer index for this element. 0 is the base layer.
    /// </summary>
    public int LayerIndex { get; set; }

    /// <summary>
    /// The SignalR ConnectionId of the client that created this element.
    /// Null when the element was created in solo mode or loaded from a saved file.
    /// </summary>
    public string? CreatorConnectionId { get; set; }

    /// <summary>
    /// The time this element was created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

[JsonDerivedType(typeof(DrawStroke), typeDiscriminator: "DrawStroke")]
[JsonDerivedType(typeof(EraserStroke), typeDiscriminator: "EraserStroke")]
[JsonDerivedType(typeof(SelectionBound), typeDiscriminator: "SelectionBound")]
public abstract class Stroke : CanvasElement
{
    [JsonConverter(typeof(SKPathJsonConverter))]
    public required SKPath Path { get; init; } = new();
}

public class DrawStroke : Stroke
{
    public bool IsToBeErased = false;
    public required ToolType ToolType;
    public required HashSet<ToolOption> ToolOptions { get; init; } = [];
    public required StrokePaint Paint { get; init; } = new();
}

public class EraserStroke : Stroke
{
    public HashSet<Guid> Targets = [];
}

public class SelectionBound : Stroke
{
    public HashSet<Guid> Targets = [];
}

public class CanvasImage : CanvasElement
{
    public required string ImageBase64String { get; init; }

    [JsonIgnore] private SKRect _bounds;

    public required SKRect Bounds
    {
        get => _bounds;
        set
        {
            _bounds = value;
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

    public void DisposeBitmap()
    {
        _cachedBitmap?.Dispose();
        _cachedBitmap = null;
    }
}

public enum ToolType
{
    Pencil,
    Line,
    Arrow,
    Ellipse,
    Rectangle,
    Text
}

public enum StrokeStyle
{
    Solid,
    Dash,
    Dotted
}

public enum EdgeType
{
    Sharp,
    Rounded
}