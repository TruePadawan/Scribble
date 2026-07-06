using System;
using System.Collections.Generic;
using System.Linq;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using SkiaSharp;

namespace Scribble.Services.CanvasStateService;

/// <summary>
/// Holds the state of the canvas at a point in time
/// </summary>
public class CanvasCheckpoint : IDisposable
{
    public int EventIndex { get; }
    public HashSet<Guid> AppliedActionIds { get; }
    public Dictionary<Guid, PaintableStroke> StrokeLookup { get; }
    public Dictionary<Guid, EraserStroke> EraserStrokeLookup { get; }
    public Dictionary<Guid, SKPoint> EraserHeadLookup { get; }
    public Dictionary<Guid, SelectionBound> SelectionBoundLookup { get; }
    public Dictionary<Guid, CanvasImage> CanvasImageLookup { get; }

    public CanvasCheckpoint(
        int eventIndex,
        HashSet<Guid> appliedActionIds,
        IDictionary<Guid, PaintableStroke> strokeLookup,
        IDictionary<Guid, EraserStroke> eraserStrokeLookup,
        IDictionary<Guid, SKPoint> eraserHeadLookup,
        IDictionary<Guid, SelectionBound> selectionBoundLookup,
        IDictionary<Guid, CanvasImage> canvasImageLookup)
    {
        EventIndex = eventIndex;
        AppliedActionIds = new HashSet<Guid>(appliedActionIds);

        // Deep clone all elements and build lookups referencing the cloned instances
        StrokeLookup =
            strokeLookup.ToDictionary(kvp => kvp.Key, kvp => (PaintableStroke)kvp.Value.Clone(preserveId: true));
        EraserStrokeLookup = eraserStrokeLookup.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Clone(preserveId: true));
        EraserHeadLookup = new Dictionary<Guid, SKPoint>(eraserHeadLookup);
        SelectionBoundLookup =
            selectionBoundLookup.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Clone(preserveId: true));
        CanvasImageLookup =
            canvasImageLookup.ToDictionary(kvp => kvp.Key, kvp => (CanvasImage)kvp.Value.Clone(preserveId: true));
    }

    public void Dispose()
    {
        foreach (var stroke in StrokeLookup.Values)
        {
            stroke.Dispose();
        }

        foreach (var eraser in EraserStrokeLookup.Values)
        {
            eraser.Dispose();
        }

        foreach (var bound in SelectionBoundLookup.Values)
        {
            bound.Dispose();
        }

        foreach (var image in CanvasImageLookup.Values)
        {
            image.Dispose();
        }
    }
}