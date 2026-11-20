using System.Text;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using NSubstitute;
using Scribble.Services.CanvasStateService;
using Scribble.Services.DocumentService;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using SkiaSharp;

namespace Scribble.UnitTests.ServiceTests;

public class DocumentServiceTests
{
    private readonly IDocumentService _documentService;
    private readonly ICanvasStateService _canvasStateService;

    public DocumentServiceTests()
    {
        _canvasStateService = Substitute.For<ICanvasStateService>();
        _documentService = new DocumentService(_canvasStateService);
    }

    private static DrawStroke MakeStroke(SKColor? color = null)
    {
        var builder = new SKPathBuilder();
        builder.MoveTo(0f, 0f);
        builder.LineTo(100f, 50f);
        var path = builder.Snapshot();

        return new DrawStroke
        {
            Id = Guid.NewGuid(),
            ToolType = ToolType.Pencil,
            LayerIndex = 0,
            Path = path,
            Paint = new StrokePaint { Color = color ?? SKColors.Red, StrokeWidth = 3f },
            ToolOptions = []
        };
    }

    private static CanvasImage MakeImage()
    {
        const string base64String = "BASE64_ENCODED_IMAGE_DATA";
        return new CanvasImage
        {
            ImageBase64String = base64String,
            Bounds = SKRect.Create(0f, 0f, 100f, 100f)
        };
    }

    private static MemoryStream MakeStream(string json) =>
        new(Encoding.UTF8.GetBytes(json));

    // SaveAsync

    [AvaloniaFact]
    public async Task SaveAsync_CanvasHasStrokes_StreamContainsValidJSON()
    {
        var stroke = MakeStroke();
        _canvasStateService.CanvasElements.Returns([stroke]);
        _canvasStateService.BackgroundColor.Returns(SKColors.White);
        using var stream = new MemoryStream();

        await _documentService.SaveAsync(stream);

        stream.Position = 0;
        using var jsonDoc = await JsonDocument.ParseAsync(stream);
        var root = jsonDoc.RootElement;

        root.GetProperty("elements").GetProperty("strokes").GetArrayLength()
            .Should().Be(1);
    }

    [AvaloniaFact]
    public async Task SaveAsync_CanvasHasImages_StreamContainsValidJSON()
    {
        var image = MakeImage();
        _canvasStateService.CanvasElements.Returns([image]);
        _canvasStateService.BackgroundColor.Returns(SKColors.White);
        using var stream = new MemoryStream();

        await _documentService.SaveAsync(stream);

        stream.Position = 0;
        using var jsonDoc = await JsonDocument.ParseAsync(stream);
        var root = jsonDoc.RootElement;

        root.GetProperty("elements").GetProperty("images").GetArrayLength()
            .Should().Be(1);
        root.GetProperty("files").GetPropertyCount().Should().Be(1);
    }

    [AvaloniaFact]
    public async Task SaveAsync_CanvasHasDuplicateImages_NoDuplicateFilesInJSON()
    {
        var image = MakeImage();
        _canvasStateService.CanvasElements.Returns([image, image]);
        _canvasStateService.BackgroundColor.Returns(SKColors.White);
        using var stream = new MemoryStream();

        await _documentService.SaveAsync(stream);

        stream.Position = 0;
        using var jsonDoc = await JsonDocument.ParseAsync(stream);
        var root = jsonDoc.RootElement;

        root.GetProperty("elements").GetProperty("images").GetArrayLength()
            .Should().Be(2);
        root.GetProperty("files").GetPropertyCount().Should().Be(1);
    }

    [AvaloniaFact]
    public async Task SaveAsync_BackgroundColorIsSaved()
    {
        _canvasStateService.BackgroundColor.Returns(SKColors.Red);
        using var stream = new MemoryStream();

        await _documentService.SaveAsync(stream);

        stream.Position = 0;
        using var jsonDoc = await JsonDocument.ParseAsync(stream);
        var root = jsonDoc.RootElement;

        root.GetProperty("backgroundColor").GetString().Should().Be(SKColors.Red.ToString());
    }

    // LoadAsync

    [AvaloniaFact]
    public async Task LoadAsync_ValidJSONStream_CallsLoadCanvasWithCorrectElements()
    {
        var stroke = MakeStroke();
        _canvasStateService.CanvasElements.Returns([stroke]);
        using var stream = new MemoryStream();
        await _documentService.SaveAsync(stream);
        stream.Position = 0;

        await _documentService.LoadAsync(stream);

        _canvasStateService.Received(1).LoadCanvas(Arg.Is<List<CanvasElement>>(list => list.Count == 1));
    }

    [AvaloniaFact]
    public async Task LoadAsync_ValidJSONStream_CallsSetBackgroundColor()
    {
        _canvasStateService.BackgroundColor.Returns(SKColors.Blue);
        using var saveStream = new MemoryStream();
        await _documentService.SaveAsync(saveStream);
        saveStream.Position = 0;

        await _documentService.LoadAsync(saveStream);

        _canvasStateService.Received(1).SetBackgroundColor(SKColors.Blue);
    }

