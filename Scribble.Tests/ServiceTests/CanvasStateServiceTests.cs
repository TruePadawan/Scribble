using Avalonia.Skia;
using FluentAssertions;
using NSubstitute;
using Scribble.Services.CanvasStateService;
using Scribble.Services.MultiUserDrawing;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using SkiaSharp;

namespace Scribble.Tests.ServiceTests;

public class CanvasStateServiceTests
{
    private readonly IMultiUserDrawingService _multiUserDrawingService;
    private readonly CanvasStateService _canvasStateService;

    public CanvasStateServiceTests()
    {
        _multiUserDrawingService = Substitute.For<IMultiUserDrawingService>();
        _multiUserDrawingService.Room.Returns((MultiUserDrawingRoom?)null);
        _canvasStateService = new CanvasStateService(_multiUserDrawingService);
    }

    private static StrokePaint DefaultPaint() => new() { Color = SKColors.Black, StrokeWidth = 2f };

    private static (Guid actionId, Guid strokeId) ApplyStartStroke(
        CanvasStateService sut, SKPoint? startPoint = null)
    {
        var actionId = Guid.NewGuid();
        var strokeId = Guid.NewGuid();
        sut.ApplyEvent(new StartStrokeEvent(
            actionId, strokeId, startPoint ?? new SKPoint(0f, 0f),
            DefaultPaint(), ToolType.Pencil, []));
        return (actionId, strokeId);
    }

    // StartStrokeEvent

    [Fact]
    public void ApplyEvent_StartStroke_CanvasElementsContainsOneElement()
    {
        ApplyStartStroke(_canvasStateService);

        _canvasStateService.CanvasElements.Should().HaveCount(1);
    }

    [Fact]
    public void ApplyEvent_StartStroke_HasEventsIsTrue()
    {
        ApplyStartStroke(_canvasStateService);

        _canvasStateService.HasEvents.Should().BeTrue();
    }

    [Fact]
    public void ApplyEvent_StartStroke_GetSelectedElementsIsEmpty()
    {
        ApplyStartStroke(_canvasStateService);

        _canvasStateService.GetSelectedElements().Should().BeEmpty();
    }

    [Fact]
    public void ApplyEvent_StartStroke_ElementIsDrawStroke()
    {
        ApplyStartStroke(_canvasStateService);

        _canvasStateService.CanvasElements[0].Should().BeOfType<DrawStroke>();
    }

    [Fact]
    public void ApplyEvent_StartStroke_StrokePaintColorMatchesEvent()
    {
        var paint = new StrokePaint { Color = SKColors.Red, StrokeWidth = 5f };
        var strokeId = Guid.NewGuid();
        _canvasStateService.ApplyEvent(new StartStrokeEvent(
            Guid.NewGuid(), strokeId, SKPoint.Empty, paint, ToolType.Pencil, []));

        var stroke = (DrawStroke)_canvasStateService.CanvasElements[0];
        stroke.Paint.Color.Should().Be(SKColors.Red);
        stroke.Paint.StrokeWidth.Should().Be(5f);
    }

    // PencilStrokeLineToEvent

    [Fact]
    public void ApplyEvent_PencilStrokeLineTo_StrokePathHasAdditionalPoints()
    {
        var (_, strokeId) = ApplyStartStroke(_canvasStateService, new SKPoint(0f, 0f));

        _canvasStateService.ApplyEvent(new PencilStrokeLineToEvent(Guid.NewGuid(), strokeId, new SKPoint(50f, 50f)));

        var stroke = (DrawStroke)_canvasStateService.CanvasElements[0];
        stroke.Path.PointCount.Should().BeGreaterThan(1);
    }

