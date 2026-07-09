using System;
using System.Collections.Generic;
using System.Linq;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;

namespace Scribble.Services.CanvasStateService.State;

public class CanvasState
{
    public Dictionary<Guid, PaintableStroke> PaintableStrokes { get; } = new();
    public Dictionary<Guid, EraserStroke> EraserStrokes { get; } = new();

    public Dictionary<Guid, SelectionBound> SelectionBounds { get; } = new();
    public Dictionary<Guid, CanvasImage> CanvasImages { get; } = new();

    public List<CanvasElement> ElementsWithLayers { get; private set; } = [];
    public List<Guid> SelectedElementIds { get; } = [];
    public Guid? ActiveSelectionBoundId { get; set; }
    public List<Guid> StaleActionIds { get; } = [];

    public int MaxLayerIndex { get; set; }
    public int MinLayerIndex { get; set; }

    public string? MyConnectionId { get; set; }

    public CanvasState Clone()
    {
        var clone = new CanvasState
        {
            MaxLayerIndex = MaxLayerIndex,
            MinLayerIndex = MinLayerIndex,
            ActiveSelectionBoundId = ActiveSelectionBoundId,
            MyConnectionId = MyConnectionId
        };

        foreach (var (key, value) in PaintableStrokes)
        {
            clone.PaintableStrokes[key] = (PaintableStroke)value.Clone(preserveId: true);
        }

        foreach (var (key, value) in CanvasImages)
        {
            clone.CanvasImages[key] = (CanvasImage)value.Clone(preserveId: true);
        }

        foreach (var (key, value) in SelectionBounds)
        {
            clone.SelectionBounds[key] = value.Clone(preserveId: true);
        }

        foreach (var id in SelectedElementIds)
        {
            clone.SelectedElementIds.Add(id);
        }

        // We don't necessarily need to clone active eraser strokes since they are transient
        // and usually not preserved in a checkpoint, but for correctness:
        foreach (var (key, value) in EraserStrokes)
        {
            clone.EraserStrokes[key] = value.Clone(preserveId: true);
        }

        clone.NormalizeLayers();
        return clone;
    }

    public void NormalizeLayers()
    {
        var elements = PaintableStrokes.Values.Cast<CanvasElement>()
            .Concat(CanvasImages.Values)
            .ToList();

        elements.Sort((a, b) => a.LayerIndex.CompareTo(b.LayerIndex));

        for (var i = 0; i < elements.Count; i++)
        {
            elements[i].LayerIndex = i;
        }

        if (elements.Count > 0)
        {
            MinLayerIndex = 0;
            MaxLayerIndex = elements.Count - 1;
        }
        else
        {
            MinLayerIndex = 0;
            MaxLayerIndex = 0;
        }

        ElementsWithLayers = elements;
    }
}