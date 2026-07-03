using FluentAssertions;
using NSubstitute;
using Scribble.Services.CanvasStateService;
using Scribble.Services.MultiUserDrawing;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using SkiaSharp;

namespace Scribble.IntegrationTests.Tools;

public class PointerToolsIntegrationTests
{
    private readonly CanvasStateService _sut;

    public PointerToolsIntegrationTests()
    {
        var mockHubService = Substitute.For<IMultiUserDrawingService>();
        mockHubService.Room.Returns((MultiUserDrawingRoom?)null);
        _sut = new CanvasStateService(mockHubService);
    }

    private static StrokePaint DefaultPaint() => new() { Color = SKColors.Black, StrokeWidth = 2f };

    [Fact]
    public void PencilFlow_GeneratesContinuousPath()
    {
        var actionId = Guid.NewGuid();
        var strokeId = Guid.NewGuid();

        _sut.ApplyEvent(new StartStrokeEvent(actionId, strokeId, new SKPoint(0f, 0f), DefaultPaint(), ToolType.Pencil,
            []));
        _sut.ApplyEvent(new PencilStrokeLineToEvent(actionId, strokeId, new SKPoint(10f, 10f)));
        _sut.ApplyEvent(new PencilStrokeLineToEvent(actionId, strokeId, new SKPoint(20f, 15f)));
        _sut.ApplyEvent(new EndStrokeEvent(actionId));

        var element = _sut.CanvasElements.Should().ContainSingle().Subject;
        var stroke = element.Should().BeOfType<DrawStroke>().Subject;
        stroke.ToolType.Should().Be(ToolType.Pencil);

        // Fast path for pencil rebuilds the path, so point count should be > 1
        stroke.Path.PointCount.Should().BeGreaterThan(1);
        stroke.Path.TightBounds.Width.Should().BeGreaterThan(0);
        stroke.Path.TightBounds.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public void LineFlow_GeneratesStraightLineToEndpoint()
    {
        var actionId = Guid.NewGuid();
        var strokeId = Guid.NewGuid();
        var startPoint = new SKPoint(10f, 10f);
        var endPoint = new SKPoint(100f, 100f);

        _sut.ApplyEvent(new StartStrokeEvent(actionId, strokeId, startPoint, DefaultPaint(), ToolType.Line, []));
        _sut.ApplyEvent(new LineStrokeLineToEvent(actionId, strokeId, endPoint));
        _sut.ApplyEvent(new EndStrokeEvent(actionId));

        var stroke = _sut.CanvasElements.Should().ContainSingle().Subject.Should().BeOfType<DrawStroke>().Subject;
        stroke.ToolType.Should().Be(ToolType.Line);

        // The path should contain the endpoint
        stroke.Path.Points.Should().Contain(p =>
            Math.Abs(p.X - endPoint.X) < 0.1f && Math.Abs(p.Y - endPoint.Y) < 0.1f);
    }

    [Fact]
    public void ArrowFlow_GeneratesPathWithArrowHead()
    {
        var actionId = Guid.NewGuid();
        var strokeId = Guid.NewGuid();
        var startPoint = new SKPoint(50f, 50f);
        var endPoint = new SKPoint(200f, 50f);

        _sut.ApplyEvent(new StartStrokeEvent(actionId, strokeId, startPoint, DefaultPaint(), ToolType.Arrow, []));
        _sut.ApplyEvent(new LineStrokeLineToEvent(actionId, strokeId, endPoint));
        _sut.ApplyEvent(new EndStrokeEvent(actionId));

        var stroke = _sut.CanvasElements.Should().ContainSingle().Subject.Should().BeOfType<DrawStroke>().Subject;
        stroke.ToolType.Should().Be(ToolType.Arrow);

        // Arrow paths have more points than a simple straight line because of the arrowhead lines
        stroke.Path.PointCount.Should().BeGreaterThan(2);
    }

    [Fact]
    public void EllipseFlow_GeneratesEllipseWithinBoundingBox()
    {
        var actionId = Guid.NewGuid();
        var strokeId = Guid.NewGuid();
        var startPoint = new SKPoint(0f, 0f);
        var endPoint = new SKPoint(100f, 50f);

        _sut.ApplyEvent(new StartStrokeEvent(actionId, strokeId, startPoint, DefaultPaint(), ToolType.Ellipse, []));
        _sut.ApplyEvent(new LineStrokeLineToEvent(actionId, strokeId, endPoint));
        _sut.ApplyEvent(new EndStrokeEvent(actionId));

        var stroke = _sut.CanvasElements.Should().ContainSingle().Subject.Should().BeOfType<DrawStroke>().Subject;
        stroke.ToolType.Should().Be(ToolType.Ellipse);

        var bounds = stroke.Path.TightBounds;
        bounds.Width.Should().BeApproximately(100f, precision: 5f);
        bounds.Height.Should().BeApproximately(50f, precision: 5f);
    }

    [Fact]
    public void RectangleFlow_GeneratesRectGeometry()
    {
        var actionId = Guid.NewGuid();
        var strokeId = Guid.NewGuid();
        var startPoint = new SKPoint(10f, 10f);
        var endPoint = new SKPoint(110f, 60f);

        _sut.ApplyEvent(new StartStrokeEvent(actionId, strokeId, startPoint, DefaultPaint(), ToolType.Rectangle, []));
        _sut.ApplyEvent(new LineStrokeLineToEvent(actionId, strokeId, endPoint));
        _sut.ApplyEvent(new EndStrokeEvent(actionId));

        var stroke = _sut.CanvasElements.Should().ContainSingle().Subject.Should().BeOfType<DrawStroke>().Subject;
        stroke.ToolType.Should().Be(ToolType.Rectangle);

        var bounds = stroke.Path.TightBounds;
        bounds.Width.Should().BeApproximately(100f, precision: 5f);
        bounds.Height.Should().BeApproximately(50f, precision: 5f);
    }

    [Fact]
    public void TextFlow_PreservesTextProperties()
    {
        var actionId = Guid.NewGuid();
        var strokeId = Guid.NewGuid();
        var position = new SKPoint(50f, 50f);
        var textContent = "Integration Test String";

        _sut.ApplyEvent(new AddTextEvent(actionId, strokeId, position, textContent, new StrokePaint { TextSize = 24f },
            []));

        var textElement = _sut.CanvasElements.Should().ContainSingle().Subject.Should().BeOfType<TextStroke>().Subject;
        textElement.Text.Should().Be(textContent);
        textElement.Position.X.Should().BeApproximately(position.X, 0.1f);
        textElement.Position.Y.Should().BeApproximately(position.Y, 0.1f);
        textElement.Paint.TextSize.Should().Be(24f);
    }

    [Fact]
    public void EraseFlow_RemovesTargetedElement()
    {
        // Draw a rectangle to act as target
        var rectAction = Guid.NewGuid();
        var rectId = Guid.NewGuid();
        _sut.ApplyEvent(new StartStrokeEvent(rectAction, rectId, new SKPoint(0f, 0f), DefaultPaint(),
            ToolType.Rectangle, []));
        _sut.ApplyEvent(new LineStrokeLineToEvent(rectAction, rectId, new SKPoint(100f, 100f)));
        _sut.ApplyEvent(new EndStrokeEvent(rectAction));

        _sut.CanvasElements.Should().HaveCount(1);

        // Erase the rectangle
        var eraseAction = Guid.NewGuid();
        var eraseId = Guid.NewGuid();
        _sut.ApplyEvent(new StartEraseStrokeEvent(eraseAction, eraseId, new SKPoint(-10f, 50f)));
        _sut.ApplyEvent(new EraseStrokeLineToEvent(eraseAction, eraseId, new SKPoint(110f, 50f))); // Swipe across
        _sut.ApplyEvent(new TriggerEraseEvent(eraseAction, eraseId)); // Execute erase

        _sut.CanvasElements.Should().BeEmpty();
    }

    [Fact]
    public void SelectFlow_CapturesAndTransformsElement()
    {
        // Draw target
        var strokeAction = Guid.NewGuid();
        var strokeId = Guid.NewGuid();
        _sut.ApplyEvent(new StartStrokeEvent(strokeAction, strokeId, new SKPoint(50f, 50f), DefaultPaint(),
            ToolType.Ellipse, []));
        _sut.ApplyEvent(new LineStrokeLineToEvent(strokeAction, strokeId, new SKPoint(150f, 150f)));
        _sut.ApplyEvent(new EndStrokeEvent(strokeAction));

        var initialBounds = ((DrawStroke)_sut.CanvasElements[0]).Path.TightBounds;

        // Select target
        var selectAction = Guid.NewGuid();
        var boundId = Guid.NewGuid();
        _sut.ApplyEvent(new CreateSelectionBoundEvent(selectAction, boundId, new SKPoint(0f, 0f)));
        _sut.ApplyEvent(new IncreaseSelectionBoundEvent(selectAction, boundId,
            new SKPoint(200f, 200f))); // Envelopes the ellipse
        _sut.ApplyEvent(new EndSelectionEvent(selectAction, boundId));

        _sut.ActiveSelectionBoundId.Should().Be(boundId);
        _sut.SelectedElementIds.Should().Contain(strokeId);

        // Move target
        var transformAction = Guid.NewGuid();
        var delta = new SKPoint(200f, 0f);
        _sut.ApplyEvent(new MoveCanvasElementsEvent(transformAction, boundId, delta));

        var transformedBounds = ((DrawStroke)_sut.CanvasElements[0]).Path.TightBounds;
        transformedBounds.MidX.Should().BeApproximately(initialBounds.MidX + delta.X, 1f);
    }

    [Fact]
    public void ImageFlow_InstantiatesImageNodeWithBounds()
    {
        var actionId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        const string base64Dummy =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="; // 1x1 transparent png
        var position = new SKPoint(100f, 100f);
        var size = new SKSize(500f, 300f);

        _sut.ApplyEvent(new AddImageEvent(actionId, imageId, base64Dummy, position, size));

        var imageElement = _sut.CanvasElements.Should().ContainSingle().Subject.Should().BeOfType<CanvasImage>()
            .Subject;
        imageElement.ImageBase64String.Should().Be(base64Dummy);
        imageElement.Bounds.Left.Should().Be(position.X);
        imageElement.Bounds.Top.Should().Be(position.Y);
        imageElement.Bounds.Width.Should().Be(size.Width);
        imageElement.Bounds.Height.Should().Be(size.Height);
    }
}