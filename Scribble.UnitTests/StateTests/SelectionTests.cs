using FluentAssertions;
using Scribble.State;
using SkiaSharp;

namespace Scribble.UnitTests.StateTests;

public class SelectionTests
{
    // RefreshSelectionCenter
    [Fact]
    public void RefreshSelectionCenter_SquareBounds_CenterIsMiddleOfBounds()
    {
        var selection = new Selection
        {
            SelectionBounds = SKRect.Create(0f, 0f, 100f, 100f)
        };

        selection.RefreshSelectionCenter();

        selection.SelectionCenter.Should().Be(new SKPoint(50f, 50f));
    }

    [Fact]
    public void RefreshSelectionCenter_RectangularBounds_CenterIsMiddleOfBounds()
    {
        var selection = new Selection
        {
            SelectionBounds = SKRect.Create(10f, 20f, 80f, 40f)
        };

        selection.RefreshSelectionCenter();

        selection.SelectionCenter.Should().Be(new SKPoint(50f, 40f));
    }

    [Fact]
    public void RefreshSelectionCenter_BoundsNotAtOrigin_CenterAccountsForOffset()
    {
        var selection = new Selection
        {
            SelectionBounds = SKRect.Create(100f, 200f, 50f, 50f)
        };

        selection.RefreshSelectionCenter();

        selection.SelectionCenter.Should().Be(new SKPoint(125f, 225f));
    }

    // UpdateSelectionRotationAngle
    [Fact]
    public void UpdateSelectionRotationAngle_ReferencePointDirectlyRight_AngleIsZero()
    {
        var selection = new Selection
        {
            SelectionCenter = new SKPoint(0f, 0f)
        };

        selection.UpdateSelectionRotationAngle(new SKPoint(1f, 0f));

        selection.SelectionRotationAngle.Should().BeApproximately(0.0, 1e-10);
    }

    [Fact]
    public void UpdateSelectionRotationAngle_ReferencePointDirectlyAbove_AngleIsNegativeHalfPi()
    {
        // In screen space Y increases downward, so a point directly above the
        // center (lower Y) produces a negative Atan2 result.
        var selection = new Selection
        {
            SelectionCenter = new SKPoint(0f, 0f)
        };

        selection.UpdateSelectionRotationAngle(new SKPoint(0f, -1f));

        selection.SelectionRotationAngle.Should().BeApproximately(-Math.PI / 2, 1e-10);
    }

    [Fact]
    public void UpdateSelectionRotationAngle_ReferencePointDirectlyBelow_AngleIsPositiveHalfPi()
    {
        var selection = new Selection
        {
            SelectionCenter = new SKPoint(0f, 0f)
        };

        selection.UpdateSelectionRotationAngle(new SKPoint(0f, 1f));

        selection.SelectionRotationAngle.Should().BeApproximately(Math.PI / 2, 1e-10);
    }

    [Fact]
    public void UpdateSelectionRotationAngle_ReferencePointIsCenter_AngleIsZero()
    {
        // Atan2(0, 0) is defined as 0 in .NET
        var selection = new Selection
        {
            SelectionCenter = new SKPoint(50f, 50f)
        };

        selection.UpdateSelectionRotationAngle(new SKPoint(50f, 50f));

        selection.SelectionRotationAngle.Should().BeApproximately(0.0, 1e-10);
    }

    [Fact]
    public void UpdateSelectionRotationAngle_UsesSelectionCenterAsOrigin()
    {
        // Shift both center and reference by the same amount — angle must be identical
        var selection = new Selection
        {
            SelectionCenter = new SKPoint(100f, 100f)
        };

        selection.UpdateSelectionRotationAngle(new SKPoint(101f, 100f));

        selection.SelectionRotationAngle.Should().BeApproximately(0.0, 1e-10);
    }

    // RefreshScalePivot
    [Fact]
    public void RefreshScalePivot_TopLeftHandle_NoRotation_PivotIsBottomRight()
    {
        var selection = new Selection
        {
            SelectionBounds = SKRect.Create(0f, 0f, 100f, 80f),
            ActiveScaleHandle = "ScaleHandleTl"
        };

        selection.RefreshScalePivot(0f, new SKPoint(50f, 40f), false);

        selection.ScalePivot.Should().Be(new SKPoint(100f, 80f));
    }