    [Fact]
    public void ApplyEvent_MultiplePencilStrokeLineTo_PathPointCountGrowsWithEachPoint()
    {
        var (_, strokeId) = ApplyStartStroke(_canvasStateService, new SKPoint(0f, 0f));

        _canvasStateService.ApplyEvent(new PencilStrokeLineToEvent(Guid.NewGuid(), strokeId, new SKPoint(10f, 10f)));
        var countAfterFirst = ((DrawStroke)_canvasStateService.CanvasElements[0]).Path.PointCount;

        _canvasStateService.ApplyEvent(new PencilStrokeLineToEvent(Guid.NewGuid(), strokeId, new SKPoint(20f, 20f)));
        var countAfterSecond = ((DrawStroke)_canvasStateService.CanvasElements[0]).Path.PointCount;

        countAfterSecond.Should().BeGreaterThanOrEqualTo(countAfterFirst);
    }

    // LineStrokeLineToEvent

    [Fact]
    public void ApplyEvent_LineStrokeLineTo_StrokePathContainsEndpoint()
    {
        var (_, strokeId) = ApplyStartStroke(_canvasStateService, new SKPoint(0f, 0f));
        var endpoint = new SKPoint(200f, 100f);

        _canvasStateService.ApplyEvent(new LineStrokeLineToEvent(Guid.NewGuid(), strokeId, endpoint));

        var stroke = (DrawStroke)_canvasStateService.CanvasElements[0];
        stroke.Path.Points.Should().Contain(p =>
            Math.Abs(p.X - endpoint.X) < Math.Pow(10d, -5d) && Math.Abs(p.Y - endpoint.Y) < Math.Pow(10d, -5d));
    }

    // AddTextEvent

    [Fact]
    public void ApplyEvent_AddText_CanvasElementsContainsTextStroke()
    {
        var textId = Guid.NewGuid();
        _canvasStateService.ApplyEvent(new AddTextEvent(
            Guid.NewGuid(), textId,
            new SKPoint(50f, 50f), "Hello",
            new StrokePaint { TextSize = 24f }, []));

        _canvasStateService.CanvasElements.Should().HaveCount(1);
        _canvasStateService.CanvasElements[0].Should().BeOfType<TextStroke>();
    }

    [Fact]
    public void ApplyEvent_AddText_TextStrokeTextMatchesEvent()
    {
        var textId = Guid.NewGuid();
        _canvasStateService.ApplyEvent(new AddTextEvent(
            Guid.NewGuid(), textId,
            new SKPoint(0f, 0f), "Scribble",
            new StrokePaint { TextSize = 16f }, []));

        var textStroke = (TextStroke)_canvasStateService.CanvasElements[0];
        textStroke.Text.Should().Be("Scribble");
    }

    [Fact]
    public void ApplyEvent_AddText_TextStrokePositionMatchesEvent()
    {
        var position = new SKPoint(100f, 200f);
        _canvasStateService.ApplyEvent(new AddTextEvent(
            Guid.NewGuid(), Guid.NewGuid(),
            position, "Hi",
            new StrokePaint { TextSize = 12f }, []));

        var textStroke = (TextStroke)_canvasStateService.CanvasElements[0];
        textStroke.Position.X.Should().BeApproximately(position.X, precision: 0.01f);
        textStroke.Position.Y.Should().BeApproximately(position.Y, precision: 0.01f);
    }

    // LoadCanvasEvent via LoadCanvas()

    [Fact]
    public void LoadCanvas_EmptyList_CanvasElementsIsEmpty()
    {
        ApplyStartStroke(_canvasStateService);

        _canvasStateService.LoadCanvas([]);

        _canvasStateService.CanvasElements.Should().BeEmpty();
    }

    [Fact]
    public void LoadCanvas_WithElements_CanvasElementsMatchCount()
    {
        var stroke1Id = Guid.NewGuid();
        var stroke2Id = Guid.NewGuid();
        var path = new SKPath();
        path.MoveTo(0f, 0f);
        path.LineTo(10f, 10f);

        var elements = new List<CanvasElement>
        {
            new DrawStroke
            {
                Id = stroke1Id, ToolType = ToolType.Pencil, LayerIndex = 0,
                Path = path.Clone(), Paint = DefaultPaint(), ToolOptions = []
            },
            new DrawStroke
            {
                Id = stroke2Id, ToolType = ToolType.Pencil, LayerIndex = 0,
                Path = path.Clone(), Paint = DefaultPaint(), ToolOptions = []
            }
        };

        _canvasStateService.LoadCanvas(elements);

        _canvasStateService.CanvasElements.Should().HaveCount(2);
    }

