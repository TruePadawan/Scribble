using Avalonia;
using Avalonia.Media;
using FluentAssertions;
using Scribble.Utils;
using SkiaSharp;

namespace Scribble.UnitTests.UtilsTests;

public class UtilitiesTests
{
    // ToSkColor
    [Fact]
    public void ToSkColor_OpaqueColor_ReturnsEquivalentSkColor()
    {
        var brown = Colors.Brown;

        var actual = Utilities.ToSkColor(brown);

        actual.Should().Be(SKColors.Brown);
    }

    [Fact]
    public void ToSkColor_SlightlyTransparentColor_ReturnsEquivalentSkColor()
    {
        var brown = Colors.Brown;
        var slightlyTransparentBrown = Color.FromArgb(100, brown.R, brown.G, brown.B);

        var actual = Utilities.ToSkColor(slightlyTransparentBrown);

        var expected = SKColors.Brown.WithAlpha(100);
        actual.Should().Be(expected);
    }

    [Fact]
    public void ToSkColor_FullyTransparentColor_ReturnsTransparentSkColor()
    {
        var brown = Colors.Brown;
        var transparentBrown = Color.FromArgb(0, brown.R, brown.G, brown.B);

        var actual = Utilities.ToSkColor(transparentBrown);

        var expected = SKColors.Brown.WithAlpha(0);
        actual.Should().Be(expected);
    }

    // FromSkColor
    [Fact]
    public void FromSkColor_OpaqueSkColor_ReturnsEquivalentColor()
    {
        var skBrown = SKColors.Brown;

        var actual = Utilities.FromSkColor(skBrown);

        actual.Should().Be(Colors.Brown);
    }

    [Fact]
    public void FromSkColor_SlightlyTransparentSkColor_ReturnsEquivalentColor()
    {
        var skBrown = SKColors.Brown.WithAlpha(100);

        var actual = Utilities.FromSkColor(skBrown);

        var brown = Colors.Brown;
        var expected = Color.FromArgb(100, brown.R, brown.G, brown.B);
        actual.Should().Be(expected);
    }

    [Fact]
    public void FromSkColor_FullyTransparentSkColor_ReturnsEquivalentColor()
    {
        var skBrown = SKColors.Brown.WithAlpha(0);

        var actual = Utilities.FromSkColor(skBrown);

        var brown = Colors.Brown;
        var expected = Color.FromArgb(0, brown.R, brown.G, brown.B);
        actual.Should().Be(expected);
    }

    // GetSize
    [Fact]
    public void GetSize_TwoDistinctPoints_ReturnsAbsoluteDimensions()
    {
        var start = new SKPoint(10f, 20f);
        var end = new SKPoint(40f, 60f);

        var actual = Utilities.GetSize(start, end);

        actual.Should().Be(new SKSize(30f, 40f));
    }

    [Fact]
    public void GetSize_EndPointIsTopLeft_ReturnsPositiveDimensions()
    {
        // end is "before" start, width/height should still be positive
        var start = new SKPoint(40f, 60f);
        var end = new SKPoint(10f, 20f);

        var actual = Utilities.GetSize(start, end);

        actual.Should().Be(new SKSize(30f, 40f));
    }

    [Fact]
    public void GetSize_SamePoint_ReturnsZeroSize()
    {
        var point = new SKPoint(15f, 25f);

        var actual = Utilities.GetSize(point, point);

        actual.Should().Be(new SKSize(0f, 0f));
    }

    // ToSkPoint
    [Fact]
    public void ToSkPoint_PositiveCoordinates_ReturnsEquivalentSkPoint()
    {
        var point = new Point(123.5, 456.75);

        var actual = Utilities.ToSkPoint(point);

        actual.Should().Be(new SKPoint(123.5f, 456.75f));
    }

    [Fact]
    public void ToSkPoint_Origin_ReturnsZeroSkPoint()
    {
        var actual = Utilities.ToSkPoint(new Point(0, 0));

        actual.Should().Be(new SKPoint(0f, 0f));
    }

    // FromSkPoint
    [Fact]
    public void FromSkPoint_PositiveCoordinates_ReturnsEquivalentPoint()
    {
        var skPoint = new SKPoint(123.5f, 456.75f);

        var actual = Utilities.FromSkPoint(skPoint);

        actual.Should().Be(new Point(123.5f, 456.75f));
    }

