using FluentAssertions;
using Scribble.Shared.Lib;
using SkiaSharp;

namespace Scribble.Tests.Lib;

public class StrokePaintTests
{
    [Fact]
    public void ToSkPaint_ReturnsEquivalentSkPaint()
    {
        var strokePaint = new StrokePaint
        {
            StrokeCap = SKStrokeCap.Butt,
            StrokeJoin = SKStrokeJoin.Round,
            StrokeWidth = 4f,
            IsAntialias = false,
            Color = SKColors.Aqua
        };

        var skPaint = strokePaint.ToSkPaint();

        skPaint.StrokeCap.Should().Be(strokePaint.StrokeCap);
        skPaint.StrokeJoin.Should().Be(strokePaint.StrokeJoin);
        skPaint.StrokeWidth.Should().Be(strokePaint.StrokeWidth);
        skPaint.IsAntialias.Should().Be(strokePaint.IsAntialias);
        skPaint.Color.Should().Be(strokePaint.Color);
    }

    [Fact]
    public void ToSkPaint_SkPaintHasDashIntervals_ReturnsSkPaintWithNonNullPathEffect()
    {
        var strokePaint = new StrokePaint
        {
            StrokeCap = SKStrokeCap.Butt,
            StrokeJoin = SKStrokeJoin.Round,
            StrokeWidth = 4f,
            IsAntialias = false,
            Color = SKColors.Aqua,
            DashIntervals = [1f, 2f, 3f, 4f]
        };

        var skPaint = strokePaint.ToSkPaint();

        skPaint.PathEffect.Should().NotBeNull();
    }

    [Fact]
    public void GetCachedSkPaint_ReturnsSameCachedInstanceOnRepeatedCalls()
    {
        var strokePaint = new StrokePaint
        {
            StrokeCap = SKStrokeCap.Butt,
            StrokeJoin = SKStrokeJoin.Round,
            StrokeWidth = 4f,
            IsAntialias = false,
            Color = SKColors.Aqua
        };

        var actual = strokePaint.GetCachedSkPaint();

        var expected = strokePaint.GetCachedSkPaint();
        actual.Should().BeSameAs(expected);
    }

    [Fact]
    public void Clone_ReturnsDistinctInstance()
    {
        var strokePaint = new StrokePaint();

        var clone = strokePaint.Clone();

        clone.Should().NotBeSameAs(strokePaint);
    }

    [Fact]
    public void Clone_CopiesAllProperties()
    {
        var strokePaint = new StrokePaint
        {
            IsAntialias = false,
            IsStroke = false,
            StrokeCap = SKStrokeCap.Square,
            StrokeJoin = SKStrokeJoin.Bevel,
            StrokeWidth = 8f,
            TextSize = 24f,
            Color = SKColors.Blue,
            FillColor = SKColors.Green,
            DashIntervals = [1f, 2f]
        };

        var clone = strokePaint.Clone();

        clone.IsAntialias.Should().Be(strokePaint.IsAntialias);
        clone.IsStroke.Should().Be(strokePaint.IsStroke);
        clone.StrokeCap.Should().Be(strokePaint.StrokeCap);
        clone.StrokeJoin.Should().Be(strokePaint.StrokeJoin);
        clone.StrokeWidth.Should().Be(strokePaint.StrokeWidth);
        clone.TextSize.Should().Be(strokePaint.TextSize);
        clone.Color.Should().Be(strokePaint.Color);
        clone.FillColor.Should().Be(strokePaint.FillColor);
    }

    [Fact]
    public void Clone_DashIntervals_IsDeepCopied()
    {
        var strokePaint = new StrokePaint
        {
            DashIntervals = [1f, 2f, 3f, 4f]
        };

        var clone = strokePaint.Clone();

        clone.DashIntervals.Should().NotBeSameAs(strokePaint.DashIntervals);
        clone.DashIntervals.Should().BeEquivalentTo(strokePaint.DashIntervals);
    }

    [Fact]
    public void Clone_MutatingDashIntervals_DoesNotAffectOriginal()
    {
        var strokePaint = new StrokePaint
        {
            DashIntervals = [1f, 2f, 3f, 4f]
        };

        var clone = strokePaint.Clone();
        clone.DashIntervals![0] = 99f;

        strokePaint.DashIntervals![0].Should().Be(1f);
    }
}