    [Fact]
    public void LoadCanvas_AfterStrokes_UndoStackIsCleared()
    {
        ApplyStartStroke(_canvasStateService);
        _canvasStateService.ApplyEvent(new EndStrokeEvent(Guid.NewGuid()));

        _canvasStateService.LoadCanvas([]);

        _canvasStateService.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void LoadCanvas_AfterUndoableAction_RedoStackIsCleared()
    {
        ApplyStartStroke(_canvasStateService);
        _canvasStateService.ApplyEvent(new EndStrokeEvent(Guid.NewGuid()));
        _canvasStateService.Undo();

        _canvasStateService.LoadCanvas([]);

        _canvasStateService.CanRedo.Should().BeFalse();
    }

    // Undo / Redo
    // All events in one stroke share the same ActionId
    // The UndoEvent uses that ActionId to hide Start, LineTo, and End together during replay.

    private static (Guid actionId, Guid strokeId) ApplyCompleteStroke(
        CanvasStateService sut, SKPoint? start = null, SKPoint? end = null)
    {
        var actionId = Guid.NewGuid();
        var strokeId = Guid.NewGuid();
        sut.ApplyEvent(new StartStrokeEvent(
            actionId, strokeId, start ?? SKPoint.Empty, DefaultPaint(), ToolType.Pencil, []));
        sut.ApplyEvent(new PencilStrokeLineToEvent(actionId, strokeId, end ?? new SKPoint(50f, 50f)));
        sut.ApplyEvent(new EndStrokeEvent(actionId));
        return (actionId, strokeId);
    }

    [Fact]
    public void CanUndo_AfterTerminalEvent_IsTrue()
    {
        ApplyCompleteStroke(_canvasStateService);

        _canvasStateService.CanUndo.Should().BeTrue();
    }

    [Fact]
    public void CanRedo_BeforeAnyUndo_IsFalse()
    {
        ApplyCompleteStroke(_canvasStateService);

        _canvasStateService.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void Undo_AfterOneStroke_CanvasElementsIsEmpty()
    {
        ApplyCompleteStroke(_canvasStateService);

        _canvasStateService.Undo();

        _canvasStateService.CanvasElements.Should().BeEmpty();
    }

    [Fact]
    public void Undo_AfterOneStroke_CanRedoBecomesTrue()
    {
        ApplyCompleteStroke(_canvasStateService);

        _canvasStateService.Undo();

        _canvasStateService.CanRedo.Should().BeTrue();
    }

    [Fact]
    public void Undo_AfterOneStroke_CanUndoBecomesFalse()
    {
        ApplyCompleteStroke(_canvasStateService);

        _canvasStateService.Undo();

        _canvasStateService.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void Redo_AfterUndo_CanvasElementsCountIsRestored()
    {
        ApplyCompleteStroke(_canvasStateService);
        _canvasStateService.Undo();

        _canvasStateService.Redo();

        _canvasStateService.CanvasElements.Should().HaveCount(1);
    }

    [Fact]
    public void Redo_AfterUndo_CanRedoBecomesFalse()
    {
        ApplyCompleteStroke(_canvasStateService);
        _canvasStateService.Undo();

        _canvasStateService.Redo();

        _canvasStateService.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void Undo_AfterTwoStrokes_OnlyFirstStrokeRemainsVisible()
    {
        ApplyCompleteStroke(_canvasStateService);
        ApplyCompleteStroke(_canvasStateService);

        _canvasStateService.Undo();

        _canvasStateService.CanvasElements.Should().HaveCount(1);
    }

    [Fact]
    public void Undo_WhenStackIsEmpty_DoesNotThrow()
    {
        var act = () => _canvasStateService.Undo();

        act.Should().NotThrow();
    }

    [Fact]
    public void Redo_WhenStackIsEmpty_DoesNotThrow()
    {
        var act = () => _canvasStateService.Redo();

        act.Should().NotThrow();
    }

    [Fact]
    public void Undo_WhenStackIsEmpty_CanvasElementsUnchanged()
    {
        ApplyCompleteStroke(_canvasStateService);

        _canvasStateService.Undo(); // empties the stack
        _canvasStateService.Undo(); // no-op

        _canvasStateService.CanvasElements.Should().BeEmpty();
    }

    [Fact]
    public void ApplyEvent_AfterUndo_ClearsRedoStack()
    {
        ApplyCompleteStroke(_canvasStateService);
        _canvasStateService.Undo();

        // Drawing a new stroke after an undo should wipe the redo stack
        ApplyCompleteStroke(_canvasStateService);

        _canvasStateService.CanRedo.Should().BeFalse();
    }

    // Erasing
    // CheckAndErase marks a stroke as erased when the eraser point is inside Path.Contains()
    // OR within 10px of the line segment between the path's first and last points.
    // Placing the eraser exactly at the stroke's start point is a reliable hit.

    private static (Guid actionId, Guid strokeId) ApplyCompleteStroke(
        CanvasStateService sut, SKPoint start, SKPoint end)
    {
        var actionId = Guid.NewGuid();
        var strokeId = Guid.NewGuid();
        sut.ApplyEvent(new StartStrokeEvent(actionId, strokeId, start, DefaultPaint(), ToolType.Pencil, []));
        sut.ApplyEvent(new PencilStrokeLineToEvent(actionId, strokeId, end));
        sut.ApplyEvent(new EndStrokeEvent(actionId));
        return (actionId, strokeId);
    }

    private static void ApplyCompleteErase(CanvasStateService sut, Guid strokeId, SKPoint erasePoint)
    {
        var actionId = Guid.NewGuid();
        sut.ApplyEvent(new StartEraseStrokeEvent(actionId, strokeId, erasePoint));
        sut.ApplyEvent(new TriggerEraseEvent(actionId, strokeId));
    }

    [Fact]
    public void ApplyEvent_EraseAtStrokePosition_StrokeIsRemovedFromCanvas()
    {
        var strokeStart = new SKPoint(0f, 0f);
        ApplyCompleteStroke(_canvasStateService, strokeStart, new SKPoint(100f, 0f));

        ApplyCompleteErase(_canvasStateService, Guid.NewGuid(), strokeStart);

        _canvasStateService.CanvasElements.Should().BeEmpty();
    }

    [Fact]
    public void ApplyEvent_EraseInEmptySpace_CanvasElementsUnchanged()
    {
        ApplyCompleteStroke(_canvasStateService,
            new SKPoint(500f, 500f), new SKPoint(600f, 500f));

        // Erase far from the stroke — TriggerEraseEvent will have no targets and become stale
        ApplyCompleteErase(_canvasStateService, Guid.NewGuid(), new SKPoint(0f, 0f));

        _canvasStateService.CanvasElements.Should().HaveCount(1);
    }

    [Fact]
    public void ApplyEvent_EraseInEmptySpace_CanUndoRemainsTrue()
    {
        ApplyCompleteStroke(_canvasStateService,
            new SKPoint(500f, 500f), new SKPoint(600f, 500f));

        ApplyCompleteErase(_canvasStateService, Guid.NewGuid(), new SKPoint(0f, 0f));

        // Stale erase does not push onto the undo stack; the stroke's EndStroke is still top
        _canvasStateService.CanUndo.Should().BeTrue();
    }

    [Fact]
    public void Undo_AfterSuccessfulErase_StrokeIsRestored()
    {
        var strokeStart = new SKPoint(0f, 0f);
        ApplyCompleteStroke(_canvasStateService, strokeStart, new SKPoint(100f, 0f));
        ApplyCompleteErase(_canvasStateService, Guid.NewGuid(), strokeStart);
        _canvasStateService.CanvasElements.Should().BeEmpty();

        _canvasStateService.Undo();

        _canvasStateService.CanvasElements.Should().HaveCount(1);
    }

    // Selection
    // CheckAndSelect requires the selection rect to completely contain the stroke's TightBounds.
    // A rect from (0,0) -> (1000,1000) reliably encloses any stroke placed at modest coordinates.
    // ActiveSelectionBoundId and SelectedElementIds are only updated for LOCAL bounds
    // (bounds created with isLocalEvent: true, which is the default for ApplyEvent).

    private static (Guid boundId, Guid actionId) ApplySelectionBound(
        CanvasStateService sut, SKPoint from, SKPoint to)
    {
        var actionId = Guid.NewGuid();
        var boundId = Guid.NewGuid();
        sut.ApplyEvent(new CreateSelectionBoundEvent(actionId, boundId, from));
        sut.ApplyEvent(new IncreaseSelectionBoundEvent(actionId, boundId, to));
        sut.ApplyEvent(new EndSelectionEvent(actionId, boundId));
        return (boundId, actionId);
    }

    [Fact]
    public void ApplyEvent_CreateSelectionBound_ActiveSelectionBoundIdIsNotNull()
    {
        ApplyCompleteStroke(_canvasStateService,
            new SKPoint(10f, 10f), new SKPoint(50f, 50f));

        var (boundId, _) = ApplySelectionBound(_canvasStateService,
            new SKPoint(0f, 0f), new SKPoint(1000f, 1000f));

        _canvasStateService.ActiveSelectionBoundId.Should().Be(boundId);
    }

    [Fact]
    public void ApplyEvent_SelectionBoundCoversStroke_SelectedElementIdsContainsStrokeId()
    {
        var (_, strokeId) = ApplyCompleteStroke(_canvasStateService,
            new SKPoint(10f, 10f), new SKPoint(50f, 50f));

        ApplySelectionBound(_canvasStateService,
            new SKPoint(0f, 0f), new SKPoint(1000f, 1000f));

        _canvasStateService.SelectedElementIds.Should().Contain(strokeId);
    }

    [Fact]
    public void ApplyEvent_SelectionBoundCoversStroke_GetSelectedElementsReturnsStroke()
    {
        var (_, strokeId) = ApplyCompleteStroke(_canvasStateService,
            new SKPoint(10f, 10f), new SKPoint(50f, 50f));

        ApplySelectionBound(_canvasStateService,
            new SKPoint(0f, 0f), new SKPoint(1000f, 1000f));

        _canvasStateService.GetSelectedElements()
            .Should().ContainSingle(e => e.Id == strokeId);
    }

    [Fact]
    public void ApplyEvent_SelectionBoundDoesNotCoverStroke_SelectedElementIdsIsEmpty()
    {
        // Stroke is at (500,500), well outside the (0,0) -> (100,100) selection rect
        ApplyCompleteStroke(_canvasStateService,
            new SKPoint(500f, 500f), new SKPoint(600f, 500f));

        ApplySelectionBound(_canvasStateService,
            new SKPoint(0f, 0f), new SKPoint(100f, 100f));

        _canvasStateService.SelectedElementIds.Should().BeEmpty();
    }

    [Fact]
    public void ApplyEvent_ClearSelection_ActiveSelectionBoundIdIsNull()
    {
        ApplyCompleteStroke(_canvasStateService,
            new SKPoint(10f, 10f), new SKPoint(50f, 50f));
        ApplySelectionBound(_canvasStateService,
            new SKPoint(0f, 0f), new SKPoint(1000f, 1000f));

        _canvasStateService.ApplyEvent(new ClearSelectionEvent(Guid.NewGuid()));

        _canvasStateService.ActiveSelectionBoundId.Should().BeNull();
    }

    [Fact]
    public void ApplyEvent_ClearSelection_SelectedElementIdsIsEmpty()
    {
        ApplyCompleteStroke(_canvasStateService,
            new SKPoint(10f, 10f), new SKPoint(50f, 50f));
        ApplySelectionBound(_canvasStateService,
            new SKPoint(0f, 0f), new SKPoint(1000f, 1000f));

        _canvasStateService.ApplyEvent(new ClearSelectionEvent(Guid.NewGuid()));

        _canvasStateService.SelectedElementIds.Should().BeEmpty();
    }
}