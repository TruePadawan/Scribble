using System.Text.Json;
using FluentAssertions;
using Scribble.Shared.Converters;
using SkiaSharp;

namespace Scribble.UnitTests.ConverterTests;

public class SKMatrixJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new SKMatrixJsonConverter() }
    };

    // Write
    [Fact]
    public void Write_IdentityMatrix_ProducesArrayOfNineElements()
    {
        var matrix = SKMatrix.Identity;

        var json = JsonSerializer.Serialize(matrix, Options);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetArrayLength().Should().Be(9);
    }

    [Fact]
    public void Write_IdentityMatrix_ValuesMatchMatrixValues()
    {
        var matrix = SKMatrix.Identity;

        var json = JsonSerializer.Serialize(matrix, Options);

        using var doc = JsonDocument.Parse(json);
        var elements = doc.RootElement.EnumerateArray().Select(e => e.GetSingle()).ToArray();
        elements.Should().Equal(matrix.Values);
    }

    [Fact]
    public void Write_ScaleMatrix_ProducesCorrectScaleValues()
    {
        var matrix = SKMatrix.CreateScale(2f, 3f);

        var json = JsonSerializer.Serialize(matrix, Options);

        using var doc = JsonDocument.Parse(json);
        var elements = doc.RootElement.EnumerateArray().Select(e => e.GetSingle()).ToArray();
        elements.Should().Equal(matrix.Values);
    }

    [Fact]
    public void Write_TranslationMatrix_ProducesCorrectTranslationValues()
    {
        var matrix = SKMatrix.CreateTranslation(100f, 200f);

        var json = JsonSerializer.Serialize(matrix, Options);

        using var doc = JsonDocument.Parse(json);
        var elements = doc.RootElement.EnumerateArray().Select(e => e.GetSingle()).ToArray();
        elements.Should().Equal(matrix.Values);
    }

    // Read
    [Fact]
    public void Read_IdentityMatrixJson_ReturnsIdentityMatrix()
    {
        var json = JsonSerializer.Serialize(SKMatrix.Identity, Options);

        var result = JsonSerializer.Deserialize<SKMatrix>(json, Options);

        result.Values.Should().Equal(SKMatrix.Identity.Values);
    }

    [Fact]
    public void Read_ScaleMatrixJson_ReturnsCorrectMatrix()
    {
        var original = SKMatrix.CreateScale(4f, 5f);
        var json = JsonSerializer.Serialize(original, Options);

        var result = JsonSerializer.Deserialize<SKMatrix>(json, Options);

        result.Values.Should().Equal(original.Values);
    }

    [Fact]
    public void Read_NonArrayToken_ThrowsJsonException()
    {
        var act = () => JsonSerializer.Deserialize<SKMatrix>("\"not an array\"", Options);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Read_ArrayWithTooFewElements_ThrowsJsonException()
    {
        // Only 3 elements instead of 9
        var act = () => JsonSerializer.Deserialize<SKMatrix>("[1,0,0]", Options);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Read_ArrayWithTooManyElements_ThrowsJsonException()
    {
        // 10 elements, one too many
        var act = () => JsonSerializer.Deserialize<SKMatrix>("[1,0,0,0,1,0,0,0,1,99]", Options);

        act.Should().Throw<JsonException>();
    }

    // Round-trip

    [Fact]
    public void RoundTrip_IdentityMatrix_PreservesAllValues()
    {
        var original = SKMatrix.Identity;

        var json = JsonSerializer.Serialize(original, Options);
        var result = JsonSerializer.Deserialize<SKMatrix>(json, Options);

        result.Values.Should().Equal(original.Values);
    }

    [Fact]
    public void RoundTrip_ScaleAndTranslateMatrix_PreservesAllValues()
    {
        var original = SKMatrix.CreateScale(2f, 3f)
            .PostConcat(SKMatrix.CreateTranslation(50f, 75f));

        var json = JsonSerializer.Serialize(original, Options);
        var result = JsonSerializer.Deserialize<SKMatrix>(json, Options);

        result.Values.Should().Equal(original.Values);
    }

    [Fact]
    public void RoundTrip_RotationMatrix_PreservesAllValues()
    {
        var original = SKMatrix.CreateRotationDegrees(45f);

        var json = JsonSerializer.Serialize(original, Options);
        var result = JsonSerializer.Deserialize<SKMatrix>(json, Options);

        // Float serialization may introduce tiny rounding; allow a small tolerance
        for (int i = 0; i < 9; i++)
        {
            result.Values[i].Should().BeApproximately(original.Values[i], 1e-5f);
        }
    }
}