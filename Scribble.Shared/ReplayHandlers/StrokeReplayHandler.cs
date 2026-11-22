using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using Scribble.Shared.Lib.Events;
using Scribble.Shared.State;
using Scribble.Shared.Utils;
using SkiaSharp;

namespace Scribble.Shared.ReplayHandlers;

/// <summary>
/// Handles replay and fast-path for stroke-related events
/// </summary>
internal static class StrokeReplayHandler
{
    /// <summary>
    /// Extend the freehand path with one timed point.
    /// </summary>
    public static bool Apply(PencilStrokeLineToEvent ev, CanvasState ctx)
    {
        if (ctx.PaintableStrokes.TryGetValue(ev.StrokeId, out var stroke) && stroke is DrawStroke ds)
        {
            ds.RawPoints.Add(new StrokePoint(ev.Point,
                ev.TimeStamp.Ticks / TimeSpan.TicksPerMillisecond));
            var stable = ds.StablePath;
            using var newPath = new SKPathBuilder();
            FreehandPathBuilder.AppendPoint(newPath, ref stable, ds.RawPoints);
            ds.StablePath = stable;
            // Replaying long strokes otherwise retains a native path for every intermediate point.
            var previous = ds.Path;
            ds.Path = newPath.Snapshot();
            previous.Dispose();

            return true;
        }

        return false;
    }

    /// <summary>
    /// Rebuild the preview up to the current pointer position.
    /// </summary>
    public static bool Apply(LineStrokeLineToEvent ev, CanvasState ctx)
    {
        if (ctx.PaintableStrokes.TryGetValue(ev.StrokeId, out var stroke) && stroke is DrawStroke drawStroke)
        {
            RebuildLinePath(drawStroke, ev.EndPoint);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Insert the first stroke point above the existing drawing.
    /// </summary>
    public static bool Apply(StartStrokeEvent ev, CanvasState ctx)
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
            CreatorConnectionId = ev.CreatorConnectionId
        };

        ctx.AddOnTop(ds, ctx.PaintableStrokes);

        return true;
    }

    /// <summary>
    /// A completed stroke already has its geometry and only needs an undo marker.
    /// </summary>
    public static bool Apply(EndStrokeEvent ev, CanvasState ctx)
    {
        return true;
    }

    /// <summary>
    /// Commit a vertex and rebuild the polyline.
    /// </summary>
    public static bool Apply(AddPolylineNodeEvent ev, CanvasState ctx)
    {
        if (ctx.PaintableStrokes.TryGetValue(ev.StrokeId, out var stroke) && stroke is DrawStroke ds)
        {
            ds.RawPoints.Add(new StrokePoint(ev.Point, ev.TimeStamp.Ticks / TimeSpan.TicksPerMillisecond));
            RebuildLinePath(ds, ds.RawPoints[^1].Point);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Replace a vertex and rebuild the adjoining segments.
    /// </summary>
    public static bool Apply(MovePolylineNodeEvent ev, CanvasState ctx)
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
                        var (p1, p2) = PathUtilities.GetArrowHeadPoints(lastSegStart, lastSegEnd,
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