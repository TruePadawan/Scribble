using System.Text.Json.Serialization;
using Avalonia.Skia;
using Scribble.Shared.Converters;
using SkiaSharp;

namespace Scribble.Shared.Lib.CanvasElements.Strokes;

/// <summary>
/// Represents a text element on the canvas. Stores the raw text and its original
/// baseline position, in addition to the rendered SKPath inherited from Stroke.
/// </summary>
public class TextStroke : PaintableStroke, IClonable
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

    /// <summary>
    /// Whether this text stroke is rendered in bold.
    /// </summary>
    public bool IsBold { get; set; }

    /// <summary>
    /// Whether this text stroke is rendered in italic.
    /// </summary>
    public bool IsItalic { get; set; }

    /// <summary>
    /// Returns the SkiaSharp font style derived from the current IsBold/IsItalic state.
    /// </summary>
    [JsonIgnore]
    public SKFontStyle SkFontStyle
    {
        get
        {
            if (IsBold && IsItalic) return SKFontStyle.BoldItalic;
            if (IsBold) return SKFontStyle.Bold;
            if (IsItalic) return SKFontStyle.Italic;
            return SKFontStyle.Normal;
        }
    }

    public override CanvasElement Clone(bool preserveId = false)
    {
        var clone = new TextStroke
        {
            Id = preserveId ? Id : Guid.NewGuid(),
            Text = Text,
            Position = Position,
            Path = new SKPath(Path),
            ToolOptions = [..ToolOptions],
            Paint = Paint.Clone(),
            TransformMatrix = TransformMatrix,
            IsBold = IsBold,
            IsItalic = IsItalic,
            LayerIndex = LayerIndex,
            CreatorConnectionId = CreatorConnectionId
        };
        return clone;
    }
}

public enum FontCasing
{
    UpperCase,
    LowerCase
}

public enum FontStyle
{
    Normal,
    Bold,
    Italic
}