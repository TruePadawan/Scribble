using FluentAssertions;
using NSubstitute;
using Scribble.Services.CanvasStateService;
using Scribble.Services.MultiUserDrawing;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using Scribble.Shared.Lib.Events;
using SkiaSharp;

namespace Scribble.UnitTests.ServiceTests;

/// <summary>
/// Compare live editing, snapshot installation and checkpoint replay using identical event identities.
/// </summary>
public class CanvasReplayParityTests
{
    // Every concrete wire event needs a canvas mutation or a history visibility rule.
    [Fact]
    public void Dispatcher_HandlesEveryConcreteDrawingEvent()
    {
        var dispatcher = new EventReplayDispatcher();
        var events = typeof(Event).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(Event)) && !t.IsAbstract);
        events.Where(type => !dispatcher.Handles(type)).Should().BeEmpty();
    }

    // Replaying an empty marquee must preserve its effect on the previous selection.
    [Fact]
    public void EmptySelection_AfterAnotherRebuild_PreviousSelectionStaysCleared()
    {
        var canvas = new CanvasStateService(Substitute.For<IMultiUserDrawingService>());
        var image = Guid.NewGuid();
        var empty = Guid.NewGuid();
        var bound = Guid.NewGuid();
        canvas.ApplyEvent(new AddImageEvent(Guid.NewGuid(), image, "", SKPoint.Empty, new SKSize(5, 5)));
        canvas.ApplyEvent(new SelectByIdsEvent(Guid.NewGuid(), Guid.NewGuid(), [image]));
        canvas.ApplyEvent(new CreateSelectionBoundEvent(empty, bound, new SKPoint(90, 90)));
        canvas.ApplyEvent(new EndSelectionEvent(empty, bound));
        canvas.ApplyEvent(new SetElementLayerEvent(Guid.NewGuid(), [image], 1));
        canvas.SelectedElementIds.Should().BeEmpty();
        canvas.ActiveSelectionBoundId.Should().BeNull();
    }

    // Tied layers, stale gestures and local undo must survive changing the replay starting point.
    [Fact]
    public void MixedHistory_LiveFullAndCheckpointReplay_ProduceEquivalentBoards()
    {
        var live = new CanvasStateService(Substitute.For<IMultiUserDrawingService>(), int.MaxValue);
        var checkpointed = new CanvasStateService(Substitute.For<IMultiUserDrawingService>(), 2);
        var room = Substitute.For<IMultiUserDrawingService>();
        var joined = new CanvasStateService(room);
        var image = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var stroke = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var draw = Guid.NewGuid();
        var bound = Guid.NewGuid();
        var erase = Guid.NewGuid();
        var eraser = Guid.NewGuid();
        var boundary = new SetElementLayerEvent(Guid.NewGuid(), [image, stroke], 3);
        var move = new MoveCanvasElementsEvent(Guid.NewGuid(), bound, new SKPoint(2, 4));
        Event[] events =
        [
            new StartStrokeEvent(draw, stroke, SKPoint.Empty, new StrokePaint(), ToolType.Pencil, []),
            new PencilStrokeLineToEvent(draw, stroke, new SKPoint(3, 5)),
            new EndStrokeEvent(draw),
            new AddImageEvent(Guid.NewGuid(), image, "", new SKPoint(20, 20), new SKSize(5, 5)),
            new SelectByIdsEvent(Guid.NewGuid(), bound, [stroke, image]),
            new StartEraseStrokeEvent(erase, eraser, new SKPoint(90, 90)),
            new TriggerEraseEvent(erase, eraser),
            boundary,
            move, move,
            new RotateCanvasElementsEvent(Guid.NewGuid(), bound, 0.5f, SKPoint.Empty)
        ];
        foreach (var ev in events)
        {
            live.ApplyEvent(ev);
            checkpointed.ApplyEvent(ev);
        }

        checkpointed.LastReplayCheckpointId.Should().NotBeNull();
        live.CanvasElements.Select(x => x.Id).Should().Equal(image, stroke);
        live.CanvasEvents.Count.Should().Be(events.Length - 1);
        CompareBoards();
        live.Undo();
        checkpointed.Undo();
        live.CanRedo.Should().BeTrue();
        checkpointed.CanRedo.Should().BeTrue();
        CompareBoards();
        live.Redo();
        checkpointed.Redo();
        CompareBoards();

        // A new joiner rebuilds visible history from scratch without inheriting local undo ownership.
        void CompareBoards()
        {
            room.RoomCanvasReady += Raise.Event<Action<Queue<Event>?, IReadOnlyList<Event>>>(
                live.CanvasEvents, Array.Empty<Event>());
            foreach (var other in new[] { checkpointed, joined })
            {
                CanvasContent.Equals(live.CanvasElements, other.CanvasElements).Should().BeTrue();
                other.SelectedElementIds.Should().Equal(live.SelectedElementIds);
                other.ActiveSelectionBoundId.Should().Be(live.ActiveSelectionBoundId);
                var expected = live.CanvasElements.OfType<DrawStroke>().Single();
                other.CanvasElements.OfType<DrawStroke>().Single().RawPoints.Should().Equal(expected.RawPoints);
            }
            joined.CanUndo.Should().BeFalse();
            checkpointed.CanUndo.Should().Be(live.CanUndo);
            checkpointed.CanRedo.Should().Be(live.CanRedo);
        }
    }
}
