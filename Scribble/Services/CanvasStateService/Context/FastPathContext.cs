using System;
using System.Collections.Generic;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using SkiaSharp;

namespace Scribble.Services.CanvasStateService.Context;

/// <summary>
/// Provides fast-path handlers with access to the live canvas state
/// (the lookup dictionaries maintained between replays).
/// </summary>
public class FastPathContext
{
    public required Dictionary<Guid, PaintableStroke> StrokeLookup { get; init; }
    public required Dictionary<Guid, EraserStroke> EraserStrokeLookup { get; init; }
    public required Dictionary<Guid, SKPoint> EraserHeadLookup { get; init; }
    public required Dictionary<Guid, SelectionBound> SelectionBoundLookup { get; init; }
    public required Dictionary<Guid, CanvasImage> CanvasImageLookup { get; init; }
    public required IReadOnlyList<CanvasElement> CanvasElements { get; init; }
    public required HashSet<Guid> LocalSelectionBoundIds { get; init; }

    // Callbacks the handler can invoke to signal the UI
    public required Action? OnCanvasInvalidated { get; init; }
    public required Action? OnSelectionInvalidated { get; init; }

    public Guid? ActiveSelectionBoundId { get; set; }
    public List<Guid>? SelectedElementIds { get; set; }
}