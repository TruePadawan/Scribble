using System;
using System.Collections.Generic;
using Scribble.Services.CanvasStateService.State;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using Scribble.Shared.Lib.Events;
using Scribble.Utils;
using SkiaSharp;

namespace Scribble.Services.CanvasStateService.Handlers;

/// <summary>
/// Handles replay and fast-path for eraser-related events:
/// StartEraseStrokeEvent, EraseStrokeLineToEvent, TriggerEraseEvent
/// </summary>
public class EraserReplayHandler :
    IEventReplayHandler<StartEraseStrokeEvent>,
    IEventReplayHandler<EraseStrokeLineToEvent>,
    IEventReplayHandler<TriggerEraseEvent>,
    IFastPathHandler<EraseStrokeLineToEvent>
{
    // Replay handlers

    public void Replay(StartEraseStrokeEvent ev, CanvasState ctx)
    {
        var eraserPath = new SKPath();
        eraserPath.MoveTo(ev.StartPoint);
        var newEraserStroke = new EraserStroke
        {
            Id = ev.StrokeId,
            Path = eraserPath,
            CreatorConnectionId = ev.CreatorConnectionId
        };


        // Find all targets for erasing
        CheckAndErase(ev.StartPoint, [..ctx.PaintableStrokes.Values, ..ctx.CanvasImages.Values], newEraserStroke,
            ownerFilter: ev.CreatorConnectionId);

        ctx.EraserStrokes[ev.StrokeId] = newEraserStroke;
    }

    public void Replay(EraseStrokeLineToEvent ev, CanvasState ctx)
    {
        if (ctx.EraserStrokes.TryGetValue(ev.StrokeId, out var currentEraserStroke))
        {
            // Use interpolation to find all targets for erasing
            var start = currentEraserStroke.Path.LastPoint;
            var end = ev.Point;
            var distance = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));

            var stepSize = 5.0;
            var steps = (int)Math.Ceiling(distance / stepSize);
            for (int s = 1; s <= steps; s++)
            {
                var completionPercentage = s / stepSize;
                var checkX = start.X + (end.X - start.X) * completionPercentage;
                var checkY = start.Y + (end.Y - start.Y) * completionPercentage;
                CheckAndErase(new SKPoint((float)checkX, (float)checkY),
                    [..ctx.PaintableStrokes.Values, ..ctx.CanvasImages.Values],
                    currentEraserStroke,
                    ownerFilter: currentEraserStroke.CreatorConnectionId);
            }

            currentEraserStroke.Path.LineTo(ev.Point);
        }
    }

    public void Replay(TriggerEraseEvent ev, CanvasState ctx)
    {
        if (ctx.EraserStrokes.TryGetValue(ev.StrokeId, out var currentEraserStroke))
        {
            // Erase all targets
            foreach (var targetId in currentEraserStroke.Targets)
            {
                ctx.PaintableStrokes.Remove(targetId);
                ctx.CanvasImages.Remove(targetId);
            }

            if (currentEraserStroke.Targets.Count == 0)
            {
                ctx.StaleActionIds.Add(ev.ActionId);
            }
        }
    }

    // Fast-path handler

    public bool TryApplyFastPath(EraseStrokeLineToEvent ev, CanvasState ctx)
    {
        if (ctx.EraserStrokes.TryGetValue(ev.StrokeId, out var currentEraserStroke))
        {
            var start = currentEraserStroke.Path.LastPoint;
            var end = ev.Point;
            var distance = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));

            const double stepSize = 5.0;
            var steps = (int)Math.Ceiling(distance / stepSize);
            for (var s = 1; s <= steps; s++)
            {
                var completionPercentage = s / stepSize;
                var checkX = start.X + (end.X - start.X) * completionPercentage;
                var checkY = start.Y + (end.Y - start.Y) * completionPercentage;
                CheckAndErase(new SKPoint((float)checkX, (float)checkY), ctx.ElementsWithLayers, currentEraserStroke,
                    ownerFilter: currentEraserStroke.CreatorConnectionId);
            }

            currentEraserStroke.Path.LineTo(ev.Point);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Marks the strokes that are a target for erasure
    /// </summary>
    /// <param name="eraserPoint">The latest point in the eraser's stroke</param>
    /// <param name="canvasElements">Collection of all current elements on the canvas</param>
    /// <param name="eraserStroke">The active eraser stroke</param>
    /// <param name="ownerFilter">SignalR connection id for the current client</param>
    private static void CheckAndErase(SKPoint eraserPoint, IEnumerable<CanvasElement> canvasElements,
        EraserStroke eraserStroke, string? ownerFilter = null)
    {
        foreach (var element in canvasElements)
        {
            // In multi-user mode, only erase elements the eraser's creator owns.
            if (ownerFilter != null && element.CreatorConnectionId != ownerFilter)
                continue;

            switch (element)
            {
                case PaintableStroke stroke:
                {
                    var strokeId = stroke.Id;
                    var endPoints = new[] { stroke.Path[0], stroke.Path.LastPoint };
                    var isLineLike = stroke is DrawStroke { ToolType: ToolType.Line or ToolType.Arrow };
                    if (isLineLike)
                    {
                        if (Utilities.IsPointNearLine(eraserPoint, endPoints, 10.0f))
                        {
                            stroke.IsToBeErased = true;
                            eraserStroke.Targets.Add(strokeId);
                        }
                    }
                    else
                    {
                        if (stroke.Path.Contains(eraserPoint.X, eraserPoint.Y) ||
                            // For handling very small/short pencil strokes
                            Utilities.IsPointNearLine(eraserPoint, endPoints, 10.0f))
                        {
                            stroke.IsToBeErased = true;
                            eraserStroke.Targets.Add(strokeId);
                        }
                    }

                    break;
                }
                case CanvasImage image:
                {
                    if (image.Bounds.Contains(eraserPoint.X, eraserPoint.Y))
                    {
                        image.IsToBeErased = true;
                        eraserStroke.Targets.Add(image.Id);
                    }

                    break;
                }
            }
        }
    }
}