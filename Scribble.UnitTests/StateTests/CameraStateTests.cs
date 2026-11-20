using FluentAssertions;
using Scribble.State;
using SkiaSharp;

namespace Scribble.UnitTests.StateTests;

public class CameraStateTests
{
    private readonly CameraState _camera = new();

    // Each viewport keeps an independent transform.
    [Fact]
    public void CameraState_TwoInstances_KeepIndependentZoomAndOffset()
    {
        var other = new CameraState();
        _camera.SetZoom(2);
        _camera.WorldOffSetX = 40;

        other.Zoom.Should().Be(1);
        other.WorldOffSetX.Should().Be(0);
    }

    // SetZoom
    [Fact]
    public void SetZoom_ValueWithinRange_ZoomIsUpdated()
    {
        const float validZoom = 2.5f;
        _camera.SetZoom(validZoom);

        _camera.Zoom.Should().Be(validZoom);
    }

    [Fact]
    public void SetZoom_ValueBelowRange_ZoomIsClampedToMinimum()
    {
        const float invalidZoom = 0.01f;
        _camera.SetZoom(invalidZoom);

        _camera.Zoom.Should().Be(CameraState.MinZoom);
    }

    [Fact]
    public void SetZoom_ValueAboveRange_ZoomIsClampedToMaximum()
    {
        const float invalidZoom = 100f;
        _camera.SetZoom(invalidZoom);

        _camera.Zoom.Should().Be(CameraState.MaxZoom);
    }

    [Fact]
    public void Reset_ResetsZoomToDefault()
    {
        _camera.SetZoom(2.5f);
        _camera.Reset();

        const float defaultZoom = 1.0f;
        _camera.Zoom.Should().Be(defaultZoom);
    }

    // ScreenToWorld
    [Fact]
    public void ScreenToWorld_NoZoomNoOffset_ScreenPosShouldEqualWorldPos()
    {
        var screenPos = new SKPoint(100, 100);

        _camera.ScreenToWorld(screenPos).Should().Be(screenPos);
    }

    [Fact]
    public void ScreenToWorld_2xZoomNoOffset_WorldPosShouldBeHalved()
    {
        _camera.SetZoom(2);
        var screenPos = new SKPoint(100, 100);
        _camera.ScreenToWorld(screenPos).Should().Be(new SKPoint(screenPos.X / 2, screenPos.Y / 2));
    }

    [Fact]
    public void ScreenToWorld_WithOffset_WorldPosShouldBeOffset()
    {
        var offset = new SKPoint(50, 75);
        _camera.WorldOffSetX = offset.X;
        _camera.WorldOffSetY = offset.Y;
        var screenPos = new SKPoint(100, 100);

        _camera.ScreenToWorld(screenPos).Should().Be(screenPos + offset);
    }

    // WorldToScreen
    [Fact]
    public void WorldToScreen_NoZoomNoOffset_WorldPosShouldEqualScreenPos()
    {
        var worldPos = new SKPoint(100, 100);

        _camera.WorldToScreen(worldPos).Should().Be(worldPos);
    }

    [Fact]
    public void WorldToScreen_2xZoomNoOffset_ScreenPosShouldBeDoubled()
    {
        _camera.SetZoom(2);
        var worldPos = new SKPoint(100, 100);

        _camera.WorldToScreen(worldPos).Should().Be(new SKPoint(worldPos.X * 2, worldPos.Y * 2));
    }

    [Fact]
    public void WorldToScreen_WithOffset_ScreenPosShouldBeShiftedByOffset()
    {
        var offset = new SKPoint(50, 75);
        _camera.WorldOffSetX = offset.X;
        _camera.WorldOffSetY = offset.Y;
        var worldPos = new SKPoint(100, 100);

        _camera.WorldToScreen(worldPos).Should().Be(worldPos - offset);
    }

    // GetViewMatrix
    [Fact]
    public void GetViewMatrix_DefaultState_ReturnsIdentityScaleAndZeroTranslation()
    {
        var matrix = _camera.GetViewMatrix();

        matrix.ScaleX.Should().Be(1f);
        matrix.ScaleY.Should().Be(1f);
        matrix.TransX.Should().Be(0f);
        matrix.TransY.Should().Be(0f);
    }

    [Fact]
    public void GetViewMatrix_2xZoomNoOffset_ScaleComponentsAreDoubled()
    {
        _camera.SetZoom(2f);

        var matrix = _camera.GetViewMatrix();

        matrix.ScaleX.Should().Be(2f);
        matrix.ScaleY.Should().Be(2f);
    }

    [Fact]
    public void GetViewMatrix_2xZoomNoOffset_TranslationRemainsZero()
    {
        _camera.SetZoom(2f);

        var matrix = _camera.GetViewMatrix();

        matrix.TransX.Should().Be(0f);
        matrix.TransY.Should().Be(0f);
    }

    [Fact]
    public void GetViewMatrix_WithOffsetNoZoom_ScaleRemainsIdentity()
    {
        _camera.WorldOffSetX = 50f;
        _camera.WorldOffSetY = 75f;

        var matrix = _camera.GetViewMatrix();

        matrix.ScaleX.Should().Be(1f);
        matrix.ScaleY.Should().Be(1f);
    }

    [Fact]
    public void GetViewMatrix_WithOffsetNoZoom_TranslationIsNegatedOffset()
    {
        _camera.WorldOffSetX = 50f;
        _camera.WorldOffSetY = 75f;

        var matrix = _camera.GetViewMatrix();

        matrix.TransX.Should().Be(-50f);
        matrix.TransY.Should().Be(-75f);
    }

    [Fact]
    public void GetViewMatrix_2xZoomWithOffset_TranslationIsNegatedAndOffsetScaledByZoom()
    {
        _camera.SetZoom(2f);
        _camera.WorldOffSetX = 50f;
        _camera.WorldOffSetY = 75f;

        var matrix = _camera.GetViewMatrix();

        matrix.TransX.Should().Be(-100f);
        matrix.TransY.Should().Be(-150f);
    }
}