using System;
using SkiaSharp;

namespace Scribble.State;

/// <summary>
/// Represents the current selection in the application
/// It encapsulates the data that represents an active selection
/// </summary>
public class Selection
{
    public SKPoint SelectionMoveCoord = SKPoint.Empty;
    public double SelectionRotationAngle = double.NaN;
    public SKRect SelectionBounds = SKRect.Empty;
    public SKPoint SelectionCenter = SKPoint.Empty;
    public SKPoint ScalePivot = SKPoint.Empty;
    public SKPoint ScalePrevCoord = SKPoint.Empty;
    public string? ActiveScaleHandle;

    // Rotation context captured at scale-start so pointer math stays
    // in the element's local (un-rotated) coordinate frame throughout the gesture.
    public float ScaleRotationRad;
    public SKPoint ScaleRotationCenter = SKPoint.Empty;
    public bool IsMultiElementScale;

    public Guid MoveActionId = Guid.NewGuid();
    public Guid RotateActionId = Guid.NewGuid();
    public Guid ScaleActionId = Guid.NewGuid();

    public void RefreshSelectionCenter()
    {
        SelectionCenter = new SKPoint(
            SelectionBounds.Left + SelectionBounds.Width / 2,
            SelectionBounds.Top + SelectionBounds.Height / 2);
    }

    public void UpdateSelectionRotationAngle(SKPoint referencePoint)
    {
        SelectionRotationAngle = Math.Atan2(referencePoint.Y - SelectionCenter.Y,
            referencePoint.X - SelectionCenter.X);
    }

    /// <summary>
    /// Sets the scale pivot to the opposite corner of the active handle.
    /// When the element is rotated, the pivot is rotated into world space so it
    /// matches the visual corner position the user sees on screen.
    /// </summary>
    public void RefreshScalePivot(float rotationRad, SKPoint rotationCenter, bool isMultiElement)
    {
        ScaleRotationRad = rotationRad;
        ScaleRotationCenter = rotationCenter;
        IsMultiElementScale = isMultiElement;

        // Pick the opposite corner from the axis-aligned (un-rotated) bounds
        var localPivot = ActiveScaleHandle switch
        {
            "ScaleHandleTl" => new SKPoint(SelectionBounds.Right, SelectionBounds.Bottom),
            "ScaleHandleTr" => new SKPoint(SelectionBounds.Left, SelectionBounds.Bottom),
            "ScaleHandleBl" => new SKPoint(SelectionBounds.Right, SelectionBounds.Top),
            "ScaleHandleBr" => new SKPoint(SelectionBounds.Left, SelectionBounds.Top),
            _ => ScalePivot
        };

        // Rotate the pivot into world space so it sits on the actual visual corner
        if (Math.Abs(rotationRad) > 0.001f)
        {
            var rotMatrix = SKMatrix.CreateRotation(rotationRad, rotationCenter.X, rotationCenter.Y);
            localPivot = rotMatrix.MapPoint(localPivot);
        }

        ScalePivot = localPivot;
    }

    public void ClearScaleState()
    {
        ActiveScaleHandle = null;
        ScalePivot = SKPoint.Empty;
        ScalePrevCoord = SKPoint.Empty;
        ScaleRotationRad = 0f;
        ScaleRotationCenter = SKPoint.Empty;
        IsMultiElementScale = false;
    }
}