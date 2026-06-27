using FluentAssertions;
using Scribble.State;
using SkiaSharp;

namespace Scribble.Tests.StateTests;

public class CameraStateTests
{
    public CameraStateTests()
    {
        // Reset camera state before each test
        CameraState.Reset();
    }

    // SetZoom
    [Fact]
    public void SetZoom_ValueWithinRange_ZoomIsUpdated()
    {
        const float validZoom = 2.5f;
        CameraState.SetZoom(validZoom);

        CameraState.Zoom.Should().Be(validZoom);
    }

    [Fact]
    public void SetZoom_ValueBelowRange_ZoomIsClampedToMinimum()
    {
        const float invalidZoom = 0.01f;
        CameraState.SetZoom(invalidZoom);

        CameraState.Zoom.Should().Be(CameraState.MinZoom);
    }

    [Fact]
    public void SetZoom_ValueAboveRange_ZoomIsClampedToMaximum()
    {
        const float invalidZoom = 100f;
        CameraState.SetZoom(invalidZoom);

        CameraState.Zoom.Should().Be(CameraState.MaxZoom);
    }

    [Fact]
    public void Reset_ResetsZoomToDefault()
    {
        CameraState.SetZoom(2.5f);
        CameraState.Reset();

        const float defaultZoom = 1.0f;
        CameraState.Zoom.Should().Be(defaultZoom);
    }

    // ScreenToWorld
    [Fact]
    public void ScreenToWorld_NoZoomNoOffset_ScreenPosShouldEqualWorldPos()
    {
        var screenPos = new SKPoint(100, 100);

        CameraState.ScreenToWorld(screenPos).Should().Be(screenPos);
    }

    [Fact]
    public void ScreenToWorld_2xZoomNoOffset_WorldPosShouldBeHalved()
    {
        CameraState.SetZoom(2);
        var screenPos = new SKPoint(100, 100);
        CameraState.ScreenToWorld(screenPos).Should().Be(new SKPoint(screenPos.X / 2, screenPos.Y / 2));
    }

    [Fact]
    public void ScreenToWorld_WithOffset_WorldPosShouldBeOffset()
    {
        var offset = new SKPoint(50, 75);
        CameraState.WorldOffSetX = offset.X;
        CameraState.WorldOffSetY = offset.Y;
        var screenPos = new SKPoint(100, 100);

        CameraState.ScreenToWorld(screenPos).Should().Be(screenPos + offset);
    }

    // WorldToScreen
    [Fact]
    public void WorldToScreen_NoZoomNoOffset_WorldPosShouldEqualScreenPos()
    {
        var worldPos = new SKPoint(100, 100);

        CameraState.WorldToScreen(worldPos).Should().Be(worldPos);
    }

    [Fact]
    public void WorldToScreen_2xZoomNoOffset_ScreenPosShouldBeDoubled()
    {
        CameraState.SetZoom(2);
        var worldPos = new SKPoint(100, 100);

        CameraState.WorldToScreen(worldPos).Should().Be(new SKPoint(worldPos.X * 2, worldPos.Y * 2));
    }

    [Fact]
    public void WorldToScreen_WithOffset_ScreenPosShouldBeShiftedByOffset()
    {
        var offset = new SKPoint(50, 75);
        CameraState.WorldOffSetX = offset.X;
        CameraState.WorldOffSetY = offset.Y;
        var worldPos = new SKPoint(100, 100);

        CameraState.WorldToScreen(worldPos).Should().Be(worldPos - offset);
    }

    // GetViewMatrix
    [Fact]
    public void GetViewMatrix_DefaultState_ReturnsIdentityScaleAndZeroTranslation()
    {
        var matrix = CameraState.GetViewMatrix();

        matrix.ScaleX.Should().Be(1f);
        matrix.ScaleY.Should().Be(1f);
        matrix.TransX.Should().Be(0f);
        matrix.TransY.Should().Be(0f);
    }

    [Fact]
    public void GetViewMatrix_2xZoomNoOffset_ScaleComponentsAreDoubled()
    {
        CameraState.SetZoom(2f);

        var matrix = CameraState.GetViewMatrix();

        matrix.ScaleX.Should().Be(2f);
        matrix.ScaleY.Should().Be(2f);
    }

    [Fact]
    public void GetViewMatrix_2xZoomNoOffset_TranslationRemainsZero()
    {
        CameraState.SetZoom(2f);

        var matrix = CameraState.GetViewMatrix();

        matrix.TransX.Should().Be(0f);
        matrix.TransY.Should().Be(0f);
    }

    [Fact]
    public void GetViewMatrix_WithOffsetNoZoom_ScaleRemainsIdentity()
    {
        CameraState.WorldOffSetX = 50f;
        CameraState.WorldOffSetY = 75f;

        var matrix = CameraState.GetViewMatrix();

        matrix.ScaleX.Should().Be(1f);
        matrix.ScaleY.Should().Be(1f);
    }

    [Fact]
    public void GetViewMatrix_WithOffsetNoZoom_TranslationIsNegatedOffset()
    {
        CameraState.WorldOffSetX = 50f;
        CameraState.WorldOffSetY = 75f;

        var matrix = CameraState.GetViewMatrix();

        matrix.TransX.Should().Be(-50f);
        matrix.TransY.Should().Be(-75f);
    }

    [Fact]
    public void GetViewMatrix_2xZoomWithOffset_TranslationIsNegatedAndOffsetScaledByZoom()
    {
        CameraState.SetZoom(2f);
        CameraState.WorldOffSetX = 50f;
        CameraState.WorldOffSetY = 75f;

        var matrix = CameraState.GetViewMatrix();

        matrix.TransX.Should().Be(-100f);
        matrix.TransY.Should().Be(-150f);
    }
}