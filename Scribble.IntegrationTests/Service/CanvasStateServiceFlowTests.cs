using FluentAssertions;
using NSubstitute;
using Scribble.Services.CanvasStateService;
using Scribble.Services.MultiUserDrawing;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using Scribble.Shared.Lib.Events;
using SkiaSharp;

namespace Scribble.IntegrationTests.Service;

public class CanvasStateServiceFlowTests
{
    private readonly CanvasStateService _sut;

    public CanvasStateServiceFlowTests()
    {
        var mockHubService = Substitute.For<IMultiUserDrawingService>();
        mockHubService.Room.Returns((MultiUserDrawingRoom?)null);
        _sut = new CanvasStateService(mockHubService);
    }

    private static StrokePaint DefaultPaint()
    {
        return new StrokePaint { Color = SKColors.Black, StrokeWidth = 2f };
    }

    [Fact]
    public void DrawUndoRedo_MultiStroke_StateRestored()
    {
        // Act 1: Draw first stroke (Line)
        var action1 = Guid.NewGuid();
        var stroke1 = Guid.NewGuid();
        _sut.ApplyEvent(
            new StartStrokeEvent(action1, stroke1, new SKPoint(0f, 0f), DefaultPaint(), ToolType.Pencil, []));
        _sut.ApplyEvent(new PencilStrokeLineToEvent(action1, stroke1, new SKPoint(10f, 10f)));
        _sut.ApplyEvent(new EndStrokeEvent(action1));

        // Act 2: Draw second stroke (Arrow)
        var action2 = Guid.NewGuid();
        var stroke2 = Guid.NewGuid();
        _sut.ApplyEvent(new StartStrokeEvent(action2, stroke2, new SKPoint(20f, 20f), DefaultPaint(), ToolType.Arrow,
            []));
        _sut.ApplyEvent(new LineStrokeLineToEvent(action2, stroke2, new SKPoint(30f, 30f)));
        _sut.ApplyEvent(new EndStrokeEvent(action2));

        // Assert Step 1: Canvas has 2 elements
        _sut.CanvasElements.Should().HaveCount(2);

        // Act 3: Undo once (removes stroke 2)
        _sut.Undo();
        _sut.CanvasElements.Should().HaveCount(1);
        _sut.CanvasElements[0].Id.Should().Be(stroke1);

        // Act 4: Undo again (removes stroke 1)
        _sut.Undo();
        _sut.CanvasElements.Should().BeEmpty();

        // Act 5: Redo (restores stroke 1)
        _sut.Redo();
        _sut.CanvasElements.Should().HaveCount(1);
        _sut.CanvasElements[0].Id.Should().Be(stroke1);

        // Act 6: Redo again (restores stroke 2)
        _sut.Redo();
        _sut.CanvasElements.Should().HaveCount(2);
        _sut.CanvasElements[1].Id.Should().Be(stroke2);
    }

    [Fact]
    public void EraseUndoRedo_EraseRestored()
    {
        // Setup: Draw a stroke
        var action1 = Guid.NewGuid();
        var stroke1 = Guid.NewGuid();
        var startPoint = new SKPoint(0f, 0f);
        _sut.ApplyEvent(new StartStrokeEvent(action1, stroke1, startPoint, DefaultPaint(), ToolType.Pencil, []));
        _sut.ApplyEvent(new PencilStrokeLineToEvent(action1, stroke1, new SKPoint(100f, 100f)));
        _sut.ApplyEvent(new EndStrokeEvent(action1));

        // Act 1: Erase the stroke
        var action2 = Guid.NewGuid();
        _sut.ApplyEvent(new StartEraseStrokeEvent(action2, stroke1, startPoint));
        _sut.ApplyEvent(new TriggerEraseEvent(action2, stroke1));

        // Assert Step 1: Canvas is empty
        _sut.CanvasElements.Should().BeEmpty();

        // Act 2: Undo the erase
        _sut.Undo();

        // Assert Step 2: Stroke is back
        _sut.CanvasElements.Should().HaveCount(1);
        _sut.CanvasElements[0].Id.Should().Be(stroke1);

        // Act 3: Redo the erase
        _sut.Redo();

        // Assert Step 3: Stroke is gone again
        _sut.CanvasElements.Should().BeEmpty();
    }

