using System;
using System.Collections.Generic;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using SkiaSharp;

namespace Scribble.Services.DocumentService;

/// <summary>
/// Owns a detached copy of the canvas for saving or exporting.
/// </summary>
public sealed class DocumentSnapshot(IReadOnlyList<CanvasElement> elements, SKColor backgroundColor) : IDisposable
{
    internal IReadOnlyList<CanvasElement> Elements { get; } = elements;
    internal SKColor BackgroundColor { get; } = backgroundColor;

    /// <summary>
    /// Release the Skia resources owned by this snapshot.
    /// </summary>
    public void Dispose()
    {
        foreach (var element in Elements)
        {
            if (element is PaintableStroke paintable) paintable.Paint.DisposeSkPaint();
            if (element is Stroke stroke) stroke.Path.Dispose();
            if (element is DrawStroke drawStroke) drawStroke.StablePath?.Dispose();
            if (element is CanvasImage image) image.ReleaseCachedBitmap();
        }
    }
}