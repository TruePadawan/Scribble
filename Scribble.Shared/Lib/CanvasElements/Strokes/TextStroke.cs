using System.Text.Json.Serialization;
using Scribble.Shared.Converters;
using SkiaSharp;

namespace Scribble.Shared.Lib.CanvasElements.Strokes;

/// <summary>
/// Represents a text element on the canvas. Stores the raw text and its original
/// baseline position, in addition to the rendered SKPath inherited from Stroke.
/// </summary>
public class TextStroke : PaintableStroke
{
    /// <summary>
    /// The text content of this stroke.
    /// </summary>
    public required string Text { get; set; }

    /// <summary>
    /// The original baseline position at which the text path was first created.
    /// This is the anchor used when rebuilding the path after font-size changes.
    /// </summary>
    public required SKPoint Position { get; set; }

    /// <summary>
    /// Cumulatively tracks translation, rotation, and scaling matrices applied to the stroke.
    /// </summary>
    [JsonConverter(typeof(SKMatrixJsonConverter))]
    public SKMatrix TransformMatrix { get; set; } = SKMatrix.Identity;
}
