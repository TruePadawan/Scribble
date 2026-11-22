using System.Text.Json;
using FluentAssertions;
using Scribble.Services.DocumentService;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using SkiaSharp;

namespace Scribble.UnitTests.ServiceTests;

/// <summary>
/// Exercise file encoding without creating an Avalonia dispatcher or canvas service.
/// </summary>
public class DocumentCodecTests
{
    private readonly DocumentCodec _codec = new();

    // A fixed pre-transform file keeps its placement IDs, order, shared image data, and defaults.
    [Fact]
    public async Task ReadAsync_LegacyFixture_LoadsPlacementsWithoutUiServices()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy-unversioned-document.scribble");
        await using var stream = File.OpenRead(path);

        using var document = await DocumentCodec.ReadAsync(stream);
        var images = document.TakeElements().Cast<CanvasImage>().ToList();

        images.Select(image => (image.Id, image.LayerIndex, image.Bounds.Left, image.Bounds.Top,
                image.Bounds.Width, image.Bounds.Height))
            .Should().Equal(
                (Guid.Parse("1eb49e11-6890-4c27-9d91-6c982a20b8b0"), 2, 5f, 7f, 20f, 30f),
                (Guid.Parse("a75d2609-e5a3-42a2-9497-60438f193b59"), 4, 50f, 60f, 40f, 15f));
        images.Select(image => image.ImageBase64String).Distinct().Should().ContainSingle();
        images.Should().AllSatisfy(image =>
        {
            image.Rotation.Should().Be(0);
            image.FlipX.Should().BeFalse();
            image.FlipY.Should().BeFalse();
        });
        document.BackgroundColor.Should().BeNull();
        stream.CanRead.Should().BeTrue();
    }

    // Repeated image bytes share one file entry while each placement keeps its own transform.
    [Fact]
    public async Task WriteThenRead_ImagePlacements_RoundTripsDetachedData()
    {
        var first = new CanvasImage
        {
            Id = Guid.NewGuid(),
            Bounds = SKRect.Create(3, 4, 20, 30),
            LayerIndex = 1,
            Rotation = 0.5f,
            FlipX = true,
            ImageBase64String = "RkFLRS1JTUFHRS1EQVRB"
        };
        var second = new CanvasImage
        {
            Id = Guid.NewGuid(),
            Bounds = SKRect.Create(50, 60, 40, 15),
            LayerIndex = 3,
            Rotation = -0.25f,
            FlipY = true,
            ImageBase64String = first.ImageBase64String
        };
        using var pathBuilder = new SKPathBuilder();
        pathBuilder.MoveTo(2, 4);
        pathBuilder.LineTo(18, 23);
        var stroke = new DrawStroke
        {
            Id = Guid.NewGuid(),
            ToolType = ToolType.Pencil,
            LayerIndex = 2,
            Path = pathBuilder.Snapshot(),
            Paint = new StrokePaint { Color = SKColors.DarkSlateBlue, StrokeWidth = 3 },
            ToolOptions = []
        };
        using var snapshot = new DocumentSnapshot([first, stroke, second], SKColors.CornflowerBlue);
        await using var stream = new MemoryStream();

        await DocumentCodec.WriteAsync(stream, snapshot);

        stream.Position = 0;
        using var json = await JsonDocument.ParseAsync(stream,
            cancellationToken: TestContext.Current.CancellationToken);
        json.RootElement.GetProperty("files").EnumerateObject().Count().Should().Be(1);
        stream.Position = 0;
        using var loaded = await DocumentCodec.ReadAsync(stream);
        var elements = loaded.TakeElements();
        using var loadedSnapshot = new DocumentSnapshot(elements, loaded.BackgroundColor ?? SKColors.Transparent);
        var loadedStroke = elements.OfType<DrawStroke>().Should().ContainSingle().Which;
        loadedStroke.Id.Should().Be(stroke.Id);
        loadedStroke.LayerIndex.Should().Be(2);
        loadedStroke.Path.Bounds.Should().Be(stroke.Path.Bounds);
        var images = elements.OfType<CanvasImage>().ToList();
        images.Select(image => (image.Id, image.LayerIndex, image.Rotation, image.FlipX, image.FlipY))
            .Should().Equal((first.Id, 1, 0.5f, true, false), (second.Id, 3, -0.25f, false, true));
        loaded.BackgroundColor.Should().Be(SKColors.CornflowerBlue);
        stream.CanWrite.Should().BeTrue();
    }
}