    [Fact]
    public void FromSkPoint_Origin_ReturnsZeroPoint()
    {
        var actual = Utilities.FromSkPoint(new SKPoint(0f, 0f));

        actual.Should().Be(new Point(0, 0));
    }

    // AreSamePosition
    [Fact]
    public void AreSamePosition_IdenticalPoints_ReturnsTrue()
    {
        var point = new SKPoint(50f, 50f);

        var actual = Utilities.AreSamePosition(point, point);

        actual.Should().BeTrue();
    }

    [Fact]
    public void AreSamePosition_PointsWithinDefaultEpsilon_ReturnsTrue()
    {
        var a = new SKPoint(50f, 50f);
        var b = new SKPoint(50.4f, 50.4f); // difference < 0.5 on both axes

        var actual = Utilities.AreSamePosition(a, b);

        actual.Should().BeTrue();
    }

    [Fact]
    public void AreSamePosition_PointsOutsideDefaultEpsilon_ReturnsFalse()
    {
        var a = new SKPoint(50f, 50f);
        var b = new SKPoint(50.6f, 50f); // X difference >= 0.5

        var actual = Utilities.AreSamePosition(a, b);

        actual.Should().BeFalse();
    }

    [Fact]
    public void AreSamePosition_PointsWithinCustomEpsilon_ReturnsTrue()
    {
        var a = new SKPoint(10f, 10f);
        var b = new SKPoint(11.9f, 11.9f);

        var actual = Utilities.AreSamePosition(a, b, epsilon: 2.0);

        actual.Should().BeTrue();
    }

    [Fact]
    public void AreSamePosition_PointsOutsideCustomEpsilon_ReturnsFalse()
    {
        var a = new SKPoint(10f, 10f);
        var b = new SKPoint(12.1f, 10f);

        var actual = Utilities.AreSamePosition(a, b, epsilon: 2.0);

        actual.Should().BeFalse();
    }

    // IsPointNearLine
    [Fact]
    public void IsPointNearLine_PointOnSegment_ReturnsTrue()
    {
        var start = new SKPoint(0f, 0f);
        var end = new SKPoint(10f, 0f);
        var point = new SKPoint(5f, 0f); // exactly on the midpoint

        var actual = Utilities.IsPointNearLine(point, [start, end], tolerance: 1f);

        actual.Should().BeTrue();
    }

    [Fact]
    public void IsPointNearLine_PointWithinTolerance_ReturnsTrue()
    {
        var start = new SKPoint(0f, 0f);
        var end = new SKPoint(10f, 0f);
        var point = new SKPoint(5f, 0.8f); // 0.8 units above midpoint, tolerance is 1

        var actual = Utilities.IsPointNearLine(point, [start, end], tolerance: 1f);

        actual.Should().BeTrue();
    }

    [Fact]
    public void IsPointNearLine_PointOutsideTolerance_ReturnsFalse()
    {
        var start = new SKPoint(0f, 0f);
        var end = new SKPoint(10f, 0f);
        var point = new SKPoint(5f, 5f); // 5 units above, far outside tolerance

        var actual = Utilities.IsPointNearLine(point, [start, end], tolerance: 1f);

        actual.Should().BeFalse();
    }

    [Fact]
    public void IsPointNearLine_PointBeyondEndpoint_UsesClosestEndpoint()
    {
        // Projection of point falls outside the segment; nearest point is the endpoint
        var start = new SKPoint(0f, 0f);
        var end = new SKPoint(10f, 0f);
        var point = new SKPoint(12f, 0f); // 2 units past the end

        var actual = Utilities.IsPointNearLine(point, [start, end], tolerance: 3f);

        actual.Should().BeTrue();
    }

    [Fact]
    public void IsPointNearLine_ZeroLengthLine_TreatsAsPoint()
    {
        var start = new SKPoint(5f, 5f);
        var point = new SKPoint(5f, 5.5f); // 0.5 units from the start point

        var actual = Utilities.IsPointNearLine(point, [start, start], tolerance: 1f);

        actual.Should().BeTrue();
    }

    [Fact]
    public void IsPointNearLine_ZeroLengthLine_PointTooFar_ReturnsFalse()
    {
        var start = new SKPoint(5f, 5f);
        var point = new SKPoint(5f, 10f); // 5 units away

        var actual = Utilities.IsPointNearLine(point, [start, start], tolerance: 1f);

        actual.Should().BeFalse();
    }
}