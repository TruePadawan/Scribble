using Avalonia.Data;
using FluentAssertions;
using Scribble.Converters;
using Scribble.Shared.Lib;
using System.Globalization;

namespace Scribble.Tests.ConverterTests;

public class EnumToBoolConverterTests
{
    private readonly EnumToBoolConverter _sut = new();
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;
    private readonly Type _targetType = typeof(bool);

    // Convert

    [Fact]
    public void Convert_ValueMatchesParameter_ReturnsTrue()
    {
        var result = _sut.Convert(ToolType.Pencil, _targetType, ToolType.Pencil, _culture);

        result.Should().Be(true);
    }

    [Fact]
    public void Convert_ValueDoesNotMatchParameter_ReturnsFalse()
    {
        var result = _sut.Convert(ToolType.Pencil, _targetType, ToolType.Line, _culture);

        result.Should().Be(false);
    }

    [Fact]
    public void Convert_NullValue_ReturnsFalse()
    {
        var result = _sut.Convert(null, _targetType, ToolType.Pencil, _culture);

        result.Should().Be(false);
    }

    // ConvertBack

    [Fact]
    public void ConvertBack_TrueWithNonNullParameter_ReturnsParameter()
    {
        var result = _sut.ConvertBack(true, typeof(ToolType), ToolType.Line, _culture);

        result.Should().Be(ToolType.Line);
    }

    [Fact]
    public void ConvertBack_FalseWithNonNullParameter_ReturnsDoNothing()
    {
        var result = _sut.ConvertBack(false, typeof(ToolType), ToolType.Line, _culture);

        result.Should().Be(BindingOperations.DoNothing);
    }

    [Fact]
    public void ConvertBack_TrueWithNullParameter_ReturnsDoNothing()
    {
        var result = _sut.ConvertBack(true, typeof(ToolType), null, _culture);

        result.Should().Be(BindingOperations.DoNothing);
    }

    [Fact]
    public void ConvertBack_NullValue_ReturnsDoNothing()
    {
        var result = _sut.ConvertBack(null, typeof(ToolType), ToolType.Pencil, _culture);

        result.Should().Be(BindingOperations.DoNothing);
    }
}