using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;

namespace Scribble.Shared.State;

public class CanvasState
{
    public Dictionary<Guid, PaintableStroke> PaintableStrokes { get; } = new();
    public Dictionary<Guid, EraserStroke> EraserStrokes { get; } = new();

    public Dictionary<Guid, SelectionBound> SelectionBounds { get; } = new();
    public Dictionary<Guid, CanvasImage> CanvasImages { get; } = new();

    public List<CanvasElement> ElementsWithLayers { get; private set; } = [];
    public List<Guid> SelectedElementIds { get; } = [];
    public Guid? ActiveSelectionBoundId { get; set; }
    public HashSet<Guid> StaleActionIds { get; } = [];

    public string? MyConnectionId { get; set; }

    public CanvasState Clone()
    {
        var clone = new CanvasState
        {
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

        clone.StaleActionIds.UnionWith(StaleActionIds);
        clone.NormalizeLayers();
        return clone;
    }

    /// <summary>
    /// Append new drawing elements above the current board and keep their lookup in sync.
    /// </summary>
    internal void AddOnTop<T>(T element, Dictionary<Guid, T> lookup) where T : CanvasElement
    {
        if (lookup.TryGetValue(element.Id, out var previous)) ElementsWithLayers.Remove(previous);
        element.LayerIndex = ElementsWithLayers.Count;
        lookup[element.Id] = element;
        ElementsWithLayers.Add(element);

        // Replacing an existing ID can leave a gap in the previous layer positions.
        if (previous != null)
        {
            for (var i = 0; i < ElementsWithLayers.Count; i++) ElementsWithLayers[i].LayerIndex = i;
        }
    }

    public void NormalizeLayers()
    {
        var elements = PaintableStrokes.Values.Cast<CanvasElement>()
            .Concat(CanvasImages.Values)
            .ToList();

        // IDs survive serialization and break ties consistently across both element types.
        elements.Sort((a, b) =>
        {
            var order = a.LayerIndex.CompareTo(b.LayerIndex);
            return order != 0 ? order : a.Id.CompareTo(b.Id);
        });

        for (var i = 0; i < elements.Count; i++)
        {
            elements[i].LayerIndex = i;
        }

        ElementsWithLayers = elements;
    }

    /// <summary>
    /// Removes all selection bounds owned by the given connection ID.
    /// Other users' bounds are preserved.
    /// </summary>
    public void ClearSelectionBoundsForUser(string? connectionId)
    {
        var toRemove = SelectionBounds.Values
            .Where(b => b.CreatorConnectionId == connectionId)
            .Select(b => b.Id)
            .ToList();

        foreach (var id in toRemove)
        {
            SelectionBounds.Remove(id);
        }
    }
}