using Avalonia.Skia;
using FluentAssertions;
using NSubstitute;
using Scribble.Services.CanvasStateService;
using Scribble.Services.MultiUserDrawing;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using SkiaSharp;

namespace Scribble.UnitTests.ServiceTests;

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

    // Selection: Transform operations
    // These all go through the fast-path and require a bound + stroke already tracked
    // in _selectionBoundLookup. ApplySelectionBound sets that up.

    private static SKPoint GetStrokeMidpoint(CanvasStateService canvasStateService, Guid strokeId)
    {
        var stroke = (DrawStroke)canvasStateService.CanvasElements.First(e => e.Id == strokeId);
        return new SKPoint(stroke.Path.TightBounds.MidX, stroke.Path.TightBounds.MidY);
    }

    [Fact]
    public void ApplyEvent_MoveCanvasElements_StrokePathIsTranslated()
    {
        var (_, strokeId) = ApplyCompleteStroke(_canvasStateService,
            new SKPoint(10f, 10f), new SKPoint(50f, 50f));
        var (boundId, _) = ApplySelectionBound(_canvasStateService,
            new SKPoint(0f, 0f), new SKPoint(1000f, 1000f));

        var before = GetStrokeMidpoint(_canvasStateService, strokeId);
        var delta = new SKPoint(100f, 50f);
        _canvasStateService.ApplyEvent(new MoveCanvasElementsEvent(Guid.NewGuid(), boundId, delta));

        var after = GetStrokeMidpoint(_canvasStateService, strokeId);
        after.X.Should().BeApproximately(before.X + delta.X, precision: 1f);
        after.Y.Should().BeApproximately(before.Y + delta.Y, precision: 1f);
    }

    [Fact]
    public void ApplyEvent_RotateCanvasElements_StrokeBoundsRotateAroundCenter()
    {
        var (_, strokeId) = ApplyCompleteStroke(_canvasStateService,
            new SKPoint(100f, 0f), new SKPoint(200f, 0f));
        var (boundId, _) = ApplySelectionBound(_canvasStateService,
            new SKPoint(0f, 0f), new SKPoint(1000f, 1000f));

        var before = GetStrokeMidpoint(_canvasStateService, strokeId);
        // Rotate 180° around the stroke midpoint, path should end up at approximately the same spot
        // because it's rotating around its own centre, but let's just verify the path changed
        var center = new SKPoint(150f, 0f);
        _canvasStateService.ApplyEvent(new RotateCanvasElementsEvent(
            Guid.NewGuid(), boundId, DegreesRad: MathF.PI, Center: center));

        var after = GetStrokeMidpoint(_canvasStateService, strokeId);
        // After 180° rotation around (150, 0), midpoint at (150, 0) stays at (150, 0)
        after.X.Should().BeApproximately(before.X, precision: 1f);
        after.Y.Should().BeApproximately(before.Y, precision: 1f);
    }

    [Fact]
    public void ApplyEvent_ScaleCanvasElements_StrokeBoundsScaleAroundCenter()
    {
        var (_, strokeId) = ApplyCompleteStroke(_canvasStateService,
            new SKPoint(100f, 100f), new SKPoint(200f, 100f));
        var (boundId, _) = ApplySelectionBound(_canvasStateService,
            new SKPoint(0f, 0f), new SKPoint(1000f, 1000f));

        var stroke = (DrawStroke)_canvasStateService.CanvasElements.First(e => e.Id == strokeId);
        var widthBefore = stroke.Path.TightBounds.Width;

        // Scale by 2x around origin
        _canvasStateService.ApplyEvent(new ScaleCanvasElementsEvent(
            Guid.NewGuid(), boundId,
            Scale: new SKPoint(2f, 2f),
            Center: new SKPoint(0f, 0f)));

        var widthAfter = stroke.Path.TightBounds.Width;
        widthAfter.Should().BeApproximately(widthBefore * 2f, precision: 1f);
    }

    // Layer Management
    // After replay, layer indices are normalized to contiguous 0..N-1.
    // Two strokes created sequentially share layer 0, then normalization maps them both to 0.
    // SetElementLayerEvent changes the raw value before normalization.
    // NudgeElementLayerEvent adds an offset to the existing value.

    [Fact]
    public void ApplyEvent_SetElementLayer_ElementLayerIndexChanges()
    {
        var (_, strokeId) = ApplyCompleteStroke(_canvasStateService);

        _canvasStateService.ApplyEvent(new SetElementLayerEvent(
            Guid.NewGuid(), [strokeId], NewLayerIndex: 5));

        // Only one element, so normalization maps layer 5 → 0
        _canvasStateService.CanvasElements[0].LayerIndex.Should().Be(0);
    }

    [Fact]
    public void ApplyEvent_SetElementLayer_ChangesRelativeOrder()
    {
        // stroke A created first (layer 0), stroke B second (also layer 0 at creation)
        var (_, strokeA) = ApplyCompleteStroke(_canvasStateService,
            new SKPoint(0f, 0f), new SKPoint(10f, 10f));
        var (_, strokeB) = ApplyCompleteStroke(_canvasStateService,
            new SKPoint(100f, 100f), new SKPoint(110f, 110f));

        // Move A above B
        _canvasStateService.ApplyEvent(new SetElementLayerEvent(
            Guid.NewGuid(), [strokeA], NewLayerIndex: 10));

        // After normalization: B stays at 0, A moves to 1
        var elementA = _canvasStateService.CanvasElements.First(e => e.Id == strokeA);
        var elementB = _canvasStateService.CanvasElements.First(e => e.Id == strokeB);
        elementA.LayerIndex.Should().BeGreaterThan(elementB.LayerIndex);
    }

    [Fact]
    public void ApplyEvent_NudgeElementLayer_ElementLayerIndexShifts()
    {
        var (_, strokeA) = ApplyCompleteStroke(_canvasStateService,
            new SKPoint(0f, 0f), new SKPoint(10f, 10f));
        var (_, strokeB) = ApplyCompleteStroke(_canvasStateService,
            new SKPoint(100f, 100f), new SKPoint(110f, 110f));

        // Nudge B up by 1
        _canvasStateService.ApplyEvent(new NudgeElementLayerEvent(
            Guid.NewGuid(), [strokeB], Offset: 1));

        var elementA = _canvasStateService.CanvasElements.First(e => e.Id == strokeA);
        var elementB = _canvasStateService.CanvasElements.First(e => e.Id == strokeB);
        elementB.LayerIndex.Should().BeGreaterThan(elementA.LayerIndex);
    }

    [Fact]
    public void LayerNormalization_IndicesAreContiguousStartingFromZero()
    {
        var (_, strokeA) = ApplyCompleteStroke(_canvasStateService,
            new SKPoint(0f, 0f), new SKPoint(10f, 10f));
        var (_, strokeB) = ApplyCompleteStroke(_canvasStateService,
            new SKPoint(100f, 100f), new SKPoint(110f, 110f));

        // Set A to layer 100, far above B
        _canvasStateService.ApplyEvent(new SetElementLayerEvent(
            Guid.NewGuid(), [strokeA], NewLayerIndex: 100));

        var layers = _canvasStateService.CanvasElements
            .Select(e => e.LayerIndex)
            .OrderBy(l => l)
            .ToList();

        // Should be [0, 1] not [0, 100]
        layers.Should().BeEquivalentTo([0, 1]);
    }

    // Canvas Lifecycle, Events and BackgroundColor

    [Fact]
    public void SetBackgroundColor_ChangesBackgroundColor()
    {
        _canvasStateService.SetBackgroundColor(SKColors.Red);

        _canvasStateService.BackgroundColor.Should().Be(SKColors.Red);
    }

    [Fact]
    public void SetBackgroundColor_SameColor_DoesNotFireEvent()
    {
        var defaultColor = _canvasStateService.BackgroundColor;
        var fired = false;
        _canvasStateService.BackgroundColorChanged += () => fired = true;

        _canvasStateService.SetBackgroundColor(defaultColor);

        fired.Should().BeFalse();
    }

    [Fact]
    public void SetBackgroundColor_DifferentColor_FiresBackgroundColorChanged()
    {
        var fired = false;
        _canvasStateService.BackgroundColorChanged += () => fired = true;

        _canvasStateService.SetBackgroundColor(SKColors.Green);

        fired.Should().BeTrue();
    }

    [Fact]
    public void ApplyEvent_StartStroke_FiresCanvasInvalidated()
    {
        var fired = false;
        _canvasStateService.CanvasInvalidated += () => fired = true;

        ApplyStartStroke(_canvasStateService);

        fired.Should().BeTrue();
    }

    [Fact]
    public void Undo_FiresUndoRedoStateChanged()
    {
        ApplyCompleteStroke(_canvasStateService);
        var fired = false;
        _canvasStateService.UndoRedoStateChanged += () => fired = true;

        _canvasStateService.Undo();

        fired.Should().BeTrue();
    }

    [Fact]
    public void Redo_FiresUndoRedoStateChanged()
    {
        ApplyCompleteStroke(_canvasStateService);
        _canvasStateService.Undo();
        var fired = false;
        _canvasStateService.UndoRedoStateChanged += () => fired = true;

        _canvasStateService.Redo();

        fired.Should().BeTrue();
    }

    // Network Integration
    // ApplyEvent stamps CreatorConnectionId and calls BroadcastEventAsync when Room is non-null.

    private static MultiUserDrawingRoom MakeRoom(string connectionId = "conn-1") =>
        new MultiUserDrawingRoom("room-1", connectionId, "Alice")
        {
            Clients = [new MultiUserDrawingClient(connectionId, "Alice")]
        };

    [Fact]
    public async Task ApplyEvent_WhenRoomIsNull_BroadcastEventAsyncIsNotCalled()
    {
        _multiUserDrawingService.Room.Returns((MultiUserDrawingRoom?)null);

        ApplyStartStroke(_canvasStateService);

        await _multiUserDrawingService.DidNotReceive().BroadcastEventAsync(Arg.Any<Event>());
    }

    [Fact]
    public async Task ApplyEvent_WhenRoomIsNonNull_BroadcastEventAsyncIsCalled()
    {
        var room = MakeRoom();
        _multiUserDrawingService.Room.Returns(room);

        ApplyStartStroke(_canvasStateService);

        await _multiUserDrawingService.Received(1).BroadcastEventAsync(Arg.Any<Event>());
    }

    [Fact]
    public async Task ApplyEvent_WhenInRoom_CreatorConnectionIdIsStampedOnEvent()
    {
        const string myConnectionId = "conn-abc";
        var room = MakeRoom(myConnectionId);
        _multiUserDrawingService.Room.Returns(room);

        _canvasStateService.ApplyEvent(new StartStrokeEvent(
            Guid.NewGuid(), Guid.NewGuid(), SKPoint.Empty, DefaultPaint(), ToolType.Pencil, []));

        await _multiUserDrawingService.Received(1).BroadcastEventAsync(
            Arg.Is<Event>(e => e.CreatorConnectionId == myConnectionId));
    }
}