    [Fact]
    public void RefreshScalePivot_TopRightHandle_NoRotation_PivotIsBottomLeft()
    {
        var selection = new Selection
        {
            SelectionBounds = SKRect.Create(0f, 0f, 100f, 80f),
            ActiveScaleHandle = "ScaleHandleTr"
        };

        selection.RefreshScalePivot(0f, new SKPoint(50f, 40f), false);

        selection.ScalePivot.Should().Be(new SKPoint(0f, 80f));
    }

    [Fact]
    public void RefreshScalePivot_BottomLeftHandle_NoRotation_PivotIsTopRight()
    {
        var selection = new Selection
        {
            SelectionBounds = SKRect.Create(0f, 0f, 100f, 80f),
            ActiveScaleHandle = "ScaleHandleBl"
        };

        selection.RefreshScalePivot(0f, new SKPoint(50f, 40f), false);

        selection.ScalePivot.Should().Be(new SKPoint(100f, 0f));
    }

    [Fact]
    public void RefreshScalePivot_BottomRightHandle_NoRotation_PivotIsTopLeft()
    {
        var selection = new Selection
        {
            SelectionBounds = SKRect.Create(0f, 0f, 100f, 80f),
            ActiveScaleHandle = "ScaleHandleBr"
        };

        selection.RefreshScalePivot(0f, new SKPoint(50f, 40f), false);

        selection.ScalePivot.Should().Be(new SKPoint(0f, 0f));
    }

    [Fact]
    public void RefreshScalePivot_UnknownHandle_PivotIsNotChanged()
    {
        var originalPivot = new SKPoint(99f, 99f);
        var selection = new Selection
        {
            SelectionBounds = SKRect.Create(0f, 0f, 100f, 80f),
            ActiveScaleHandle = "UnknownHandle",
            ScalePivot = originalPivot
        };

        selection.RefreshScalePivot(0f, new SKPoint(50f, 40f), false);

        selection.ScalePivot.Should().Be(originalPivot);
    }

    [Fact]
    public void RefreshScalePivot_NullHandle_PivotIsNotChanged()
    {
        var originalPivot = new SKPoint(99f, 99f);
        var selection = new Selection
        {
            SelectionBounds = SKRect.Create(0f, 0f, 100f, 80f),
            ActiveScaleHandle = null,
            ScalePivot = originalPivot
        };

        selection.RefreshScalePivot(0f, new SKPoint(50f, 40f), false);

        selection.ScalePivot.Should().Be(originalPivot);
    }

    [Fact]
    public void RefreshScalePivot_90DegRotation_PivotIsRotatedOppositeCorner()
    {
        var selection = new Selection
        {
            SelectionBounds = SKRect.Create(0f, 0f, 100f, 100f),
            ActiveScaleHandle = "ScaleHandleTl"
        };

        const float rotRad = (float)(Math.PI / 2); // 90deg clockwise
        selection.RefreshScalePivot(rotRad, new SKPoint(50f, 50f), false);

        selection.ScalePivot.X.Should().BeApproximately(0f, 1f);
        selection.ScalePivot.Y.Should().BeApproximately(100f, 1f);
    }

    [Fact]
    public void RefreshScalePivot_45DegRotation_StoresRotationContext()
    {
        var selection = new Selection
        {
            SelectionBounds = SKRect.Create(0f, 0f, 100f, 100f),
            ActiveScaleHandle = "ScaleHandleBr"
        };

        var rotRad = (float)(Math.PI / 4);
        var center = new SKPoint(50f, 50f);
        selection.RefreshScalePivot(rotRad, center, true);

        selection.ScaleRotationRad.Should().BeApproximately(rotRad, 1e-6f);
        selection.ScaleRotationCenter.Should().Be(center);
        selection.MaintainAspectRatio.Should().BeTrue();
    }

    // ClearScaleState
    [Fact]
    public void ClearScaleState_ResetsAllScaleFields()
    {
        var selection = new Selection
        {
            ActiveScaleHandle = "ScaleHandleTl",
            ScalePivot = new SKPoint(10, 20),
            ScalePrevCoord = new SKPoint(30, 40),
            ScaleRotationRad = 1.5f,
            ScaleRotationCenter = new SKPoint(50, 50),
            MaintainAspectRatio = true
        };

        selection.ClearScaleState();

        selection.ActiveScaleHandle.Should().BeNull();
        selection.ScalePivot.Should().Be(SKPoint.Empty);
        selection.ScalePrevCoord.Should().Be(SKPoint.Empty);
        selection.ScaleRotationRad.Should().Be(0f);
        selection.ScaleRotationCenter.Should().Be(SKPoint.Empty);
        selection.MaintainAspectRatio.Should().BeFalse();
    }
}