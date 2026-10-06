using System;
using System.Linq;
using Scribble.Services.CanvasStateService.State;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using Scribble.Shared.Lib.Events;
using Scribble.Tools.PointerTools.ArrowTool;
using Scribble.Utils;
using SkiaSharp;

namespace Scribble.Services.CanvasStateService.Handlers;

/// <summary>
/// Handles replay and fast-path for stroke-related events
/// </summary>
public class StrokeReplayHandler :
    IEventReplayHandler<StartStrokeEvent>,
    IEventReplayHandler<PencilStrokeLineToEvent>,
    IEventReplayHandler<LineStrokeLineToEvent>,
    IEventReplayHandler<AddPolylineNodeEvent>,
    IEventReplayHandler<MovePolylineNodeEvent>,
    IEventReplayHandler<EndStrokeEvent>,
    IFastPathHandler<StartStrokeEvent>,
    IFastPathHandler<EndStrokeEvent>,
    IFastPathHandler<PencilStrokeLineToEvent>,
    IFastPathHandler<LineStrokeLineToEvent>,
    IFastPathHandler<AddPolylineNodeEvent>,
    IFastPathHandler<MovePolylineNodeEvent>
{
    // Replay handlers

    public void Replay(StartStrokeEvent ev, CanvasState ctx)
    {
        var newLinePath = new SKPathBuilder();
        newLinePath.MoveTo(ev.StartPoint);
        ctx.PaintableStrokes[ev.StrokeId] = new DrawStroke
        {
            Id = ev.StrokeId,
            Paint = ev.StrokePaint.Clone(),
            Path = newLinePath.Snapshot(),
            RawPoints = [new StrokePoint(ev.StartPoint, ev.TimeStamp.Ticks / TimeSpan.TicksPerMillisecond)],
            ToolType = ev.ToolType,
            ToolOptions = ev.ToolOptions,
            CreatorConnectionId = ev.CreatorConnectionId,
            LayerIndex = ctx.MaxLayerIndex
        };
    }

    public void Replay(PencilStrokeLineToEvent ev, CanvasState ctx)
    {
        if (ctx.PaintableStrokes.TryGetValue(ev.StrokeId, out var pStroke) && pStroke is DrawStroke dsPencil)
        {
            dsPencil.RawPoints.Add(new StrokePoint(ev.Point,
                ev.TimeStamp.Ticks / TimeSpan.TicksPerMillisecond));
            var stable = dsPencil.StablePath;
            var newPath = new SKPathBuilder();
            FreehandPathBuilder.AppendPoint(newPath, ref stable, dsPencil.RawPoints);
            dsPencil.StablePath = stable;
            dsPencil.Path = newPath.Snapshot();
        }
    }

    public void Replay(LineStrokeLineToEvent ev, CanvasState ctx)
    {
        if (ctx.PaintableStrokes.TryGetValue(ev.StrokeId, out var paintableStroke) &&
            paintableStroke is DrawStroke ds)
        {
            RebuildLinePath(ds, ev.EndPoint);
        }
    }

    public void Replay(AddPolylineNodeEvent ev, CanvasState ctx)
    {
        if (ctx.PaintableStrokes.TryGetValue(ev.StrokeId, out var paintableStroke) &&
            paintableStroke is DrawStroke ds)
        {
            ds.RawPoints.Add(new StrokePoint(ev.Point, ev.TimeStamp.Ticks / TimeSpan.TicksPerMillisecond));
            // Rebuild the path from all raw points with the last point as the endpoint
            RebuildLinePath(ds, ds.RawPoints[^1].Point);
        }
    }

    public void Replay(MovePolylineNodeEvent ev, CanvasState ctx)
    {
        if (ctx.PaintableStrokes.TryGetValue(ev.StrokeId, out var paintableStroke) &&
            paintableStroke is DrawStroke ds)
        {
            if (ev.NodeIndex >= 0 && ev.NodeIndex < ds.RawPoints.Count)
            {
                ds.RawPoints[ev.NodeIndex] =
                    new StrokePoint(ev.NewPosition, ev.TimeStamp.Ticks / TimeSpan.TicksPerMillisecond);
                RebuildLinePath(ds, ds.RawPoints[^1].Point);
            }
        }
    }

    public void Replay(EndStrokeEvent ev, CanvasState ctx)
    {
        // EndStrokeEvent has no replay effect on canvas state.
        // It exists only as a terminal event marker for undo/redo tracking.
    }

    // Fast-path handlers

    public bool TryApplyFastPath(PencilStrokeLineToEvent ev, CanvasState ctx)
    {
        if (ctx.PaintableStrokes.TryGetValue(ev.StrokeId, out var stroke) && stroke is DrawStroke ds)
        {
            ds.RawPoints.Add(new StrokePoint(ev.Point,
                ev.TimeStamp.Ticks / TimeSpan.TicksPerMillisecond));
            var stable = ds.StablePath;
            var newPath = new SKPathBuilder();
            FreehandPathBuilder.AppendPoint(newPath, ref stable, ds.RawPoints);
            ds.StablePath = stable;
            ds.Path = newPath.Snapshot();

            return true;
        }

        return false;
    }

    public bool TryApplyFastPath(LineStrokeLineToEvent ev, CanvasState ctx)
    {
        if (ctx.PaintableStrokes.TryGetValue(ev.StrokeId, out var stroke) && stroke is DrawStroke drawStroke)
        {
            RebuildLinePath(drawStroke, ev.EndPoint);
            return true;
        }

        return false;
    }

    public bool TryApplyFastPath(StartStrokeEvent ev, CanvasState ctx)
    {
        var newLinePath = new SKPathBuilder();
        newLinePath.MoveTo(ev.StartPoint);
        var ds = new DrawStroke
        {
            Id = ev.StrokeId,
            Paint = ev.StrokePaint.Clone(),
            Path = newLinePath.Snapshot(),
            RawPoints = [new StrokePoint(ev.StartPoint, ev.TimeStamp.Ticks / TimeSpan.TicksPerMillisecond)],
            ToolType = ev.ToolType,
            ToolOptions = ev.ToolOptions,
            CreatorConnectionId = ev.CreatorConnectionId,
            LayerIndex = ctx.ElementsWithLayers.Count
        };

        ctx.PaintableStrokes[ev.StrokeId] = ds;
        ctx.ElementsWithLayers.Add(ds);

        return true;
    }

    public bool TryApplyFastPath(EndStrokeEvent ev, CanvasState ctx)
    {
        return true;
    }

    public bool TryApplyFastPath(AddPolylineNodeEvent ev, CanvasState ctx)
    {
        if (ctx.PaintableStrokes.TryGetValue(ev.StrokeId, out var stroke) && stroke is DrawStroke ds)
        {
            ds.RawPoints.Add(new StrokePoint(ev.Point, ev.TimeStamp.Ticks / TimeSpan.TicksPerMillisecond));
            RebuildLinePath(ds, ds.RawPoints[^1].Point);
            return true;
        }

        return false;
    }

    public bool TryApplyFastPath(MovePolylineNodeEvent ev, CanvasState ctx)
    {
        if (ctx.PaintableStrokes.TryGetValue(ev.StrokeId, out var stroke) && stroke is DrawStroke ds)
        {
            if (ev.NodeIndex >= 0 && ev.NodeIndex < ds.RawPoints.Count)
            {
                ds.RawPoints[ev.NodeIndex] =
                    new StrokePoint(ev.NewPosition, ev.TimeStamp.Ticks / TimeSpan.TicksPerMillisecond);
                RebuildLinePath(ds, ds.RawPoints[^1].Point);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Builds the path for strokes: Rectangles, Ellipses, Lines, and Arrows.
    /// For Line/Arrow, supports multi-point polylines by using all RawPoints
    /// plus a tentative endpoint for rubberband preview.
    /// </summary>
    /// <param name="stroke">The DrawStroke object</param>
    /// <param name="endPoint">The tentative endpoint (mouse position for rubberband, or last confirmed node for finalized strokes)</param>
    private static void RebuildLinePath(DrawStroke stroke, SKPoint endPoint)
    {
        var lineStartPoint = stroke.RawPoints[0].Point;
        var newPath = new SKPathBuilder();
        var isCurvedStroke = stroke.Paint.StrokeJoin == SKStrokeJoin.Round;

        switch (stroke.ToolType)
        {
            case ToolType.Rectangle:
                PathUtilities.BuildRectangleShape(newPath, lineStartPoint, endPoint, isCurvedStroke);
                break;
            case ToolType.Diamond:
                PathUtilities.BuildDiamondShape(newPath, lineStartPoint, endPoint, isCurvedStroke);
                break;
            case ToolType.Ellipse:
                PathUtilities.BuildEllipseShape(newPath, lineStartPoint, endPoint);
                break;
            default:
            {
                // Line or Arrow: build polyline from all confirmed raw points + tentative endpoint
                var allPoints = stroke.RawPoints.Select(rp => rp.Point).ToList();

                // Add the tentative endpoint for rubberband preview,
                // but only if it differs from the last confirmed point
                if (allPoints.Count > 0 && allPoints[^1] != endPoint)
                {
                    allPoints.Add(endPoint);
                }

                if (allPoints.Count > 0)
                {
                    PathUtilities.BuildPolylinePath(newPath, allPoints, stroke.Paint.StrokeJoin == SKStrokeJoin.Round);

                    // Arrow head on the final segment
                    if (stroke.ToolType == ToolType.Arrow && allPoints.Count >= 2)
                    {
                        var lastSegStart = allPoints[^2];
                        var lastSegEnd = allPoints[^1];
                        var (p1, p2) = ArrowTool.GetArrowHeadPoints(lastSegStart, lastSegEnd,
                            stroke.Paint.StrokeWidth);

                        newPath.MoveTo(lastSegEnd);
                        newPath.LineTo(p1);

                        newPath.MoveTo(lastSegEnd);
                        newPath.LineTo(p2);
                    }
                }

                break;
            }
        }

        var finalPath = newPath.Snapshot();
        if (!stroke.TransformMatrix.IsIdentity)
        {
            finalPath.Transform(stroke.TransformMatrix);
        }

        stroke.Path = finalPath;
    }
}