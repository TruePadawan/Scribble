using System;
using System.Collections.Generic;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using SkiaSharp;

namespace Scribble.Services.CanvasStateService.Context;

/// <summary>
/// Context for event handlers during replay
/// </summary>
public class ReplayContext
{
    public Dictionary<Guid, PaintableStroke> PaintableStrokes { get; } = new();
    public Dictionary<Guid, EraserStroke> EraserStrokes { get; } = new();
    public Dictionary<Guid, SKPoint> EraserHeads { get; } = new();
    public Dictionary<Guid, SelectionBound> SelectionBounds { get; } = new();
    public Dictionary<Guid, CanvasImage> CanvasImages { get; } = new();
    public List<Guid> StaleActionIds { get; } = [];
    public int MaxLayerIndex { get; set; }
    public int MinLayerIndex { get; set; }
}