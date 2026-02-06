using System;
using Avalonia;
using SkiaSharp;

namespace Scribble.Lib;

public class Selection
{
    public Point SelectionMoveCoord = new Point(-1, -1);
    public double SelectionRotationAngle = double.NaN;
    public SKRect SelectionBounds = SKRect.Empty;
    public Point SelectionCenter = new Point(-1, -1);
    public Point ScalePivot = new Point(-1, -1);
    public Point ScalePrevCoord = new Point(-1, -1);
    public string? ActiveScaleHandle;

    public Guid MoveActionId = Guid.NewGuid();
    public Guid RotateActionId = Guid.NewGuid();
    public Guid ScaleActionId = Guid.NewGuid();

    public void RefreshSelectionCenter()
    {
        SelectionCenter = new Point(
            SelectionBounds.Left + SelectionBounds.Width / 2,
            SelectionBounds.Top + SelectionBounds.Height / 2);
    }

    public void UpdateSelectionRotationAngle(Point referencePoint)
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
                    new Point(SelectionBounds.Right, SelectionBounds.Bottom);
                break;
            case "ScaleHandleTr":
                ScalePivot =
                    new Point(SelectionBounds.Left, SelectionBounds.Bottom);
                break;
            case "ScaleHandleBl":
                ScalePivot = new Point(SelectionBounds.Right, SelectionBounds.Top);
                break;
            case "ScaleHandleBr":
                ScalePivot = new Point(SelectionBounds.Left, SelectionBounds.Top);
                break;
        }
    }
}