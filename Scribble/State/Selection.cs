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

    // Determine pivot based on the handle (opposite corner)
    // SelectionBounds contains the current bounds in Canvas coordinates
    public void RefreshScalePivot()
    {
        switch (ActiveScaleHandle)
        {
            case "ScaleHandleTl":
                ScalePivot =
                    new SKPoint(SelectionBounds.Right, SelectionBounds.Bottom);
                break;
            case "ScaleHandleTr":
                ScalePivot =
                    new SKPoint(SelectionBounds.Left, SelectionBounds.Bottom);
                break;
            case "ScaleHandleBl":
                ScalePivot = new SKPoint(SelectionBounds.Right, SelectionBounds.Top);
                break;
            case "ScaleHandleBr":
                ScalePivot = new SKPoint(SelectionBounds.Left, SelectionBounds.Top);
                break;
        }
    }
}