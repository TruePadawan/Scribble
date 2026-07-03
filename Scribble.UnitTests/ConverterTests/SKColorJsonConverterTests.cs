using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Scribble.Shared.Converters;
using SkiaSharp;

namespace Scribble.UnitTests.ConverterTests;

public class SKColorJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new SKColorJsonConverter() }
    };

    private class ColorPayload
    {
        [JsonConverter(typeof(SKColorJsonConverter))]
        public SKColor Color { get; init; }
    }

    // Write
    [Fact]
    public void Write_OpaqueRed_WritesHexString()
    {
        var color = new SKColor(255, 0, 0, 255);

        var json = JsonSerializer.Serialize(color, Options);

        Console.WriteLine(color);
        json.Should().Be($"\"{color}\"");
    }

    [Fact]
    public void Write_TransparentColor_IncludesAlphaInHexString()
    {
        var color = new SKColor(128, 255, 64, 0);

        var json = JsonSerializer.Serialize(color, Options);

        json.Should().Be($"\"{color}\"");
    }

    [Fact]
    public void Write_Black_WritesHexString()
    {
        var color = SKColors.Black;

        var json = JsonSerializer.Serialize(color, Options);

        json.Should().Be($"\"{color}\"");
    }

    [Fact]
    public void Write_White_WritesHexString()
    {
        var color = SKColors.White;

        var json = JsonSerializer.Serialize(color, Options);

        json.Should().Be($"\"{color}\"");
    }

    [Fact]
    public void Write_FullyTransparent_WritesHexString()
    {
        var color = SKColors.Transparent;

        var json = JsonSerializer.Serialize(color, Options);

        json.Should().Be($"\"{color}\"");
    }

    // Read
    [Fact]
    public void Read_EightDigitHex_ParsesCorrectly()
    {
        var color = new SKColor(255, 0, 0, 255);
        var json = $"\"{color}\"";

        var result = JsonSerializer.Deserialize<SKColor>(json, Options);

        result.Should().Be(color);
    }

    [Fact]
    public void Read_ColorWithAlpha_ParsesCorrectly()
    {
        var color = new SKColor(0, 128, 255, 64);
        var json = $"\"{color}\"";

        var result = JsonSerializer.Deserialize<SKColor>(json, Options);

        result.Should().Be(color);
    }

    [Fact]
    public void Read_BlackHex_ParsesCorrectly()
    {
        var json = $"\"{SKColors.Black}\"";

        var result = JsonSerializer.Deserialize<SKColor>(json, Options);

        result.Should().Be(SKColors.Black);
    }

    [Fact]
    public void Read_WhiteHex_ParsesCorrectly()
    {
        var json = $"\"{SKColors.White}\"";

        var result = JsonSerializer.Deserialize<SKColor>(json, Options);

        result.Should().Be(SKColors.White);
    }

    [Fact]
    public void Read_FullyTransparentHex_ParsesCorrectly()
    {
        var json = $"\"{SKColors.Transparent}\"";

        var result = JsonSerializer.Deserialize<SKColor>(json, Options);

        result.Should().Be(SKColors.Transparent);
    }

    [Fact]
    public void Read_ShortSixDigitHex_ParsesAsOpaqueColor()
    {
        // SKColor.Parse accepts "#RRGGBB" and treats alpha as 0xFF
        var result = JsonSerializer.Deserialize<SKColor>("\"#FF0000\"", Options);

        result.Red.Should().Be(255);
        result.Green.Should().Be(0);
        result.Blue.Should().Be(0);
        result.Alpha.Should().Be(255);
    }

    [Fact]
    public void Read_HexWithoutHash_ParsesCorrectly()
    {
        // SKColor.Parse accepts input without the leading '#'
        var expected = new SKColor(255, 0, 0, 255);

        var result = JsonSerializer.Deserialize<SKColor>("\"FFFF0000\"", Options);

        result.Should().Be(expected);
    }

    [Fact]
    public void Read_LowercaseHex_ParsesCorrectly()
    {
        var expected = new SKColor(0, 255, 0, 255);
        var json = $"\"{expected.ToString().ToLowerInvariant()}\"";

        var result = JsonSerializer.Deserialize<SKColor>(json, Options);

        result.Should().Be(expected);
    }

    // Round-trip
    [Theory]
    [InlineData(255, 0, 0, 255)]
    [InlineData(0, 255, 0, 255)]
    [InlineData(0, 0, 255, 255)]
    [InlineData(128, 64, 32, 200)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(255, 255, 255, 255)]
    public void RoundTrip_SerializeDeserialize_ReturnsOriginalColor(
        byte r, byte g, byte b, byte a)
    {
        var original = new SKColor(r, g, b, a);

        var json = JsonSerializer.Serialize(original, Options);
        var result = JsonSerializer.Deserialize<SKColor>(json, Options);

        result.Should().Be(original);
    }

    [Fact]
    public void RoundTrip_ColorInObject_PreservesValue()
    {
        var payload = new ColorPayload { Color = new SKColor(100, 150, 200, 220) };

        var json = JsonSerializer.Serialize(payload, Options);
        var result = JsonSerializer.Deserialize<ColorPayload>(json, Options);

        result.Should().NotBeNull();
        result.Color.Should().Be(payload.Color);
    }

    // Error Handling
    [Fact]
    public void Read_InvalidHexString_ThrowsException()
    {
        var act = () => JsonSerializer.Deserialize<SKColor>("\"not-a-color\"", Options);

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void Read_EmptyString_ThrowsException()
    {
        var act = () => JsonSerializer.Deserialize<SKColor>("\"\"", Options);

        act.Should().Throw<Exception>();
    }
}