using FluentAssertions;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using Scribble.Utils;
using SkiaSharp;

namespace Scribble.Tests.Utils;

public class FreehandPathBuilderTests
{
    // helpers
    private static StrokePoint Pt(float x, float y) =>
        new StrokePoint { Point = new SKPoint(x, y) };

    /// <summary>Iterates the path and returns every (verb, points) pair.</summary>
    private static List<(SKPathVerb Verb, SKPoint[] Points)> GetVerbs(SKPath path)
    {
        var result = new List<(SKPathVerb, SKPoint[])>();
        using var iter = path.CreateIterator(false);
        var points = new SKPoint[4];
        SKPathVerb verb;
        while ((verb = iter.Next(points)) != SKPathVerb.Done)
        {
            result.Add((verb, (SKPoint[])points.Clone()));
        }

        return result;
    }

    // 0 points
    [Fact]
    public void Build_EmptyPoints_ReturnsEmptyPath()
    {
        var path = FreehandPathBuilder.Build([]);

        path.IsEmpty.Should().BeTrue();
    }

    // 1 point
    [Fact]
    public void Build_SinglePoint_ReturnsPathWithSingleRecordedPoint()
    {
        var path = FreehandPathBuilder.Build([Pt(2f, 2f)]);

        path.PointCount.Should().Be(1);
    }

    [Fact]
    public void Build_SinglePoint_MoveToIsAtCorrectPosition()
    {
        var point = new SKPoint(2f, 2f);
        var path = FreehandPathBuilder.Build([new StrokePoint { Point = point }]);

        path.GetPoint(0).Should().Be(point);
    }

    // 2 points
    [Fact]
    public void Build_TwoPoints_ProducesMoveThenLine()
    {
        var path = FreehandPathBuilder.Build([Pt(0f, 0f), Pt(10f, 10f)]);

        var verbs = GetVerbs(path);
        verbs.Should().HaveCount(2);
        verbs[0].Verb.Should().Be(SKPathVerb.Move);
        verbs[1].Verb.Should().Be(SKPathVerb.Line);
    }

    [Fact]
    public void Build_TwoPoints_LineStartsAtFirstPoint()
    {
        var path = FreehandPathBuilder.Build([Pt(0f, 0f), Pt(10f, 10f)]);

        path.GetPoint(0).Should().Be(new SKPoint(0f, 0f));
    }

    [Fact]
    public void Build_TwoPoints_LineEndsAtSecondPoint()
    {
        var path = FreehandPathBuilder.Build([Pt(0f, 0f), Pt(10f, 10f)]);

        var verbs = GetVerbs(path);
        var lineVerb = verbs.Single(v => v.Verb == SKPathVerb.Line);
        lineVerb.Points[1].Should().Be(new SKPoint(10f, 10f));
    }

    // 3+ points
    [Fact]
    public void Build_ThreePoints_ProducesMoveThenQuadThenLine()
    {
        var path = FreehandPathBuilder.Build([Pt(0f, 0f), Pt(10f, 0f), Pt(20f, 0f)]);

        var verbs = GetVerbs(path);
        verbs.Select(v => v.Verb).Should().Equal(
            SKPathVerb.Move,
            SKPathVerb.Quad,
            SKPathVerb.Line);
    }

    [Fact]
    public void Build_ThreePoints_QuadControlPointIsSecondInputPoint()
    {
        // For 3 points p0, p1, p2: QuadTo(p1, midpoint(p1,p2))
        var path = FreehandPathBuilder.Build([Pt(0f, 0f), Pt(10f, 0f), Pt(20f, 0f)]);

        var verbs = GetVerbs(path);
        var quad = verbs.Single(v => v.Verb == SKPathVerb.Quad);
        // pts[0] = start (implicit), pts[1] = control, pts[2] = end
        quad.Points[1].Should().Be(new SKPoint(10f, 0f));
    }

    [Fact]
    public void Build_ThreePoints_QuadEndPointIsMidpointOfSecondAndThirdInputPoints()
    {
        // midpoint of (10,0) and (20,0) = (15,0)
        var path = FreehandPathBuilder.Build([Pt(0f, 0f), Pt(10f, 0f), Pt(20f, 0f)]);

        var verbs = GetVerbs(path);
        var quad = verbs.Single(v => v.Verb == SKPathVerb.Quad);
        quad.Points[2].Should().Be(new SKPoint(15f, 0f));
    }

    [Fact]
    public void Build_ThreePoints_LineEndsAtLastInputPoint()
    {
        var path = FreehandPathBuilder.Build([Pt(0f, 0f), Pt(10f, 0f), Pt(20f, 0f)]);

        var verbs = GetVerbs(path);
        var line = verbs.Single(v => v.Verb == SKPathVerb.Line);
        line.Points[1].Should().Be(new SKPoint(20f, 0f));
    }

    [Fact]
    public void Build_FourPoints_ProducesMoveTwoQuadsThenLine()
    {
        var path = FreehandPathBuilder.Build(
            [Pt(0f, 0f), Pt(10f, 0f), Pt(20f, 0f), Pt(30f, 0f)]);

        var verbs = GetVerbs(path);
        verbs.Select(v => v.Verb).Should().Equal(
            SKPathVerb.Move,
            SKPathVerb.Quad,
            SKPathVerb.Quad,
            SKPathVerb.Line);
    }
}