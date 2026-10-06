using System;
using System.Collections.Generic;
using Scribble.Services.CanvasStateService.State;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using Scribble.Shared.Lib.Events;
using Scribble.Tools.PointerTools.ArrowTool;
using Scribble.Utils;
using SkiaSharp;

namespace Scribble.Services.CanvasStateService.Handlers;

/// <summary>
/// Handles replay for stroke property update events:
/// UpdateStrokeColorEvent, UpdateStrokeThicknessEvent, UpdateStrokeStyleEvent,
/// UpdateStrokeFillColorEvent, UpdateStrokeEdgeTypeEvent
/// </summary>
public class PropertyReplayHandler :
    IEventReplayHandler<UpdateStrokeColorEvent>,
    IEventReplayHandler<UpdateStrokeThicknessEvent>,
    IEventReplayHandler<UpdateStrokeStyleEvent>,
    IEventReplayHandler<UpdateStrokeFillColorEvent>,
    IEventReplayHandler<UpdateStrokeEdgeTypeEvent>
{
    public void Replay(UpdateStrokeColorEvent ev, CanvasState ctx)
    {
        foreach (var strokeId in ev.StrokeIds)
        {
            ctx.PaintableStrokes[strokeId].Paint.Color = ev.NewColor;
        }
    }

    public void Replay(UpdateStrokeThicknessEvent ev, CanvasState ctx)
    {
        foreach (var strokeId in ev.StrokeIds)
        {
            ctx.PaintableStrokes[strokeId].Paint.StrokeWidth = ev.NewThickness;
        }
    }

    public void Replay(UpdateStrokeStyleEvent ev, CanvasState ctx)
    {
        foreach (var strokeId in ev.StrokeIds)
        {
            ctx.PaintableStrokes[strokeId].Paint.DashIntervals = ev.NewDashIntervals;
        }
    }

    public void Replay(UpdateStrokeFillColorEvent ev, CanvasState ctx)
    {
        foreach (var strokeId in ev.StrokeIds)
        {
            ctx.PaintableStrokes[strokeId].Paint.FillColor = ev.NewFillColor;
        }
    }

    public void Replay(UpdateStrokeEdgeTypeEvent ev, CanvasState ctx)
    {
        foreach (var strokeId in ev.StrokeIds)
        {
            var stroke = ctx.PaintableStrokes[strokeId];
            stroke.Paint.StrokeJoin = ev.NewStrokeJoin;
            // Recreate the stroke paths, preserving any rotation

            if (stroke is DrawStroke ds && (ds.ToolType == ToolType.Line || ds.ToolType == ToolType.Arrow))
            {
                var nodes = new List<SKPoint>();
                using var iterator = ds.Path.CreateIterator(false);
                var pts = new SKPoint[4];
                SKPathVerb verb;
                bool firstMoveSeen = false;

                while ((verb = iterator.Next(pts)) != SKPathVerb.Done)
                {
                    if (verb == SKPathVerb.Move)
                    {
                        if (firstMoveSeen) break; // Arrowhead or other contour started
                        firstMoveSeen = true;
                        nodes.Add(pts[0]);
                    }
                    else if (verb == SKPathVerb.Line) nodes.Add(pts[1]);
                    else if (verb == SKPathVerb.Cubic) nodes.Add(pts[3]);
                    else if (verb == SKPathVerb.Quad) nodes.Add(pts[2]);
                }

                if (nodes.Count > 0)
                {
                    var builder = new SKPathBuilder();
                    PathUtilities.BuildPolylinePath(builder, nodes, ds.Paint.StrokeJoin == SKStrokeJoin.Round);

                    if (ds.ToolType == ToolType.Arrow && nodes.Count >= 2)
                    {
                        var lastSegStart = nodes[^2];
                        var lastSegEnd = nodes[^1];
                        var (p1, p2) = ArrowTool.GetArrowHeadPoints(
                            lastSegStart, lastSegEnd, ds.Paint.StrokeWidth);

                        builder.MoveTo(lastSegEnd);
                        builder.LineTo(p1);
                        builder.MoveTo(lastSegEnd);
                        builder.LineTo(p2);
                    }
                    ds.Path = builder.Snapshot();
                }
            }
            else
            {
                // Detect a rotation angle if any from the first edge of the rect/roundrect sub-path
                var points = stroke.Path.Points;
                var rotationAngle = (float)Math.Atan2(
                    points[2].Y - points[1].Y,
                    points[2].X - points[1].X);

                // Un-rotate around the shape's center to recover axis-aligned dimensions
                var center = new SKPoint(
                    stroke.Path.TightBounds.MidX,
                    stroke.Path.TightBounds.MidY);

                using var unrotatedPath = new SKPath(stroke.Path);
                if (Math.Abs(rotationAngle) > 0.001f)
                {
                    unrotatedPath.Transform(
                        SKMatrix.CreateRotation(-rotationAngle, center.X, center.Y));
                }

                var bounds = unrotatedPath.Bounds;
                var lineStartPoint = unrotatedPath.Points[0];
                var lineEndPoint = new SKPoint(
                    bounds.Left + bounds.Right - lineStartPoint.X,
                    bounds.Top + bounds.Bottom - lineStartPoint.Y
                );

                // Rebuild the path with the new edge type
                var builder = new SKPathBuilder();
                if (stroke is DrawStroke dsShape && dsShape.ToolType == ToolType.Ellipse)
                {
                    PathUtilities.BuildEllipseShape(builder, lineStartPoint, lineEndPoint);
                }
                else
                {
                    PathUtilities.BuildRectangleShape(builder, lineStartPoint, lineEndPoint, stroke.Paint.StrokeJoin == SKStrokeJoin.Round);
                }

                var finalPath = builder.Snapshot();
                // Re-apply the rotation
                if (Math.Abs(rotationAngle) > 0.001f)
                {
                    finalPath.Transform(
                        SKMatrix.CreateRotation(rotationAngle, center.X, center.Y));
                }
                stroke.Path = finalPath;
            }
        }
    }
}