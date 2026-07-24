using System;
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
            var scaleMatrix = BuildScaleMatrix(ev);
            foreach (var boundTargetId in bound.Targets)
            {
                if (ctx.PaintableStrokes.TryGetValue(boundTargetId, out var stroke))
                {
                    stroke.Path.Transform(scaleMatrix);
                    stroke.TransformMatrix = stroke.TransformMatrix.PostConcat(scaleMatrix);
                }
                else if (ctx.CanvasImages.TryGetValue(boundTargetId, out var image))
                {
                    ApplyScaleToImage(image, scaleMatrix);
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
            var scaleMatrix = BuildScaleMatrix(ev);
            foreach (var boundTargetId in bound.Targets)
            {
                if (ctx.PaintableStrokes.TryGetValue(boundTargetId, out var stroke))
                {
                    stroke.Path.Transform(scaleMatrix);
                    stroke.TransformMatrix = stroke.TransformMatrix.PostConcat(scaleMatrix);
                }
                else if (ctx.CanvasImages.TryGetValue(boundTargetId, out var image))
                {
                    ApplyScaleToImage(image, scaleMatrix);
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

    /// <summary>
    /// Builds a scale matrix that works correctly for rotated elements.
    /// For a rotated element the transform is:
    ///   T(-pivot) · R(-θ) · S(sx,sy) · R(θ) · T(pivot)
    /// This un-rotates around the pivot, scales along axis-aligned directions,
    /// then re-rotates keeping the pivot corner fixed in world space.
    /// When RotationRad is 0 this simplifies to a normal CreateScale(sx,sy,cx,cy).
    /// </summary>
    private static SKMatrix BuildScaleMatrix(ScaleCanvasElementsEvent ev)
    {
        if (Math.Abs(ev.RotationRad) <= 0.001f)
        {
            return SKMatrix.CreateScale(ev.Scale.X, ev.Scale.Y, ev.Center.X, ev.Center.Y);
        }

        var tToPivot = SKMatrix.CreateTranslation(-ev.Center.X, -ev.Center.Y);
        var unrotate = SKMatrix.CreateRotation(-ev.RotationRad);
        var scale = SKMatrix.CreateScale(ev.Scale.X, ev.Scale.Y);
        var rerotate = SKMatrix.CreateRotation(ev.RotationRad);
        var tBack = SKMatrix.CreateTranslation(ev.Center.X, ev.Center.Y);

        return tToPivot
            .PostConcat(unrotate)
            .PostConcat(scale)
            .PostConcat(rerotate)
            .PostConcat(tBack);
    }

    /// <summary>
    /// Applies a scale matrix to an image's axis-aligned bounds, handling axis
    /// inversion by swapping coordinates and toggling FlipX/FlipY.
    /// </summary>
    private static void ApplyScaleToImage(CanvasImage image, SKMatrix scaleMatrix)
    {
        var topLeft = scaleMatrix.MapPoint(new SKPoint(image.Bounds.Left, image.Bounds.Top));
        var bottomRight = scaleMatrix.MapPoint(new SKPoint(image.Bounds.Right, image.Bounds.Bottom));

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