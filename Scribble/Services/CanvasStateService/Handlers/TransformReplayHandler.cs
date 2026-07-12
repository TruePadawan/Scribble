using System.Collections.Generic;
using Scribble.Services.CanvasStateService.State;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using Scribble.Shared.Lib.Events;
using SkiaSharp;

namespace Scribble.Services.CanvasStateService.Handlers;

/// <summary>
/// Handles replay and fast-path for transform-related events:
/// MoveCanvasElementsEvent, RotateCanvasElementsEvent, ScaleCanvasElementsEvent
/// </summary>
public class TransformReplayHandler :
    IEventReplayHandler<MoveCanvasElementsEvent>,
    IEventReplayHandler<RotateCanvasElementsEvent>,
    IEventReplayHandler<ScaleCanvasElementsEvent>,
    IFastPathHandler<MoveCanvasElementsEvent>,
    IFastPathHandler<RotateCanvasElementsEvent>,
    IFastPathHandler<ScaleCanvasElementsEvent>
{
    // Replay handlers

    public void Replay(MoveCanvasElementsEvent ev, CanvasState ctx)
    {
        if (ctx.SelectionBounds.TryGetValue(ev.BoundId, out var bound))
        {
            List<CanvasElement> elements = [];
            foreach (var boundTargetId in bound.Targets)
            {
                if (ctx.PaintableStrokes.TryGetValue(boundTargetId, out var stroke))
                {
                    elements.Add(stroke);
                }
                else if (ctx.CanvasImages.TryGetValue(boundTargetId, out var image))
                {
                    elements.Add(image);
                }
            }

            MoveElements(elements, ev.Delta);
        }
    }

    public void Replay(RotateCanvasElementsEvent ev, CanvasState ctx)
    {
        if (ctx.SelectionBounds.TryGetValue(ev.BoundId, out var bound))
        {
            var rotationMatrix = SKMatrix.CreateRotation(ev.DegreesRad, ev.Center.X, ev.Center.Y);
            foreach (var boundTargetId in bound.Targets)
            {
                if (ctx.PaintableStrokes.TryGetValue(boundTargetId, out var stroke))
                {
                    stroke.Rotation += ev.DegreesRad;
                    stroke.Path.Transform(rotationMatrix);
                    // Keep track of the transformations applied to the stroke
                    stroke.TransformMatrix = stroke.TransformMatrix.PostConcat(rotationMatrix);
                }
                else if (ctx.CanvasImages.TryGetValue(boundTargetId, out var image))
                {
                    image.Rotation += ev.DegreesRad;
                    // Rotate the bounds center around the rotation pivot
                    var imgCenter = new SKPoint(image.Bounds.MidX, image.Bounds.MidY);
                    var rotatedPoint = rotationMatrix.MapPoint(imgCenter);
                    var bounds = image.Bounds;
                    bounds.Offset(rotatedPoint.X - imgCenter.X, rotatedPoint.Y - imgCenter.Y);
                    image.Bounds = bounds;
                }
            }
        }
    }

    public void Replay(ScaleCanvasElementsEvent ev, CanvasState ctx)
    {
        if (ctx.SelectionBounds.TryGetValue(ev.BoundId, out var bound))
        {
            var scaleMatrix = SKMatrix.CreateScale(ev.Scale.X, ev.Scale.Y, ev.Center.X, ev.Center.Y);
            foreach (var boundTargetId in bound.Targets)
            {
                if (ctx.PaintableStrokes.TryGetValue(boundTargetId, out var stroke))
                {
                    stroke.Path.Transform(scaleMatrix);
                    // Keep track of the transformations applied to the stroke
                    stroke.TransformMatrix = stroke.TransformMatrix.PostConcat(scaleMatrix);
                }
                else if (ctx.CanvasImages.TryGetValue(boundTargetId, out var image))
                {
                    var topLeft = scaleMatrix.MapPoint(new SKPoint(image.Bounds.Left, image.Bounds.Top));
                    var bottomRight =
                        scaleMatrix.MapPoint(new SKPoint(image.Bounds.Right, image.Bounds.Bottom));

                    // If the x-axis becomes inverted, swap the x coordinates so that the bound's width stays positive
                    // Then flip the image horizontally
                    if (topLeft.X > bottomRight.X)
                    {
                        (topLeft.X, bottomRight.X) = (bottomRight.X, topLeft.X);
                        image.FlipX = !image.FlipX;
                    }

                    // If the y-axis becomes inverted, swap the y coordinates so that the bound's height stays positive
                    // Then flip the image vertically
                    if (topLeft.Y > bottomRight.Y)
                    {
                        (topLeft.Y, bottomRight.Y) = (bottomRight.Y, topLeft.Y);
                        image.FlipY = !image.FlipY;
                    }

                    image.Bounds = new SKRect(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
                }
            }
        }
    }

    // Fast-path handlers

    public bool TryApplyFastPath(MoveCanvasElementsEvent ev, CanvasState ctx)
    {
        if (ctx.SelectionBounds.TryGetValue(ev.BoundId, out var bound))
        {
            var translationMatrix = SKMatrix.CreateTranslation(ev.Delta.X, ev.Delta.Y);
            foreach (var boundTargetId in bound.Targets)
            {
                if (ctx.PaintableStrokes.TryGetValue(boundTargetId, out var stroke))
                {
                    stroke.Path.Transform(translationMatrix);
                    // Keep track of the transformations applied to the stroke
                    stroke.TransformMatrix = stroke.TransformMatrix.PostConcat(translationMatrix);
                }
                else if (ctx.CanvasImages.TryGetValue(boundTargetId, out var image))
                {
                    var bounds = image.Bounds;
                    bounds.Offset(ev.Delta);
                    image.Bounds = bounds;
                }
            }

            return true;
        }

        return false;
    }

    public bool TryApplyFastPath(RotateCanvasElementsEvent ev, CanvasState ctx)
    {
        if (ctx.SelectionBounds.TryGetValue(ev.BoundId, out var bound))
        {
            var rotationMatrix = SKMatrix.CreateRotation(ev.DegreesRad, ev.Center.X, ev.Center.Y);
            foreach (var boundTargetId in bound.Targets)
            {
                if (ctx.PaintableStrokes.TryGetValue(boundTargetId, out var stroke))
                {
                    stroke.Rotation += ev.DegreesRad;
                    stroke.Path.Transform(rotationMatrix);
                    // Keep track of the transformations applied to the stroke
                    stroke.TransformMatrix = stroke.TransformMatrix.PostConcat(rotationMatrix);
                }
                else if (ctx.CanvasImages.TryGetValue(boundTargetId, out var image))
                {
                    image.Rotation += ev.DegreesRad;
                    var imgCenter = new SKPoint(image.Bounds.MidX, image.Bounds.MidY);
                    var rotated = rotationMatrix.MapPoint(imgCenter);
                    var bounds = image.Bounds;
                    bounds.Offset(rotated.X - imgCenter.X, rotated.Y - imgCenter.Y);
                    image.Bounds = bounds;
                }
            }

            return true;
        }

        return false;
    }

    public bool TryApplyFastPath(ScaleCanvasElementsEvent ev, CanvasState ctx)
    {
        if (ctx.SelectionBounds.TryGetValue(ev.BoundId, out var bound))
        {
            var scaleMatrix = SKMatrix.CreateScale(ev.Scale.X, ev.Scale.Y, ev.Center.X,
                ev.Center.Y);
            foreach (var boundTargetId in bound.Targets)
            {
                if (ctx.PaintableStrokes.TryGetValue(boundTargetId, out var stroke))
                {
                    stroke.Path.Transform(scaleMatrix);
                    // Keep track of the transformations applied to the stroke
                    stroke.TransformMatrix = stroke.TransformMatrix.PostConcat(scaleMatrix);
                }
                else if (ctx.CanvasImages.TryGetValue(boundTargetId, out var image))
                {
                    var topLeft = scaleMatrix.MapPoint(new SKPoint(image.Bounds.Left, image.Bounds.Top));
                    var bottomRight =
                        scaleMatrix.MapPoint(new SKPoint(image.Bounds.Right, image.Bounds.Bottom));

                    if (topLeft.X > bottomRight.X)
                    {
                        (topLeft.X, bottomRight.X) = (bottomRight.X, topLeft.X);
                        image.FlipX = !image.FlipX;
                    }

                    if (topLeft.Y > bottomRight.Y)
                    {
                        (topLeft.Y, bottomRight.Y) = (bottomRight.Y, topLeft.Y);
                        image.FlipY = !image.FlipY;
                    }

                    image.Bounds = new SKRect(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
                }
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Translates a collection of canvas elements by the given delta
    /// </summary>
    internal static void MoveElements(IEnumerable<CanvasElement> elements, SKPoint delta)
    {
        var translationMatrix = SKMatrix.CreateTranslation(delta.X, delta.Y);
        foreach (var canvasElement in elements)
        {
            switch (canvasElement)
            {
                case PaintableStroke stroke:
                {
                    stroke.Path.Transform(translationMatrix);
                    // Keep track of the transformations applied to the stroke
                    stroke.TransformMatrix = stroke.TransformMatrix.PostConcat(translationMatrix);
                    break;
                }
                case CanvasImage image:
                {
                    // SKRect is a struct (value-type), so we need to create a new one to modify
                    var bounds = image.Bounds;
                    bounds.Offset(delta);
                    image.Bounds = bounds;
                    break;
                }
            }
        }
    }
}