    [AvaloniaFact]
    public async Task LoadAsync_MissingElementsKey_ThrowsException()
    {
        using var stream = MakeStream("""{ "files": {}, "backgroundColor": "#FF000000" }""");

        var act = async () => await _documentService.LoadAsync(stream);

        await act.Should().ThrowAsync<Exception>().WithMessage("*elements*");
    }

    [AvaloniaFact]
    public async Task LoadAsync_MissingStrokesKey_ThrowsException()
    {
        using var stream = MakeStream("""
                                      {
                                          "elements": { "images": [] },
                                          "files": {},
                                          "backgroundColor": "#FF000000"
                                      }
                                      """);

        var act = async () => await _documentService.LoadAsync(stream);

        await act.Should().ThrowAsync<Exception>().WithMessage("*strokes*");
    }

    [AvaloniaFact]
    public async Task LoadAsync_MissingFilesKey_ThrowsException()
    {
        using var stream = MakeStream("""
                                      {
                                          "elements": { "strokes": [], "images": [] },
                                          "backgroundColor": "#FF000000"
                                      }
                                      """);

        var act = async () => await _documentService.LoadAsync(stream);

        await act.Should().ThrowAsync<Exception>().WithMessage("*files*");
    }

    // A malformed file is decoded fully before the active document is replaced.
    [AvaloniaFact]
    public async Task LoadAsync_MalformedDocument_LeavesCurrentCanvasUntouched()
    {
        using var stream = MakeStream("""{ "elements": { "strokes": [], "images": [] } }""");

        await Assert.ThrowsAsync<Exception>(() => _documentService.LoadAsync(stream));

        _canvasStateService.DidNotReceive().LoadCanvas(Arg.Any<List<CanvasElement>>());
        _canvasStateService.DidNotReceive().SetBackgroundColor(Arg.Any<SKColor>());
    }

    [AvaloniaFact]
    public async Task RoundTrip_SaveThenLoad_StrokeCountIsPreserved()
    {
        var stroke1 = MakeStroke(SKColors.Red);
        var stroke2 = MakeStroke(SKColors.Blue);
        _canvasStateService.CanvasElements.Returns([stroke1, stroke2]);
        _canvasStateService.BackgroundColor.Returns(SKColors.Black);

        using var stream = new MemoryStream();
        await _documentService.SaveAsync(stream);
        stream.Position = 0;
        await _documentService.LoadAsync(stream);

        _canvasStateService.Received(1).LoadCanvas(
            Arg.Is<List<CanvasElement>>(list => list.Count == 2));
    }

    // Change a live image after capture to verify the saved snapshot stays stable.
    [AvaloniaFact]
    public async Task RoundTrip_ImagesKeepIndependentTransformsAndShareEmbeddedFile()
    {
        var first = MakeImage();
        first.Rotation = 1.5f;
        first.FlipX = true;
        var second = MakeImage();
        second.Rotation = -0.5f;
        second.FlipY = true;
        List<CanvasElement> loaded = [];
        _canvasStateService.When(state => state.LoadCanvas(Arg.Any<List<CanvasElement>>()))
            .Do(call => loaded = call.Arg<List<CanvasElement>>());
        _canvasStateService.CanvasElements.Returns([first, second]);
        using var stream = new MemoryStream();

        using var snapshot = _documentService.CaptureSnapshot();
        first.Rotation = 0;
        first.FlipX = false;
        await _documentService.SaveAsync(stream, snapshot);
        stream.Position = 0;
        using var json = await JsonDocument.ParseAsync(stream);
        json.RootElement.GetProperty("files").GetPropertyCount().Should().Be(1);
        stream.Position = 0;
        await _documentService.LoadAsync(stream);

        loaded.Cast<CanvasImage>().Select(image => (image.Id, image.Rotation, image.FlipX, image.FlipY))
            .Should().Equal((first.Id, 1.5f, true, false), (second.Id, -0.5f, false, true));
    }

    // Load an image saved before transform fields were added.
    [AvaloniaFact]
    public async Task LoadAsync_LegacyImageDefaultsToUntransformed()
    {
        using var stream = MakeStream("""
            {
                "elements": {
                    "strokes": [],
                    "images": [{
                        "Width": 100, "Height": 50,
                        "FileId": "4efe31af-a08b-4123-82fd-cb8c3b12252a"
                    }]
                },
                "files": { "4efe31af-a08b-4123-82fd-cb8c3b12252a": "IMAGE" }
            }
            """);
        List<CanvasElement> loaded = [];
        _canvasStateService.When(state => state.LoadCanvas(Arg.Any<List<CanvasElement>>()))
            .Do(call => loaded = call.Arg<List<CanvasElement>>());

        await _documentService.LoadAsync(stream);

        var image = loaded.Should().ContainSingle().Which.Should().BeOfType<CanvasImage>().Which;
        image.Rotation.Should().Be(0);
        image.FlipX.Should().BeFalse();
        image.FlipY.Should().BeFalse();
    }
}
