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
internal static class SelectionReplayHandler
{
    /// <summary>
    /// Start a marquee for the selecting client.
    /// </summary>
    public static void Apply(CreateSelectionBoundEvent ev, CanvasState ctx)
    {
        var selectionPath = new SKPathBuilder();
        selectionPath.MoveTo(ev.StartPoint);

        ctx.ClearSelectionBoundsForUser(ev.CreatorConnectionId);

        var selectionBound = new SelectionBound
        {
            Id = ev.BoundId,
            Path = selectionPath.Snapshot(),
            CreatorConnectionId = ev.CreatorConnectionId
        };
        ctx.SelectionBounds[ev.BoundId] = selectionBound;
    }

    /// <summary>
    /// Discard an empty selection gesture.
    /// </summary>
    public static void Apply(EndSelectionEvent ev, CanvasState ctx)
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

    /// <summary>
    /// Remove this client’s selection.
    /// </summary>
    public static void Apply(ClearSelectionEvent ev, CanvasState ctx)
    {
        // Check if there are any selection bounds for this user.
        // If not, the clear selection action can be marked stale.
        var hasSelectionBounds =
            ctx.SelectionBounds.Values.Any(bound => bound.CreatorConnectionId == ev.CreatorConnectionId);

        if (ev.CreatorConnectionId == ctx.MyConnectionId && !hasSelectionBounds)
        {
            ctx.StaleActionIds.Add(ev.ActionId);
        }

        ctx.ClearSelectionBoundsForUser(ev.CreatorConnectionId);

        if (ctx.MyConnectionId == ev.CreatorConnectionId)
        {
            ctx.ActiveSelectionBoundId = null;
            ctx.SelectedElementIds.Clear();
        }
    }

    /// <summary>
    /// Update the marquee and its enclosed targets.
    /// </summary>
    public static bool Apply(IncreaseSelectionBoundEvent ev, CanvasState ctx)
    {
        if (ctx.SelectionBounds.TryGetValue(ev.BoundId, out var bound))
        {
            var boundOrigin = bound.Path.Points[0];
            var builder = new SKPathBuilder();
            builder.MoveTo(boundOrigin);
            builder.LineTo(ev.Point);
            bound.Path = builder.Snapshot();

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
                    var strokeBounds = stroke.Path.TightBounds;
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

    /// <summary>
    /// Select surviving targets and refresh local selection state.
    /// </summary>
    public static bool Apply(SelectByIdsEvent ev, CanvasState ctx)
    {
        ctx.ClearSelectionBoundsForUser(ev.CreatorConnectionId);

        var bound = new SelectionBound
        {
            Id = ev.BoundId,
            Path = new SKPath(),
            CreatorConnectionId = ev.CreatorConnectionId
        };

        var validIds = ctx.ElementsWithLayers
            .Where(e => ev.CreatorConnectionId == null || e.CreatorConnectionId == ev.CreatorConnectionId)
            .Select(e => e.Id).ToHashSet();

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
            if (ev.CreatorConnectionId == ctx.MyConnectionId)
            {
                ctx.ActiveSelectionBoundId = null;
                ctx.SelectedElementIds.Clear();
            }

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