using System;
using Scribble.Services.CanvasStateService.State;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.Events;
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
            stroke.Path.Reset();
            stroke.Path.MoveTo(lineStartPoint);
            var left = Math.Min(lineStartPoint.X, lineEndPoint.X);
            var top = Math.Min(lineStartPoint.Y, lineEndPoint.Y);
            var rect = SKRect.Create(new SKPoint(left, top),
                Utilities.GetSize(lineStartPoint, lineEndPoint));
            if (stroke.Paint.StrokeJoin == SKStrokeJoin.Miter)
            {
                stroke.Path.AddRect(rect);
            }
            else
            {
                stroke.Path.AddRoundRect(rect, 24f, 24f);
            }

            // Re-apply the rotation
            if (Math.Abs(rotationAngle) > 0.001f)
            {
                stroke.Path.Transform(
                    SKMatrix.CreateRotation(rotationAngle, center.X, center.Y));
            }
        }
    }
}