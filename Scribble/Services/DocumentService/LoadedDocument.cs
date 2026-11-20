using System;
using System.Collections.Generic;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using SkiaSharp;

namespace Scribble.Services.DocumentService;

/// <summary>Owns decoded elements until the document service installs them on the canvas.</summary>
internal sealed class LoadedDocument(List<CanvasElement> elements, SKColor? backgroundColor) : IDisposable
{
    private List<CanvasElement>? _elements = elements;

    public SKColor? BackgroundColor { get; } = backgroundColor;

    /// <summary>Transfers decoded elements to the canvas and ends this object's ownership.</summary>
    public List<CanvasElement> TakeElements()
    {
        var elements = _elements ?? throw new InvalidOperationException("Document elements were already transferred.");
        _elements = null;
        return elements;
    }

    /// <summary>Releases Skia resources when a decoded document is rejected or abandoned.</summary>
    public void Dispose()
    {
        if (_elements is not { } elements) return;
        foreach (var element in elements)
        {
            if (element is PaintableStroke stroke) stroke.Paint.DisposeSkPaint();
            if (element is Stroke pathStroke) pathStroke.Path.Dispose();
            if (element is DrawStroke drawStroke) drawStroke.StablePath?.Dispose();
            if (element is CanvasImage image) image.ReleaseCachedBitmap();
        }

        _elements = null;
    }
}