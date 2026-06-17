using Avalonia.Media;
using FluentAssertions;
using Scribble.Utils;
using SkiaSharp;

namespace Scribble.Tests.Utils;

public class UtilitiesTests
{
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
}