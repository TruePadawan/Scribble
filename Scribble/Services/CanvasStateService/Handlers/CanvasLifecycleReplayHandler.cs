using System;
using System.Collections.Generic;
using System.Linq;
using Scribble.Services.CanvasStateService.State;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using Scribble.Shared.Lib.Events;
using Scribble.Utils;
using SkiaSharp;

namespace Scribble.Services.CanvasStateService.Handlers;

/// <summary>
/// Handles replay for canvas lifecycle events:
/// LoadCanvasEvent, AddImageEvent, SetElementLayerEvent, NudgeElementLayerEvent, PasteCanvasElementsEvent
/// </summary>
internal static class CanvasLifecycleReplayHandler
{
    // Replace the board with detached copies of the supplied elements.
    public static void Apply(LoadCanvasEvent ev, CanvasState ctx)
    {
        ctx.PaintableStrokes.Clear();
        ctx.CanvasImages.Clear();
        ctx.EraserStrokes.Clear();

        ctx.SelectionBounds.Clear();

        foreach (var element in ev.CanvasElements)
        {
            if (element is IClonable copyable)
            {
                var cloned = copyable.Clone(preserveId: true);
                if (cloned is PaintableStroke paintable)
                {
                    ctx.PaintableStrokes[paintable.Id] = paintable;
                }
                else if (cloned is CanvasImage image)
                {
                    ctx.CanvasImages[image.Id] = image;
                }
            }
        }
    }

    // Place the image above the existing drawing.
    public static void Apply(AddImageEvent ev, CanvasState ctx)
    {
        var imageBounds = SKRect.Create(ev.Position, ev.Size);
        ctx.AddOnTop(new CanvasImage
        {
            Id = ev.ImageId,
            ImageBase64String = ev.ImageBase64String,
            Bounds = imageBounds,
            CreatorConnectionId = ev.CreatorConnectionId
        }, ctx.CanvasImages);
    }

    // Assign the requested layer before normalizing drawing order.
    public static void Apply(SetElementLayerEvent ev, CanvasState ctx)
    {
        foreach (var elementId in ev.TargetElementIds)
        {
            if (ctx.PaintableStrokes.TryGetValue(elementId, out var stroke))
            {
                stroke.LayerIndex = ev.NewLayerIndex;
            }
            else if (ctx.CanvasImages.TryGetValue(elementId, out var image))
            {
                image.LayerIndex = ev.NewLayerIndex;
            }
        }

    }

    // Move targets through the current drawing order.
    public static void Apply(NudgeElementLayerEvent ev, CanvasState ctx)
    {
        // When a moved element lands on a neighbor, cross that neighbor in the requested direction.
        var targets = ev.TargetElementIds.ToHashSet();
        var ordered = ctx.ElementsWithLayers
            .OrderBy(element => (long)element.LayerIndex + (targets.Contains(element.Id) ? ev.Offset : 0))
            .ThenByDescending(element => element.LayerIndex)
            .ToArray();
        for (var i = 0; i < ordered.Length; i++) ordered[i].LayerIndex = i;
    }

    // Copy, position and select the pasted elements.
    public static void Apply(PasteCanvasElementsEvent ev, CanvasState ctx)
    {
        List<CanvasElement> pastedElements = [];
        foreach (var element in ev.CopiedElements)
        {
            if (element is IClonable copyable)
            {
                pastedElements.Add(copyable.Clone(preserveId: true));
            }
        }

        var copiedElementsBounds = Utilities.GetElementsBounds(pastedElements);
        var boundsMiddlePos = new SKPoint(copiedElementsBounds.MidX, copiedElementsBounds.MidY);
        var delta = ev.Position - boundsMiddlePos;
        // Translate all elements such that the middle of the total bound is at the pointer position
        TransformReplayHandler.MoveElements(pastedElements, delta);
        foreach (var copiedElement in pastedElements)
        {
            switch (copiedElement)
            {
                case PaintableStroke stroke:
                    ctx.PaintableStrokes[stroke.Id] = stroke;
                    break;
                case CanvasImage image:
                    ctx.CanvasImages[image.Id] = image;
                    break;
            }
        }

        // Automatically select the pasted elements
        ctx.ClearSelectionBoundsForUser(ev.CreatorConnectionId);
        var pasteSelectionPath = new SKPathBuilder();
        var selectionRect = Utilities.GetElementsBounds(pastedElements);
        pasteSelectionPath.AddRect(selectionRect);

        var pasteSelectionBound = new SelectionBound
        {
            Id = ev.SelectionBoundId,
            Path = pasteSelectionPath.Snapshot(),
            CreatorConnectionId = ev.CreatorConnectionId,
            Targets = [..pastedElements.Select(e => e.Id)]
        };
        ctx.SelectionBounds[ev.SelectionBoundId] = pasteSelectionBound;
    }
}