using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using FluentAssertions;
using Scribble.Controls;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using Scribble.State;
using Scribble.ViewModels;
using SkiaSharp;

namespace Scribble.UnitTests.UtilsTests;

// Keep Avalonia frame tests in one non-parallel collection.
[CollectionDefinition(DisableParallelization = true)]
public class CanvasFrameCollection;

// Exercise the screen recording boundary and compare it with the exported drawing.
[Collection(typeof(CanvasFrameCollection))]
public class CanvasFrameTests
{
    // A queued frame keeps its geometry, pixels and viewport after the live board changes.
    [AvaloniaFact]
    public void CaptureFrame_AfterEditingAndPanning_ReplaysOriginalExportPixels()
    {
        var image = CreateImage();
        using var builder = new SKPathBuilder();
        builder.AddRect(SKRect.Create(21, 21, 24, 17));
        using var path = builder.Snapshot();
        var stroke = new DrawStroke
        {
            ToolType = ToolType.Rectangle, ToolOptions = [], Path = path,
            Paint = new StrokePaint { Color = SKColors.Black, FillColor = SKColors.Green, StrokeWidth = 2 }
        };
        try
        {
            var view = new SkiaCanvas { CanvasBackground = Colors.Transparent, CanvasElements = [stroke, image] };
            var camera = view.Camera;
            view.Arrange(new Rect(0, 0, 90, 80));
            using var recorded = view.CaptureFrame();
            var png = CanvasExportViewModel.GetImageData(view.CanvasElements, false, SKColors.Transparent, 1,
                SKEncodedImageFormat.Png);
            using var export = SKBitmap.Decode(png);

            // The original export starts at world (0, 0) after its 20-unit padding.
            path.Transform(SKMatrix.CreateTranslation(100, 100));
            stroke.Paint.FillColor = SKColors.Yellow;
            image.Bounds = SKRect.Create(110, 100, 3, 3);
            image.GetBitmap();
            image.ReleaseCachedBitmap();
            camera.SetZoom(2);
            camera.WorldOffSetX = 100;

            using var screen = new SKBitmap(90, 80);
            using var canvas = new SKCanvas(screen);
            canvas.DrawPicture(recorded);
            screen.GetPixel(30, 30).Should().Be(SKColors.Green);
            var colors = new HashSet<SKColor>();
            for (var y = 19; y < 56; y++)
            for (var x = 19; x < 76; x++)
            {
                // This region contains both shapes and no screen-only grid dots.
                Assert.Equal(export.GetPixel(x, y), screen.GetPixel(x, y));
                colors.Add(screen.GetPixel(x, y));
            }

            colors.Should().Contain(SKColors.Red).And.Contain(SKColors.Blue);
        }
        finally
        {
            image.ReleaseCachedBitmap();
        }
    }

    // Translation and scaling above the source resolution reuse the decoded image.
    [Fact]
    public void GetBitmap_MovingOrGrowingPlacement_ReusesPixelsUntilDecodeSizeChanges()
    {
        var image = CreateImage();
        try
        {
            var first = image.GetBitmap();
            image.Bounds = SKRect.Create(100, 120, 10, 18);
            image.GetBitmap().Should().BeSameAs(first);
            image.Bounds = SKRect.Create(100, 120, 20, 36);
            image.GetBitmap().Should().BeSameAs(first);
            image.Bounds = SKRect.Create(100, 120, 5, 9);
            var resized = image.GetBitmap();
            resized.Width.Should().Be(5);
            resized.Height.Should().Be(9);
            resized.Should().NotBeSameAs(first);
        }
        finally
        {
            image.ReleaseCachedBitmap();
        }
    }

    // Use distinct left and right halves to expose rotation or flip mismatches.
    private static CanvasImage CreateImage()
    {
        using var source = new SKBitmap(10, 18);
        source.Erase(SKColors.Red);
        source.Erase(SKColors.Blue, new SKRectI(5, 0, 10, 18));
        using var png = source.Encode(SKEncodedImageFormat.Png, 100);
        return new CanvasImage
        {
            LayerIndex = 1, ImageBase64String = Convert.ToBase64String(png.ToArray()),
            Bounds = SKRect.Create(57, 39, 10, 18), Rotation = MathF.PI / 2, FlipX = true
        };
    }
}