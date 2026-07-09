using System;
using Scribble.Services.CanvasStateService.State;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using Scribble.Shared.Lib.Events;
using Scribble.Tools.PointerTools.ArrowTool;
using Scribble.Utils;
using SkiaSharp;

namespace Scribble.Services.CanvasStateService.Handlers;

/// <summary>
/// Handles replay and fast-path for stroke-related events:
/// StartStrokeEvent, PencilStrokeLineToEvent, LineStrokeLineToEvent, EndStrokeEvent
/// </summary>
public class StrokeReplayHandler :
    IEventReplayHandler<StartStrokeEvent>,
    IEventReplayHandler<PencilStrokeLineToEvent>,
    IEventReplayHandler<LineStrokeLineToEvent>,
    IEventReplayHandler<EndStrokeEvent>,
    IFastPathHandler<StartStrokeEvent>,
    IFastPathHandler<EndStrokeEvent>,
    IFastPathHandler<PencilStrokeLineToEvent>,
    IFastPathHandler<LineStrokeLineToEvent>
{
    // Replay handlers

    public void Replay(StartStrokeEvent ev, CanvasState ctx)
    {
        var newLinePath = new SKPath();
        newLinePath.MoveTo(ev.StartPoint);
        ctx.PaintableStrokes[ev.StrokeId] = new DrawStroke
        {
            Id = ev.StrokeId,
            Paint = ev.StrokePaint.Clone(),
            Path = newLinePath,
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
            var newPath = new SKPath();
            FreehandPathBuilder.AppendPoint(newPath, ref stable, dsPencil.RawPoints);
            dsPencil.StablePath = stable;
            dsPencil.Path = newPath;
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
            var newPath = new SKPath();
            FreehandPathBuilder.AppendPoint(newPath, ref stable, ds.RawPoints);
            ds.StablePath = stable;
            ds.Path = newPath;

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
        var newLinePath = new SKPath();
        newLinePath.MoveTo(ev.StartPoint);
        var ds = new DrawStroke
        {
            Id = ev.StrokeId,
            Paint = ev.StrokePaint.Clone(),
            Path = newLinePath,
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

    /// <summary>
    /// Builds the path for strokes: Rectangles, Ellipses, Lines, and Arrows
    /// </summary>
    /// <param name="stroke">The DrawStroke object</param>
    /// <param name="endPoint">The line endpoint</param>
    private static void RebuildLinePath(DrawStroke stroke, SKPoint endPoint)
    {
        var lineStartPoint = stroke.RawPoints[0].Point;
        var newPath = new SKPath();

        if (stroke.ToolType == ToolType.Rectangle)
        {
            newPath.MoveTo(lineStartPoint);
            var left = Math.Min(lineStartPoint.X, endPoint.X);
            var top = Math.Min(lineStartPoint.Y, endPoint.Y);
            var rect = SKRect.Create(new SKPoint(left, top),
                Utilities.GetSize(lineStartPoint, endPoint));
            if (stroke.Paint.StrokeJoin == SKStrokeJoin.Miter)
            {
                newPath.AddRect(rect);
            }
            else
            {
                newPath.AddRoundRect(rect, 24f, 24f);
            }
        }
        else if (stroke.ToolType == ToolType.Ellipse)
        {
            newPath.MoveTo(lineStartPoint);
            var left = Math.Min(lineStartPoint.X, endPoint.X);
            var top = Math.Min(lineStartPoint.Y, endPoint.Y);
            var rect = SKRect.Create(new SKPoint(left, top),
                Utilities.GetSize(lineStartPoint, endPoint));
            newPath.AddOval(rect);
        }
        else
        {
            newPath.MoveTo(lineStartPoint);
            newPath.LineTo(endPoint);

            if (stroke.ToolType == ToolType.Arrow)
            {
                var (p1, p2) =
                    ArrowTool.GetArrowHeadPoints(lineStartPoint, endPoint,
                        stroke.Paint.StrokeWidth);

                newPath.MoveTo(endPoint);
                newPath.LineTo(p1);

                newPath.MoveTo(endPoint);
                newPath.LineTo(p2);
            }
        }

        stroke.Path = newPath;
    }
}