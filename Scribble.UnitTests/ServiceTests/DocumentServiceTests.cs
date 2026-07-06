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
        var path = new SKPath();
        path.MoveTo(0f, 0f);
        path.LineTo(100f, 50f);

        return new DrawStroke(Guid.NewGuid())
        {
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
}