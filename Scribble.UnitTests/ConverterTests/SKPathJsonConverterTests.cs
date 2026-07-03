using System.Text.Json;
using FluentAssertions;
using Scribble.Shared.Converters;
using SkiaSharp;

namespace Scribble.UnitTests.ConverterTests;

public class SKPathJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new SKPathJsonConverter() }
    };

    // Write
    [Fact]
    public void Write_EmptyPath_ProducesEmptyString()
    {
        var path = new SKPath();

        var json = JsonSerializer.Serialize(path, Options);

        json.Should().Be("\"\"");
    }

    [Fact]
    public void Write_PathWithLine_ProducesSvgMoveAndLineCommands()
    {
        // A MoveTo alone produces no SVG output in Skia; a drawing operation is required
        var path = new SKPath();
        path.MoveTo(0f, 0f);
        path.LineTo(10f, 20f);

        var json = JsonSerializer.Serialize(path, Options);

        json.Should().Contain("M").And.Contain("L");
    }

    [Fact]
    public void Write_PathWithLine_ProducesSvgLineCommand()
    {
        var path = new SKPath();
        path.MoveTo(0f, 0f);
        path.LineTo(10f, 10f);

        var json = JsonSerializer.Serialize(path, Options);

        json.Should().Contain("L");
    }

    [Fact]
    public void Write_PathWithQuadCurve_ProducesSvgQuadCommand()
    {
        var path = new SKPath();
        path.MoveTo(0f, 0f);
        path.QuadTo(5f, 10f, 10f, 0f);

        var json = JsonSerializer.Serialize(path, Options);

        json.Should().Contain("Q");
    }

    // Read
    [Fact]
    public void Read_EmptyString_ReturnsEmptyPath()
    {
        var result = JsonSerializer.Deserialize<SKPath>("\"\"", Options);

        result.Should().NotBeNull();
        result.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Read_SimpleSvgMoveTo_ProducesPathWithCorrectPoint()
    {
        var result = JsonSerializer.Deserialize<SKPath>("\"M 10 20\"", Options);

        result.Should().NotBeNull();
        result.GetPoint(0).Should().Be(new SKPoint(10f, 20f));
    }

    [Fact]
    public void Read_SvgMoveAndLine_ProducesPathWithTwoPoints()
    {
        var result = JsonSerializer.Deserialize<SKPath>("\"M 0 0 L 10 10\"", Options);

        result.Should().NotBeNull();
        result.PointCount.Should().Be(2);
    }

    // Round-trip
    [Fact]
    public void RoundTrip_EmptyPath_RemainsEmpty()
    {
        var original = new SKPath();

        var json = JsonSerializer.Serialize(original, Options);
        var result = JsonSerializer.Deserialize<SKPath>(json, Options);

        result.Should().NotBeNull();
        result.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_PathWithLine_PointsArePreserved()
    {
        var original = new SKPath();
        original.MoveTo(0f, 0f);
        original.LineTo(100f, 50f);

        var json = JsonSerializer.Serialize(original, Options);
        var result = JsonSerializer.Deserialize<SKPath>(json, Options);

        result.Should().NotBeNull();
        result.PointCount.Should().Be(original.PointCount);
        result.GetPoint(0).Should().Be(original.GetPoint(0));
        result.GetPoint(1).Should().Be(original.GetPoint(1));
    }

    [Fact]
    public void RoundTrip_ComplexPath_SvgDataIsPreserved()
    {
        // Compare SVG strings rather than PointCount: Close() inserts an implicit
        // closing point that causes PointCount to differ after deserialization.
        var original = new SKPath();
        original.MoveTo(0f, 0f);
        original.LineTo(50f, 0f);
        original.QuadTo(75f, 50f, 50f, 100f);
        original.LineTo(0f, 100f);
        original.Close();

        var json = JsonSerializer.Serialize(original, Options);
        var result = JsonSerializer.Deserialize<SKPath>(json, Options);

        result.Should().NotBeNull();
        result.ToSvgPathData().Should().Be(original.ToSvgPathData());
    }
}