    [Fact]
    public void SelectMove_AfterUndo_OriginalPositionRestored()
    {
        // Setup: Draw a stroke
        var action1 = Guid.NewGuid();
        var stroke1 = Guid.NewGuid();
        _sut.ApplyEvent(new StartStrokeEvent(action1, stroke1, new SKPoint(10f, 10f), DefaultPaint(), ToolType.Pencil,
            []));
        _sut.ApplyEvent(new PencilStrokeLineToEvent(action1, stroke1, new SKPoint(50f, 50f)));
        _sut.ApplyEvent(new EndStrokeEvent(action1));

        // Capture initial position (Midpoint)
        var stroke = (DrawStroke)_sut.CanvasElements.First(e => e.Id == stroke1);
        var initialMidpoint = new SKPoint(stroke.Path.TightBounds.MidX, stroke.Path.TightBounds.MidY);

        // Act 1: Select the stroke
        var action2 = Guid.NewGuid();
        var boundId = Guid.NewGuid();
        _sut.ApplyEvent(new CreateSelectionBoundEvent(action2, boundId, new SKPoint(0f, 0f)));
        _sut.ApplyEvent(new IncreaseSelectionBoundEvent(action2, boundId, new SKPoint(1000f, 1000f)));
        _sut.ApplyEvent(new EndSelectionEvent(action2, boundId));

        // Act 2: Move the stroke
        var action3 = Guid.NewGuid();
        var translation = new SKPoint(100f, 100f);
        _sut.ApplyEvent(new MoveCanvasElementsEvent(action3, boundId, translation));

        // Assert Step 1: Stroke moved
        stroke = (DrawStroke)_sut.CanvasElements.First(e => e.Id == stroke1);
        var movedMidpoint = new SKPoint(stroke.Path.TightBounds.MidX, stroke.Path.TightBounds.MidY);
        movedMidpoint.X.Should().BeApproximately(initialMidpoint.X + translation.X, 1f);

        // Act 3: Undo the move (action3)
        _sut.Undo();

        // Assert Step 2: Stroke returned to initial position
        stroke = (DrawStroke)_sut.CanvasElements.First(e => e.Id == stroke1);
        var restoredMidpoint = new SKPoint(stroke.Path.TightBounds.MidX, stroke.Path.TightBounds.MidY);
        restoredMidpoint.X.Should().BeApproximately(initialMidpoint.X, 1f);

        // Act 4: Undo the selection (action2)
        _sut.Undo();

        // Assert Step 3: Selection is cleared
        _sut.ActiveSelectionBoundId.Should().BeNull();
        _sut.GetSelectedElements().Should().BeEmpty();
    }

    [Fact]
    public void RemoteEvent_DoesNotPushUndoStack()
    {
        // Setup: Remote event arrives from Hub
        var action1 = Guid.NewGuid();
        var stroke1 = Guid.NewGuid();
        var startEvent =
            new StartStrokeEvent(action1, stroke1, new SKPoint(0f, 0f), DefaultPaint(), ToolType.Pencil, []);
        var lineEvent = new PencilStrokeLineToEvent(action1, stroke1, new SKPoint(10f, 10f));
        var endEvent = new EndStrokeEvent(action1);

        // Act 1: Apply remote stroke
        _sut.ApplyEvent(startEvent, false);
        _sut.ApplyEvent(lineEvent, false);
        _sut.ApplyEvent(endEvent, false);

        // Assert Step 1: Canvas has the element
        _sut.CanvasElements.Should().HaveCount(1);
        _sut.CanvasElements[0].Id.Should().Be(stroke1);

        // Act 2: Try to Undo
        _sut.Undo();

        // Assert Step 2: Element remains because remote events do not populate our undo stack
        _sut.CanvasElements.Should().HaveCount(1);
    }
}