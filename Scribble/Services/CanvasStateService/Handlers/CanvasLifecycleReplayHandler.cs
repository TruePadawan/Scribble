using System;
using System.Collections.Generic;
using System.Linq;
using Scribble.Services.CanvasStateService.Context;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using Scribble.Utils;
using SkiaSharp;

namespace Scribble.Services.CanvasStateService.Handlers;

/// <summary>
/// Handles replay for canvas lifecycle events:
/// LoadCanvasEvent, AddImageEvent, SetElementLayerEvent, NudgeElementLayerEvent, PasteCanvasElementsEvent
/// </summary>
public class CanvasLifecycleReplayHandler :
    IEventReplayHandler<LoadCanvasEvent>,
    IEventReplayHandler<AddImageEvent>,
    IEventReplayHandler<SetElementLayerEvent>,
    IEventReplayHandler<NudgeElementLayerEvent>,
    IEventReplayHandler<PasteCanvasElementsEvent>
{
    public void Replay(LoadCanvasEvent ev, ReplayContext ctx)
    {
        ctx.PaintableStrokes.Clear();
        ctx.CanvasImages.Clear();
        ctx.EraserStrokes.Clear();
        ctx.EraserHeads.Clear();
        ctx.SelectionBounds.Clear();

        foreach (var element in ev.CanvasElements)
        {
            if (element is IClonable copyable)
            {
                var cloned = copyable.Clone(preserveId: true);
                if (cloned is PaintableStroke paintable)
                {
                    ctx.PaintableStrokes[paintable.Id] = paintable;
                    ctx.MaxLayerIndex = Math.Max(ctx.MaxLayerIndex, paintable.LayerIndex);
                }
                else if (cloned is CanvasImage image)
                {
                    ctx.CanvasImages[image.Id] = image;
                    ctx.MaxLayerIndex = Math.Max(ctx.MaxLayerIndex, image.LayerIndex);
                }
            }
        }
    }

    public void Replay(AddImageEvent ev, ReplayContext ctx)
    {
        var imageBounds = SKRect.Create(ev.Position, ev.Size);
        ctx.CanvasImages[ev.ImageId] = new CanvasImage(ev.ImageId)
        {
            ImageBase64String = ev.ImageBase64String,
            Bounds = imageBounds,
            CreatorConnectionId = ev.CreatorConnectionId,
            LayerIndex = ctx.MaxLayerIndex
        };
    }

    public void Replay(SetElementLayerEvent ev, ReplayContext ctx)
    {
        foreach (var elementId in ev.TargetElementIds)
        {
            if (ctx.PaintableStrokes.TryGetValue(elementId, out var stroke))
            {
                stroke.LayerIndex = ev.NewLayerIndex;
                ctx.MaxLayerIndex = Math.Max(ctx.MaxLayerIndex, stroke.LayerIndex);
            }
            else if (ctx.CanvasImages.TryGetValue(elementId, out var image))
            {
                image.LayerIndex = ev.NewLayerIndex;
                ctx.MaxLayerIndex = Math.Max(ctx.MaxLayerIndex, image.LayerIndex);
            }
        }

        ctx.MaxLayerIndex = Math.Max(ctx.MaxLayerIndex, ev.NewLayerIndex);
        ctx.MinLayerIndex = Math.Min(ctx.MinLayerIndex, ev.NewLayerIndex);
    }

    public void Replay(NudgeElementLayerEvent ev, ReplayContext ctx)
    {
        var newMinLayerIndex = 0;
        var newMaxLayerIndex = 0;
        foreach (var elementId in ev.TargetElementIds)
        {
            if (ctx.PaintableStrokes.TryGetValue(elementId, out var stroke))
            {
                var newLayer = stroke.LayerIndex + ev.Offset;

                stroke.LayerIndex = newLayer;
                newMaxLayerIndex = Math.Max(newMaxLayerIndex, newLayer);
                newMinLayerIndex = Math.Min(newMinLayerIndex, newLayer);
            }
            else if (ctx.CanvasImages.TryGetValue(elementId, out var image))
            {
                var newLayer = image.LayerIndex + ev.Offset;

                image.LayerIndex = newLayer;
                newMaxLayerIndex = Math.Max(newMaxLayerIndex, newLayer);
                newMinLayerIndex = Math.Min(newMinLayerIndex, newLayer);
            }
        }

        ctx.MinLayerIndex = Math.Min(ctx.MinLayerIndex, newMinLayerIndex);
        ctx.MaxLayerIndex = Math.Max(ctx.MaxLayerIndex, newMaxLayerIndex);
    }

    public void Replay(PasteCanvasElementsEvent ev, ReplayContext ctx)
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
        ctx.SelectionBounds.Clear();
        var pasteSelectionPath = new SKPath();
        var selectionRect = Utilities.GetElementsBounds(pastedElements);
        pasteSelectionPath.AddRect(selectionRect);

        var pasteSelectionBound = new SelectionBound(ev.SelectionBoundId)
        {
            Path = pasteSelectionPath,
            CreatorConnectionId = ev.CreatorConnectionId,
            Targets = [..pastedElements.Select(e => e.Id)]
        };
        ctx.SelectionBounds[ev.SelectionBoundId] = pasteSelectionBound;
    }
}