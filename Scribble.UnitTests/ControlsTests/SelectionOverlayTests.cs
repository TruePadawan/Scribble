using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using FluentAssertions;
using Scribble.Controls;
using SkiaSharp;

namespace Scribble.UnitTests.ControlsTests;

/// <summary>
/// Verify the selection view owns the viewport geometry it displays.
/// </summary>
public class SelectionOverlayTests
{
    // Screen-space selection bounds determine the control's size and position.
    [AvaloniaFact]
    public void SetSelectionGeometry_SetsScreenBoundsAndVisibility()
    {
        var overlay = new SelectionOverlay();

        overlay.SetSelectionGeometry(SKRect.Create(12, 24, 100, 60), 0, SKPoint.Empty, true);

        overlay.IsVisible.Should().BeTrue();
        overlay.Width.Should().Be(100);
        overlay.Height.Should().Be(81);
        Canvas.GetLeft(overlay).Should().Be(12);
        Canvas.GetTop(overlay).Should().Be(3);
    }
}
