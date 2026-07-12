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
/// Handles replay and fast-path for selection-related events:
/// CreateSelectionBoundEvent, IncreaseSelectionBoundEvent, EndSelectionEvent,
/// ClearSelectionEvent, SelectByIdsEvent
/// </summary>
public class SelectionReplayHandler :
    IEventReplayHandler<CreateSelectionBoundEvent>,
    IEventReplayHandler<IncreaseSelectionBoundEvent>,
    IEventReplayHandler<EndSelectionEvent>,
    IEventReplayHandler<ClearSelectionEvent>,
    IEventReplayHandler<SelectByIdsEvent>,
    IFastPathHandler<IncreaseSelectionBoundEvent>,
    IFastPathHandler<SelectByIdsEvent>
{
    // Replay handlers

    public void Replay(CreateSelectionBoundEvent ev, CanvasState ctx)
    {
        var selectionPath = new SKPath();
        selectionPath.MoveTo(ev.StartPoint);

        ctx.SelectionBounds.Clear();

        var selectionBound = new SelectionBound
        {
            Id = ev.BoundId,
            Path = selectionPath,
            CreatorConnectionId = ev.CreatorConnectionId
        };
        ctx.SelectionBounds[ev.BoundId] = selectionBound;
    }

    public void Replay(IncreaseSelectionBoundEvent ev, CanvasState ctx)
    {
        if (ctx.SelectionBounds.TryGetValue(ev.BoundId, out var bound))
        {
            var boundOrigin = bound.Path.Points[0];
            bound.Path.Reset();
            bound.Path.MoveTo(boundOrigin);
            bound.Path.LineTo(ev.Point);

            // Check for strokes that are within this bound
            var top = Math.Min(boundOrigin.Y, ev.Point.Y);
            var left = Math.Min(boundOrigin.X, ev.Point.X);
            var boundRect = SKRect.Create(new SKPoint(left, top), Utilities.GetSize(boundOrigin, ev.Point));
            CheckAndSelect(boundRect, bound, [..ctx.PaintableStrokes.Values, ..ctx.CanvasImages.Values],
                ownerFilter: bound.CreatorConnectionId);
        }
    }

    public void Replay(EndSelectionEvent ev, CanvasState ctx)
    {
        if (ctx.SelectionBounds.TryGetValue(ev.BoundId, out var bound))
        {
            // Remove and mark selection as stale if it has no targets
            if (bound.Targets.Count == 0)
            {
                ctx.StaleActionIds.Add(ev.ActionId);
                ctx.SelectionBounds.Remove(bound.Id);
            }
        }
    }

    public void Replay(ClearSelectionEvent ev, CanvasState ctx)
    {
        // Check if there are any selection bounds for this user.
        // If not, the clear selection action can be marked stale.
        var hasSelectionBounds =
            ctx.SelectionBounds.Values.Any(bound => bound.CreatorConnectionId == ev.CreatorConnectionId);

        if (ev.CreatorConnectionId == ctx.MyConnectionId && !hasSelectionBounds)
        {
            ctx.StaleActionIds.Add(ev.ActionId);
        }

        var boundsToRemove = new List<Guid>();
        foreach (var bound in ctx.SelectionBounds.Values)
        {
            if (bound.CreatorConnectionId == ev.CreatorConnectionId)
            {
                boundsToRemove.Add(bound.Id);
            }
        }

        foreach (var boundId in boundsToRemove)
        {
            ctx.SelectionBounds.Remove(boundId);
        }

        if (ctx.MyConnectionId == ev.CreatorConnectionId)
        {
            ctx.ActiveSelectionBoundId = null;
            ctx.SelectedElementIds.Clear();
        }
    }

    // Fast-path handler

    public bool TryApplyFastPath(IncreaseSelectionBoundEvent ev, CanvasState ctx)
    {
        if (ctx.SelectionBounds.TryGetValue(ev.BoundId, out var bound))
        {
            var boundOrigin = bound.Path.Points[0];
            bound.Path.Reset();
            bound.Path.MoveTo(boundOrigin);
            bound.Path.LineTo(ev.Point);

            var top = Math.Min(boundOrigin.Y, ev.Point.Y);
            var left = Math.Min(boundOrigin.X, ev.Point.X);
            var boundRect = SKRect.Create(new SKPoint(left, top),
                Utilities.GetSize(boundOrigin, ev.Point));
            CheckAndSelect(boundRect, bound, ctx.ElementsWithLayers,
                ownerFilter: bound.CreatorConnectionId);

            if (bound.CreatorConnectionId == ctx.MyConnectionId)
            {
                ctx.ActiveSelectionBoundId = ev.BoundId;
                ctx.SelectedElementIds.Clear();
                ctx.SelectedElementIds.AddRange(bound.Targets);
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Finds all strokes that are within the selection boundary
    /// </summary>
    /// <param name="boundRect">The selection bound's SKRect</param>
    /// <param name="bound">The selection bound</param>
    /// <param name="canvasElements">Collection of all current elements on the canvas</param>
    /// <param name="ownerFilter">SignalR connection id for the current client</param>
    private static void CheckAndSelect(SKRect boundRect, SelectionBound bound,
        IEnumerable<CanvasElement> canvasElements, string? ownerFilter = null)
    {
        bound.Targets.Clear();
        foreach (var element in canvasElements)
        {
            // In multi-user mode, only select elements the selector's creator owns.
            if (ownerFilter != null && element.CreatorConnectionId != ownerFilter)
                continue;

            switch (element)
            {
                case PaintableStroke stroke:
                {
                    var strokeBounds = stroke.Path.Bounds;
                    if (boundRect.Contains(strokeBounds))
                    {
                        bound.Targets.Add(stroke.Id);
                    }

                    break;
                }
                case CanvasImage image:
                {
                    if (boundRect.Contains(image.Bounds))
                    {
                        bound.Targets.Add(image.Id);
                    }

                    break;
                }
            }
        }
    }

    public void Replay(SelectByIdsEvent ev, CanvasState ctx)
    {
        ctx.SelectionBounds.Clear();

        var bound = new SelectionBound
        {
            Id = ev.BoundId,
            Path = new SKPath(),
            CreatorConnectionId = ev.CreatorConnectionId
        };

        // Only include IDs that still exist on the canvas (guards against undo
        // having removed an element that was originally in the selection)
        var validIds = ctx.PaintableStrokes.Keys
            .Concat(ctx.CanvasImages.Keys)
            .ToHashSet();

        foreach (var id in ev.ElementIds)
        {
            if (validIds.Contains(id))
            {
                bound.Targets.Add(id);
            }
        }

        // Mark stale if nothing survived, mirrors EndSelectionEvent behaviour
        if (bound.Targets.Count == 0)
        {
            ctx.StaleActionIds.Add(ev.ActionId);
            return;
        }

        ctx.SelectionBounds[ev.BoundId] = bound;
    }

    public bool TryApplyFastPath(SelectByIdsEvent ev, CanvasState ctx)
    {
        ctx.SelectionBounds.Clear();

        var bound = new SelectionBound
        {
            Id = ev.BoundId,
            Path = new SKPath(),
            CreatorConnectionId = ev.CreatorConnectionId
        };

        var validIds = ctx.ElementsWithLayers.Select(e => e.Id).ToHashSet();

        foreach (var id in ev.ElementIds)
        {
            if (validIds.Contains(id))
            {
                bound.Targets.Add(id);
            }
        }

        if (bound.Targets.Count == 0)
        {
            ctx.StaleActionIds.Add(ev.ActionId);
            return true;
        }

        ctx.SelectionBounds[ev.BoundId] = bound;

        if (ev.CreatorConnectionId == ctx.MyConnectionId)
        {
            ctx.ActiveSelectionBoundId = ev.BoundId;
            ctx.SelectedElementIds.Clear();
            ctx.SelectedElementIds.AddRange(bound.Targets);
        }

        return true;
    }
}