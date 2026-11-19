using FluentAssertions;
using NSubstitute;
using Scribble.Controls;
using Scribble.Services.CanvasStateService;
using Scribble.Services.FileService;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Utils;
using Scribble.ViewModels;
using SkiaSharp;

namespace Scribble.UnitTests.UtilsTests;

// Cover the same rotated image geometry in bounds, viewport culling, and export.
public class RotatedImageBoundsTests
{
    // Centered flips preserve the footprint of a rotated image at negative coordinates.
    [Theory]
    [InlineData(0, 20, 120)]
    [InlineData(45, 98.99495f, 98.99495f)]
    [InlineData(90, 120, 20)]
    public void GetElementsBounds_FlippedImage_EnclosesRotatedFootprint(
        int degrees, float expectedWidth, float expectedHeight)
    {
        var image = CreateImage(degrees);

        var bounds = Utilities.GetElementsBounds([image]);

        bounds.Width.Should().BeApproximately(expectedWidth, 0.001f);
        bounds.Height.Should().BeApproximately(expectedHeight, 0.001f);
        bounds.MidX.Should().BeApproximately(image.Bounds.MidX, 0.001f);
        bounds.MidY.Should().BeApproximately(image.Bounds.MidY, 0.001f);
    }

    // Place the viewport over the rotated end that extends beyond the original rectangle.
    [Fact]
    public void IsCanvasElementVisible_OnlyRotatedEndInViewport_ReturnsTrue()
    {
        var image = CreateImage(90);
        var viewport = SKRect.Create(-127, -44, 8, 8);

        image.Bounds.IntersectsWith(viewport).Should().BeFalse();
        SkiaCanvas.IsCanvasElementVisible(image, viewport).Should().BeTrue();
    }

    // Sample both ends of the exported image to catch clipping by the original bounds.
    [Fact]
    public void GetImageData_RotatedTallImage_IncludesBothEnds()
    {
        using var source = new SKBitmap(20, 120);
        source.Erase(SKColors.Red);
        using var png = source.Encode(SKEncodedImageFormat.Png, 100);
        var image = CreateImage(90, Convert.ToBase64String(png.ToArray()));
        var export = new CanvasExportViewModel(Substitute.For<IFileService>(), Substitute.For<ICanvasStateService>());

        var encoded =
            CanvasExportViewModel.GetImageData([image], false, SKColors.Transparent, 1, SKEncodedImageFormat.Png);

        using var output = SKBitmap.Decode(encoded);
        // Rounded bounds can grow by one pixel due to float rotation.
        output.Width.Should().BeInRange(160, 161);
        output.Height.Should().BeInRange(60, 61);
        output.GetPixel(21, output.Height / 2).Should().Be(SKColors.Red);
        output.GetPixel(output.Width - 22, output.Height / 2).Should().Be(SKColors.Red);
    }

    // Create a tall flipped image whose center is outside the positive quadrant.
    private static CanvasImage CreateImage(int degrees, string imageData = "")
    {
        return new CanvasImage
        {
            Bounds = SKRect.Create(-80, -100, 20, 120),
            Rotation = MathF.PI * degrees / 180f,
            FlipX = true,
            ImageBase64String = imageData
        };
    }
}
