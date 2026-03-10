using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Scribble.Messages;
using Scribble.Services.DialogService;
using Scribble.Shared.Lib;
using Scribble.Tools.PointerTools.ArrowTool;
using Scribble.Utils;
using SkiaSharp;

namespace Scribble.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public static int CanvasWidth => 10000;
    public static int CanvasHeight => 10000;

    private bool CanUndo => _undoStack.Count > 0;
    private bool CanRedo => _redoStack.Count > 0;

    public event Action? RequestInvalidateSelection;
    public Action? RequestInvalidateCanvas { get; set; }
    [ObservableProperty] private List<CanvasElement> _canvasElements = [];
    private readonly HashSet<Guid> _deletedActions = [];
    private Dictionary<Guid, DrawStroke> _strokeLookup = new();
    private Dictionary<Guid, EraserStroke> _eraserStrokeLookup = new();
    private Dictionary<Guid, SKPoint> _eraserHeadLookup = new();
    private Dictionary<Guid, SelectionBound> _selectionBoundLookup = new();
    private Dictionary<Guid, CanvasImage> _canvasImageLookup = new();
    public Dictionary<Guid, List<Guid>> SelectionTargets { get; private set; } = [];
    public Queue<Event> CanvasEvents { get; private set; } = [];

    private readonly IDialogService _dialogService;

    private readonly Stack<Guid> _undoStack = [];
    private readonly Stack<Guid> _redoStack = [];
    public readonly HashSet<Guid> MySelections = [];

    public MultiUserDrawingViewModel MultiUserDrawingViewModel { get; }
    public DocumentViewModel DocumentViewModel { get; }
    public UiStateViewModel UiStateViewModel { get; }
    public CanvasExportViewModel CanvasExportViewModel { get; }

    public MainViewModel(MultiUserDrawingViewModel multiplayer, DocumentViewModel documentViewModel,
        UiStateViewModel uiStateViewModel,
        IDialogService dialogService, CanvasExportViewModel canvasExportViewModel)
    {
        _dialogService = dialogService;
        CanvasExportViewModel = canvasExportViewModel;
        MultiUserDrawingViewModel = multiplayer;
        DocumentViewModel = documentViewModel;
        UiStateViewModel = uiStateViewModel;

        // Reply to requests asking if there are any canvas events
        WeakReferenceMessenger.Default.Register<MainViewModel, HasEventsRequestMessage>(this,
            (mainViewModel, message) => { message.Reply(mainViewModel.CanvasEvents.Count > 0); });

        // Listen for incoming network events
        WeakReferenceMessenger.Default.Register<NetworkEventReceivedMessage>(this,
            (r, message) => { OnNetworkEventReceived(message.Event); });

        WeakReferenceMessenger.Default.Register<CanvasStateReceivedMessage>(this,
            (r, message) => { OnCanvasStateReceived(message.Events); });

        WeakReferenceMessenger.Default.Register<CanvasStateRequestedMessage>(this,
            (r, m) =>
            {
                WeakReferenceMessenger.Default.Send(new SendCanvasStateMessage(m.TargetConnectionId, CanvasEvents));
            });

        // Send canvas data when the DocumentViewModel wants to save
        WeakReferenceMessenger.Default.Register<MainViewModel, RequestCanvasDataMessage>(this,
            (mainViewModel, message) =>
            {
                message.Reply(new CanvasDataPayload(mainViewModel.CanvasElements,
                    mainViewModel.UiStateViewModel.BackgroundColor));
            });

        // Load canvas data when the DocumentViewModel reads a file
        WeakReferenceMessenger.Default.Register<MainViewModel, LoadCanvasDataMessage>(this,
            (mainViewModel, message) =>
            {
                mainViewModel.ApplyEvent(new LoadCanvasEvent(Guid.NewGuid(), message.CanvasElements));
            });

        // Clear data when the DocumentViewModel triggers a reset
        WeakReferenceMessenger.Default.Register<MainViewModel, ClearCanvasMessage>(this,
            (mainViewModel, m) => { mainViewModel.ApplyEvent(new LoadCanvasEvent(Guid.NewGuid(), [])); });

        // Send the actively selected strokes to the recipient
        WeakReferenceMessenger.Default.Register<MainViewModel, RequestSelectedElements>(this,
            (mainViewModel, message) =>
            {
                var hasActiveSelection = mainViewModel.SelectionTargets.Count > 0;
                if (hasActiveSelection)
                {
                    var id = mainViewModel.SelectionTargets.Keys.First();
                    var selectedElementIds = mainViewModel.SelectionTargets[id];
                    List<CanvasElement> selectedElements = [];
                    foreach (var canvasElement in mainViewModel.CanvasElements)
                    {
                        if (canvasElement is DrawStroke drawStroke && selectedElementIds.Contains(drawStroke.Id))
                        {
                            selectedElements.Add(drawStroke);
                        }
                        else if (canvasElement is CanvasImage canvasImage &&
                                 selectedElementIds.Contains(canvasImage.Id))
                        {
                            selectedElements.Add(canvasImage);
                        }
                    }

                    message.Reply(new SelectedElementsPayload(selectedElements));
                }
                else
                {
                    message.Reply(new SelectedElementsPayload([]));
                }
            });
    }

    // Event handler for when another client in the room draws something
    private void OnNetworkEventReceived(Event @event)
    {
        Dispatcher.UIThread.Post(() => { ProcessEvent(@event); });
    }

    // Event handler for processing the canvas state snapshot received from the room's host
    private void OnCanvasStateReceived(Queue<Event> events)
    {
        Dispatcher.UIThread.Post(() =>
        {
            CanvasEvents = events;
            ReplayEvents();
        });
    }

    public Vector GetCanvasDimensions() => new Vector(CanvasWidth, CanvasHeight);

    private void TrackAction(Guid actionId)
    {
        _undoStack.Push(actionId);
        _redoStack.Clear();

        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_undoStack.Count == 0) return;
        var actionId = _undoStack.Pop();
        if (MultiUserDrawingViewModel.Room != null)
        {
            while (_deletedActions.Contains(actionId))
            {
                actionId = _undoStack.Pop();
            }
        }

        _redoStack.Push(actionId);
        ApplyEvent(new UndoEvent(Guid.NewGuid(), actionId), isLocalEvent: true);

        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (_redoStack.Count == 0) return;
        var actionId = _redoStack.Pop();
        if (MultiUserDrawingViewModel.Room != null)
        {
            while (_deletedActions.Contains(actionId))
            {
                actionId = _redoStack.Pop();
            }
        }

        _undoStack.Push(actionId);
        ApplyEvent(new RedoEvent(Guid.NewGuid(), actionId), isLocalEvent: true);

        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    public void ApplyEvent(Event @event, bool isLocalEvent = true)
    {
        if (@event is CreateSelectionBoundEvent ev)
        {
            MySelections.Add(ev.BoundId);
        }

        ProcessEvent(@event, isLocalEvent);

        if (MultiUserDrawingViewModel.Room != null)
        {
            var roomId = MultiUserDrawingViewModel.Room.RoomId;
            WeakReferenceMessenger.Default.Send(new BroadcastEventMessage(roomId, @event));
        }
    }

    private void ProcessEvent(Event @event, bool isLocalEvent = false)
    {
        CanvasEvents.Enqueue(@event);

        // Fast path: for pencil/line line-to events during active drawing,
        // apply directly to the existing stroke — no replay needed
        if (@event is PencilStrokeLineToEvent pencilLineToEvent)
        {
            if (_strokeLookup.TryGetValue(pencilLineToEvent.StrokeId, out var stroke))
            {
                stroke.Path.LineTo(pencilLineToEvent.Point);
                RequestInvalidateCanvas?.Invoke();
                return;
            }
        }

        if (@event is LineStrokeLineToEvent lineStrokeEvent)
        {
            if (_strokeLookup.TryGetValue(lineStrokeEvent.StrokeId, out var stroke))
            {
                RebuildLinePath(stroke, lineStrokeEvent.EndPoint);
                RequestInvalidateCanvas?.Invoke();
                return;
            }
        }

        if (@event is EraseStrokeLineToEvent eraseLineToEvent)
        {
            if (_eraserStrokeLookup.TryGetValue(eraseLineToEvent.StrokeId, out var currentEraserStroke))
            {
                var start = _eraserHeadLookup[eraseLineToEvent.StrokeId];
                var end = eraseLineToEvent.Point;
                var distance = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));

                var stepSize = 5.0;
                var steps = (int)Math.Ceiling(distance / stepSize);
                for (int s = 1; s <= steps; s++)
                {
                    var completionPercentage = s / stepSize;
                    var checkX = start.X + (end.X - start.X) * completionPercentage;
                    var checkY = start.Y + (end.Y - start.Y) * completionPercentage;
                    CheckAndErase(new SKPoint((float)checkX, (float)checkY), CanvasElements, currentEraserStroke);
                }

                currentEraserStroke.Path.LineTo(eraseLineToEvent.Point);
                _eraserHeadLookup[eraseLineToEvent.StrokeId] = eraseLineToEvent.Point;
                RequestInvalidateCanvas?.Invoke();
                return;
            }
        }

        if (@event is IncreaseSelectionBoundEvent increaseSelectionEvent)
        {
            if (_selectionBoundLookup.TryGetValue(increaseSelectionEvent.BoundId, out var bound))
            {
                var boundOrigin = bound.Path.Points[0];
                bound.Path.Reset();
                bound.Path.MoveTo(boundOrigin);
                bound.Path.LineTo(increaseSelectionEvent.Point);

                var top = Math.Min(boundOrigin.Y, increaseSelectionEvent.Point.Y);
                var left = Math.Min(boundOrigin.X, increaseSelectionEvent.Point.X);
                var boundRect = SKRect.Create(new SKPoint(left, top),
                    Utilities.GetSize(boundOrigin, increaseSelectionEvent.Point));
                CheckAndSelect(boundRect, bound, CanvasElements);

                SelectionTargets = _selectionBoundLookup
                    .Where(pair => MySelections.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value.Targets.ToList());
                RequestInvalidateSelection?.Invoke();
                return;
            }
        }

        if (@event is MoveCanvasElementsEvent moveEvent)
        {
            if (_selectionBoundLookup.TryGetValue(moveEvent.BoundId, out var bound))
            {
                foreach (var boundTargetId in bound.Targets)
                {
                    if (_strokeLookup.TryGetValue(boundTargetId, out var stroke))
                    {
                        stroke.Path.Transform(SKMatrix.CreateTranslation(moveEvent.Delta.X, moveEvent.Delta.Y));
                    }
                    else if (_canvasImageLookup.TryGetValue(boundTargetId, out var image))
                    {
                        var bounds = image.Bounds;
                        bounds.Offset(moveEvent.Delta);
                        image.Bounds = bounds;
                    }
                }

                RequestInvalidateCanvas?.Invoke();
                RequestInvalidateSelection?.Invoke();
                return;
            }
        }

        if (@event is RotateCanvasElementsEvent rotateEvent)
        {
            if (_selectionBoundLookup.TryGetValue(rotateEvent.BoundId, out var bound))
            {
                foreach (var boundTargetId in bound.Targets)
                {
                    if (_strokeLookup.TryGetValue(boundTargetId, out var stroke))
                    {
                        stroke.Path.Transform(SKMatrix.CreateRotation(rotateEvent.DegreesRad, rotateEvent.Center.X,
                            rotateEvent.Center.Y));
                    }
                    else if (_canvasImageLookup.TryGetValue(boundTargetId, out var image))
                    {
                        image.Rotation += rotateEvent.DegreesRad;
                        var imgCenter = new SKPoint(image.Bounds.MidX, image.Bounds.MidY);
                        var rotated = SKMatrix
                            .CreateRotation(rotateEvent.DegreesRad, rotateEvent.Center.X, rotateEvent.Center.Y)
                            .MapPoint(imgCenter);
                        var bounds = image.Bounds;
                        bounds.Offset(rotated.X - imgCenter.X, rotated.Y - imgCenter.Y);
                        image.Bounds = bounds;
                    }
                }

                RequestInvalidateCanvas?.Invoke();
                RequestInvalidateSelection?.Invoke();
                return;
            }
        }

        if (@event is ScaleCanvasElementsEvent scaleEvent)
        {
            if (_selectionBoundLookup.TryGetValue(scaleEvent.BoundId, out var bound))
            {
                foreach (var boundTargetId in bound.Targets)
                {
                    if (_strokeLookup.TryGetValue(boundTargetId, out var stroke))
                    {
                        stroke.Path.Transform(SKMatrix.CreateScale(scaleEvent.Scale.X, scaleEvent.Scale.Y,
                            scaleEvent.Center.X, scaleEvent.Center.Y));
                    }
                    else if (_canvasImageLookup.TryGetValue(boundTargetId, out var image))
                    {
                        var scaleMatrix = SKMatrix.CreateScale(scaleEvent.Scale.X, scaleEvent.Scale.Y,
                            scaleEvent.Center.X, scaleEvent.Center.Y);
                        var topLeft = scaleMatrix.MapPoint(new SKPoint(image.Bounds.Left, image.Bounds.Top));
                        var bottomRight =
                            scaleMatrix.MapPoint(new SKPoint(image.Bounds.Right, image.Bounds.Bottom));

                        if (topLeft.X > bottomRight.X)
                        {
                            (topLeft.X, bottomRight.X) = (bottomRight.X, topLeft.X);
                            image.FlipX = !image.FlipX;
                        }

                        if (topLeft.Y > bottomRight.Y)
                        {
                            (topLeft.Y, bottomRight.Y) = (bottomRight.Y, topLeft.Y);
                            image.FlipY = !image.FlipY;
                        }

                        image.Bounds = new SKRect(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
                    }
                }

                RequestInvalidateCanvas?.Invoke();
                RequestInvalidateSelection?.Invoke();
                return;
            }
        }

        var staleActionIds = ReplayEvents();
        bool changed = false;
        bool currentActionIsStale = false;

        Queue<Event> nonStaleEvents = [];
        foreach (var canvasEvent in CanvasEvents)
        {
            if (!staleActionIds.Contains(canvasEvent.ActionId))
            {
                nonStaleEvents.Enqueue(canvasEvent);
            }
            else
            {
                changed = true;
                if (canvasEvent.ActionId == @event.ActionId)
                {
                    currentActionIsStale = true;
                }
            }
        }

        CanvasEvents = nonStaleEvents;

        if (changed)
        {
            ReplayEvents();
        }

        // Keep track of local non-stale actions for undo/redo functionality
        if (@event is ITerminalEvent && isLocalEvent && !currentActionIsStale)
        {
            TrackAction(@event.ActionId);
        }
    }

    /// <summary>
    /// Builds the latest state of the canvas from the events in the queue
    /// </summary>
    /// <returns></returns>
    private List<Guid> ReplayEvents()
    {
        var hiddenActionIds = new HashSet<Guid>();
        foreach (var canvasEvent in CanvasEvents)
        {
            if (canvasEvent is UndoEvent ev)
            {
                hiddenActionIds.Add(ev.TargetActionId);
            }
            else if (canvasEvent is RedoEvent redoEv)
            {
                hiddenActionIds.Remove(redoEv.TargetActionId);
            }
        }

        _deletedActions.Clear();
        var drawStrokes = new Dictionary<Guid, DrawStroke>();
        var eraserStrokes = new Dictionary<Guid, EraserStroke>();
        var eraserHeads = new Dictionary<Guid, SKPoint>();
        var selectionBounds = new Dictionary<Guid, SelectionBound>();
        var staleActionIds = new List<Guid>();
        var elementIdToActionId = new Dictionary<Guid, Guid>();
        var strokeTexts = new Dictionary<Guid, string>();
        var canvasImages = new Dictionary<Guid, CanvasImage>();

        foreach (var canvasEvent in CanvasEvents.Where(canvasEvent => !hiddenActionIds.Contains(canvasEvent.ActionId)))
        {
            switch (canvasEvent)
            {
                case StartStrokeEvent ev:
                    var newLinePath = new SKPath();
                    newLinePath.MoveTo(ev.StartPoint);
                    drawStrokes[ev.StrokeId] = new DrawStroke
                    {
                        Id = ev.StrokeId,
                        Paint = ev.StrokePaint.Clone(),
                        Path = newLinePath,
                        ToolType = ev.ToolType,
                        ToolOptions = ev.ToolOptions
                    };
                    elementIdToActionId[ev.StrokeId] = ev.ActionId;
                    break;
                case StartEraseStrokeEvent ev:
                    var eraserPath = new SKPath();
                    eraserPath.MoveTo(ev.StartPoint);
                    var newEraserStroke = new EraserStroke
                    {
                        Path = eraserPath
                    };

                    // Keep track of the eraser heads for linear interpolation
                    eraserHeads[ev.StrokeId] = ev.StartPoint;

                    // Find all targets for erasing
                    CheckAndErase(ev.StartPoint, [..drawStrokes.Values, ..canvasImages.Values], newEraserStroke);

                    eraserStrokes[ev.StrokeId] = newEraserStroke;
                    break;
                case EraseStrokeLineToEvent ev:
                    if (eraserStrokes.ContainsKey(ev.StrokeId))
                    {
                        var currentEraserStroke = eraserStrokes[ev.StrokeId];
                        // Use interpolation to find all targets for erasing
                        var start = eraserHeads[ev.StrokeId];
                        var end = ev.Point;
                        var distance = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));

                        var stepSize = 5.0;
                        var steps = (int)Math.Ceiling(distance / stepSize);
                        for (int s = 1; s <= steps; s++)
                        {
                            var completionPercentage = s / stepSize;
                            var checkX = start.X + (end.X - start.X) * completionPercentage;
                            var checkY = start.Y + (end.Y - start.Y) * completionPercentage;
                            CheckAndErase(new SKPoint((float)checkX, (float)checkY),
                                [..drawStrokes.Values, ..canvasImages.Values],
                                currentEraserStroke);
                        }

                        currentEraserStroke.Path.LineTo(ev.Point);
                        eraserHeads[ev.StrokeId] = ev.Point;
                    }

                    break;
                case TriggerEraseEvent ev:
                    if (eraserStrokes.ContainsKey(ev.StrokeId))
                    {
                        // Erase all targets
                        foreach (var targetId in eraserStrokes[ev.StrokeId].Targets)
                        {
                            drawStrokes.Remove(targetId);
                            canvasImages.Remove(targetId);
                            if (elementIdToActionId.ContainsKey(targetId))
                            {
                                _deletedActions.Add(elementIdToActionId[targetId]);
                            }
                        }

                        if (eraserStrokes[ev.StrokeId].Targets.Count == 0)
                        {
                            staleActionIds.Add(ev.ActionId);
                        }
                    }

                    break;
                case PencilStrokeLineToEvent ev:
                    if (drawStrokes.ContainsKey(ev.StrokeId))
                    {
                        drawStrokes[ev.StrokeId].Path.LineTo(ev.Point);
                    }

                    break;
                case LineStrokeLineToEvent ev:
                    if (drawStrokes.ContainsKey(ev.StrokeId))
                    {
                        RebuildLinePath(drawStrokes[ev.StrokeId], ev.EndPoint);
                    }

                    break;
                case AddTextEvent ev:
                    var textPath = new SKPath();
                    textPath.MoveTo(ev.Position);
                    textPath.AddPath(
                        new SKPaint { TextSize = ev.Paint.TextSize }.GetTextPath(ev.Text, ev.Position.X,
                            ev.Position.Y));
                    drawStrokes[ev.StrokeId] = new DrawStroke
                    {
                        Id = ev.StrokeId,
                        Paint = ev.Paint.Clone(),
                        Path = textPath,
                        ToolType = ToolType.Text,
                        ToolOptions = ev.ToolOptions
                    };
                    strokeTexts[ev.StrokeId] = ev.Text;
                    elementIdToActionId[ev.StrokeId] = ev.ActionId;
                    break;
                case CreateSelectionBoundEvent ev:
                    var selectionPath = new SKPath();
                    selectionPath.MoveTo(ev.StartPoint);

                    selectionBounds.Clear();

                    var selectionBound = new SelectionBound
                    {
                        Id = ev.BoundId,
                        Path = selectionPath
                    };
                    selectionBounds[ev.BoundId] = selectionBound;
                    break;
                case IncreaseSelectionBoundEvent ev:
                    if (selectionBounds.ContainsKey(ev.BoundId))
                    {
                        var bound = selectionBounds[ev.BoundId];
                        var boundOrigin = bound.Path.Points[0];
                        bound.Path.Reset();
                        bound.Path.MoveTo(boundOrigin);
                        bound.Path.LineTo(ev.Point);

                        // Check for strokes that are within this bound
                        var top = Math.Min(boundOrigin.Y, ev.Point.Y);
                        var left = Math.Min(boundOrigin.X, ev.Point.X);
                        var boundRect = SKRect.Create(new SKPoint(left, top), Utilities.GetSize(boundOrigin, ev.Point));
                        CheckAndSelect(boundRect, bound, [..drawStrokes.Values, ..canvasImages.Values]);
                    }

                    break;
                case EndSelectionEvent ev:
                    if (selectionBounds.ContainsKey(ev.BoundId))
                    {
                        if (selectionBounds[ev.BoundId].Targets.Count == 0)
                        {
                            staleActionIds.Add(ev.ActionId);
                        }
                    }

                    break;
                case ClearSelectionEvent:
                    selectionBounds.Clear();
                    break;
                case MoveCanvasElementsEvent ev:
                    if (selectionBounds.ContainsKey(ev.BoundId))
                    {
                        var bound = selectionBounds[ev.BoundId];
                        foreach (var boundTargetId in bound.Targets)
                        {
                            if (drawStrokes.ContainsKey(boundTargetId))
                            {
                                var stroke = drawStrokes[boundTargetId];
                                stroke.Path.Transform(SKMatrix.CreateTranslation(ev.Delta.X, ev.Delta.Y));
                            }
                            else if (canvasImages.ContainsKey(boundTargetId))
                            {
                                var image = canvasImages[boundTargetId];
                                // SKRect is a struct (value-type), so we need to create a new one to modify
                                var bounds = image.Bounds;
                                bounds.Offset(ev.Delta);
                                image.Bounds = bounds;
                            }
                        }
                    }

                    break;
                case RotateCanvasElementsEvent ev:
                    if (selectionBounds.ContainsKey(ev.BoundId))
                    {
                        var bound = selectionBounds[ev.BoundId];
                        foreach (var boundTargetId in bound.Targets)
                        {
                            if (drawStrokes.ContainsKey(boundTargetId))
                            {
                                var stroke = drawStrokes[boundTargetId];
                                stroke.Path.Transform(SKMatrix.CreateRotation(ev.DegreesRad, ev.Center.X, ev.Center.Y));
                            }
                            else if (canvasImages.ContainsKey(boundTargetId))
                            {
                                var image = canvasImages[boundTargetId];
                                image.Rotation += ev.DegreesRad;

                                // Rotate the bounds center around the rotation pivot
                                var imgCenter = new SKPoint(image.Bounds.MidX, image.Bounds.MidY);
                                var rotated = SKMatrix.CreateRotation(ev.DegreesRad, ev.Center.X, ev.Center.Y)
                                    .MapPoint(imgCenter);
                                var bounds = image.Bounds;
                                bounds.Offset(rotated.X - imgCenter.X, rotated.Y - imgCenter.Y);
                                image.Bounds = bounds;
                            }
                        }
                    }

                    break;
                case ScaleCanvasElementsEvent ev:
                    if (selectionBounds.ContainsKey(ev.BoundId))
                    {
                        var bound = selectionBounds[ev.BoundId];
                        foreach (var boundTargetId in bound.Targets)
                        {
                            if (drawStrokes.ContainsKey(boundTargetId))
                            {
                                drawStrokes[boundTargetId].Path
                                    .Transform(SKMatrix.CreateScale(ev.Scale.X, ev.Scale.Y, ev.Center.X,
                                        ev.Center.Y));
                            }
                            else if (canvasImages.ContainsKey(boundTargetId))
                            {
                                var image = canvasImages[boundTargetId];
                                var scaleMatrix =
                                    SKMatrix.CreateScale(ev.Scale.X, ev.Scale.Y, ev.Center.X, ev.Center.Y);
                                var topLeft = scaleMatrix.MapPoint(new SKPoint(image.Bounds.Left, image.Bounds.Top));
                                var bottomRight =
                                    scaleMatrix.MapPoint(new SKPoint(image.Bounds.Right, image.Bounds.Bottom));

                                // If the x-axis becomes inverted, swap the x coordinates so that the bound's width stays positive
                                // Then flip the image horizontally
                                if (topLeft.X > bottomRight.X)
                                {
                                    (topLeft.X, bottomRight.X) = (bottomRight.X, topLeft.X);
                                    image.FlipX = !image.FlipX;
                                }

                                // If the y-axis becomes inverted, swap the y coordinates so that the bound's height stays positive
                                // Then flip the image vertically
                                if (topLeft.Y > bottomRight.Y)
                                {
                                    (topLeft.Y, bottomRight.Y) = (bottomRight.Y, topLeft.Y);
                                    image.FlipY = !image.FlipY;
                                }

                                image.Bounds = new SKRect(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
                            }
                        }
                    }

                    break;
                case LoadCanvasEvent ev:
                    drawStrokes.Clear();
                    canvasImages.Clear();

                    foreach (var element in ev.CanvasElements)
                    {
                        if (element is DrawStroke drawStroke)
                        {
                            drawStrokes[drawStroke.Id] = new DrawStroke
                            {
                                Id = drawStroke.Id,
                                Paint = drawStroke.Paint.Clone(),
                                ToolOptions = drawStroke.ToolOptions,
                                ToolType = drawStroke.ToolType,
                                Path = drawStroke.Path
                            };
                        }
                        else if (element is CanvasImage canvasImage)
                        {
                            canvasImages[canvasImage.Id] = new CanvasImage
                            {
                                Id = canvasImage.Id,
                                ImageBase64String = canvasImage.ImageBase64String,
                                Bounds = canvasImage.Bounds,
                                Rotation = canvasImage.Rotation,
                                FlipX = canvasImage.FlipX,
                                FlipY = canvasImage.FlipY,
                            };
                        }
                    }

                    break;
                case UpdateStrokeColorEvent ev:
                    foreach (var strokeId in ev.StrokeIds)
                    {
                        drawStrokes[strokeId].Paint.Color = ev.NewColor;
                    }

                    break;
                case UpdateStrokeThicknessEvent ev:
                    foreach (var strokeId in ev.StrokeIds)
                    {
                        drawStrokes[strokeId].Paint.StrokeWidth = ev.NewThickness;
                    }

                    break;
                case UpdateStrokeStyleEvent ev:
                    foreach (var strokeId in ev.StrokeIds)
                    {
                        drawStrokes[strokeId].Paint.DashIntervals = ev.NewDashIntervals;
                    }

                    break;
                case UpdateStrokeFillColorEvent ev:
                    foreach (var strokeId in ev.StrokeIds)
                    {
                        drawStrokes[strokeId].Paint.FillColor = ev.NewFillColor;
                    }

                    break;
                case UpdateStrokeEdgeTypeEvent ev:
                    foreach (var strokeId in ev.StrokeIds)
                    {
                        var stroke = drawStrokes[strokeId];
                        stroke.Paint.StrokeJoin = ev.NewStrokeJoin;
                        // Recreate the stroke paths, preserving any rotation

                        // Detect a rotation angle if any from the first edge of the rect/roundrect sub-path
                        var points = stroke.Path.Points;
                        var rotationAngle = (float)Math.Atan2(
                            points[2].Y - points[1].Y,
                            points[2].X - points[1].X);

                        // Un-rotate around the shape's center to recover axis-aligned dimensions
                        var center = new SKPoint(
                            stroke.Path.TightBounds.MidX,
                            stroke.Path.TightBounds.MidY);

                        using var unrotatedPath = new SKPath(stroke.Path);
                        if (Math.Abs(rotationAngle) > 0.001f)
                        {
                            unrotatedPath.Transform(
                                SKMatrix.CreateRotation(-rotationAngle, center.X, center.Y));
                        }

                        var bounds = unrotatedPath.Bounds;
                        var lineStartPoint = unrotatedPath.Points[0];
                        var lineEndPoint = new SKPoint(
                            bounds.Left + bounds.Right - lineStartPoint.X,
                            bounds.Top + bounds.Bottom - lineStartPoint.Y
                        );

                        // Rebuild the path with the new edge type
                        stroke.Path.Reset();
                        stroke.Path.MoveTo(lineStartPoint);
                        var left = Math.Min(lineStartPoint.X, lineEndPoint.X);
                        var top = Math.Min(lineStartPoint.Y, lineEndPoint.Y);
                        var rect = SKRect.Create(new SKPoint(left, top),
                            Utilities.GetSize(lineStartPoint, lineEndPoint));
                        if (stroke.Paint.StrokeJoin == SKStrokeJoin.Miter)
                        {
                            stroke.Path.AddRect(rect);
                        }
                        else
                        {
                            stroke.Path.AddRoundRect(rect, 24f, 24f);
                        }

                        // Re-apply the rotation
                        if (Math.Abs(rotationAngle) > 0.001f)
                        {
                            stroke.Path.Transform(
                                SKMatrix.CreateRotation(rotationAngle, center.X, center.Y));
                        }
                    }

                    break;
                case UpdateStrokeFontSizeEvent ev:
                    foreach (var strokeId in ev.StrokeIds)
                    {
                        // Recreate the text's paths
                        var textStroke = drawStrokes[strokeId];
                        var noTransformTextPath = new SKPath();
                        var startPoint = textStroke.Path[0];
                        noTransformTextPath.MoveTo(startPoint);
                        noTransformTextPath.AddPath(
                            new SKPaint { TextSize = ev.FontSize }.GetTextPath(strokeTexts[strokeId], startPoint.X,
                                startPoint.Y));
                        drawStrokes[strokeId].Path.Reset();
                        drawStrokes[strokeId].Path.AddPath(noTransformTextPath);
                    }

                    break;
                case AddImageEvent ev:
                    var imageBounds = SKRect.Create(ev.Position, ev.Size);
                    canvasImages[ev.ImageId] = new CanvasImage
                    {
                        Id = ev.ImageId,
                        ImageBase64String = ev.ImageBase64String,
                        Bounds = imageBounds,
                    };
                    elementIdToActionId[ev.ImageId] = ev.ActionId;
                    break;
            }
        }

        CanvasElements = [..drawStrokes.Values.ToList(), ..canvasImages.Values.ToList()];
        _strokeLookup = drawStrokes;
        _eraserStrokeLookup = eraserStrokes;
        _eraserHeadLookup = eraserHeads;
        _selectionBoundLookup = selectionBounds;
        _canvasImageLookup = canvasImages;
        // Show the selection only on the client that is doing the selection
        SelectionTargets = selectionBounds.Where(pair => MySelections.Contains(pair.Key))
            .ToDictionary(k => k.Key, v => v.Value.Targets.ToList());
        RequestInvalidateSelection?.Invoke();

        return staleActionIds;
    }

    private static void RebuildLinePath(DrawStroke stroke, SKPoint endPoint)
    {
        var lineStartPoint = stroke.Path.Points[0];
        stroke.Path.Reset();

        if (stroke.ToolType == ToolType.Rectangle)
        {
            stroke.Path.MoveTo(lineStartPoint);
            var left = Math.Min(lineStartPoint.X, endPoint.X);
            var top = Math.Min(lineStartPoint.Y, endPoint.Y);
            var rect = SKRect.Create(new SKPoint(left, top),
                Utilities.GetSize(lineStartPoint, endPoint));
            if (stroke.Paint.StrokeJoin == SKStrokeJoin.Miter)
            {
                stroke.Path.AddRect(rect);
            }
            else
            {
                stroke.Path.AddRoundRect(rect, 24f, 24f);
            }
        }
        else if (stroke.ToolType == ToolType.Ellipse)
        {
            stroke.Path.MoveTo(lineStartPoint);
            var left = Math.Min(lineStartPoint.X, endPoint.X);
            var top = Math.Min(lineStartPoint.Y, endPoint.Y);
            var rect = SKRect.Create(new SKPoint(left, top),
                Utilities.GetSize(lineStartPoint, endPoint));
            stroke.Path.AddOval(rect);
        }
        else
        {
            stroke.Path.MoveTo(lineStartPoint);
            stroke.Path.LineTo(endPoint);

            if (stroke.ToolType == ToolType.Arrow)
            {
                var (p1, p2) =
                    ArrowTool.GetArrowHeadPoints(lineStartPoint, endPoint,
                        stroke.Paint.StrokeWidth);

                stroke.Path.MoveTo(endPoint);
                stroke.Path.LineTo(p1);

                stroke.Path.MoveTo(endPoint);
                stroke.Path.LineTo(p2);
            }
        }
    }

    /// <summary>
    /// Marks the strokes that are a target for erasure
    /// </summary>
    /// <param name="eraserPoint">The latest point in the eraser's stroke</param>
    /// <param name="canvasElements">Collection of all current elements on the canvas</param>
    /// <param name="eraserStroke">The active eraser stroke</param>
    private void CheckAndErase(SKPoint eraserPoint, IEnumerable<CanvasElement> canvasElements,
        EraserStroke eraserStroke)
    {
        foreach (var element in canvasElements)
        {
            if (element is DrawStroke stroke)
            {
                var strokeId = stroke.Id;
                switch (stroke.ToolType)
                {
                    case ToolType.Line or ToolType.Arrow:
                    {
                        var endPoints = new[] { stroke.Path[0], stroke.Path[1] };
                        if (Utilities.IsPointNearLine(eraserPoint, endPoints, 10.0f))
                        {
                            stroke.IsToBeErased = true;
                            eraserStroke.Targets.Add(strokeId);
                        }

                        break;
                    }
                    default:
                    {
                        if (stroke.Path.Contains(eraserPoint.X, eraserPoint.Y))
                        {
                            stroke.IsToBeErased = true;
                            eraserStroke.Targets.Add(strokeId);
                        }

                        break;
                    }
                }
            }
            else if (element is CanvasImage image)
            {
                if (image.Bounds.Contains(eraserPoint.X, eraserPoint.Y))
                {
                    image.IsToBeErased = true;
                    eraserStroke.Targets.Add(image.Id);
                }
            }
        }
    }

    /// <summary>
    /// Finds all strokes that are within the selection boundary
    /// </summary>
    private void CheckAndSelect(SKRect boundRect, SelectionBound bound, IEnumerable<CanvasElement> canvasElements)
    {
        bound.Targets.Clear();
        foreach (var element in canvasElements)
        {
            if (element is DrawStroke stroke)
            {
                SKRect strokeBounds = stroke.Path.TightBounds;
                if (boundRect.Contains(strokeBounds))
                {
                    bound.Targets.Add(stroke.Id);
                }
            }
            else if (element is CanvasImage image)
            {
                if (boundRect.Contains(image.Bounds))
                {
                    bound.Targets.Add(image.Id);
                }
            }
        }
    }

    private bool HasCanvasEvents()
    {
        return CanvasEvents.Count > 0;
    }

    /// <summary>
    /// Opens the specified URL in the default browser
    /// </summary>
    /// <param name="url">The url to open</param>
    [RelayCommand]
    private void OpenUrl(string url)
    {
        if (!string.IsNullOrEmpty(url))
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                // Hack for Linux if the above fails
                Process.Start("xdg-open", url);
                Console.WriteLine($"Could not open URL: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Clears any active selection
    /// </summary>
    public void ClearSelection()
    {
        ApplyEvent(new ClearSelectionEvent(Guid.NewGuid()));
    }

    /// <summary>
    /// Exits the application
    /// </summary>
    [RelayCommand]
    private async Task ExitAsync()
    {
        if (HasCanvasEvents())
        {
            var confirmed = await _dialogService.ShowWarningConfirmationAsync("Warning",
                "All unsaved work will be lost. Are you sure you want to proceed?");
            if (!confirmed) return;
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    /// <summary>
    /// Clears the canvas
    /// </summary>
    [RelayCommand]
    private async Task ResetCanvas()
    {
        if (HasCanvasEvents())
        {
            var confirmed = await _dialogService.ShowWarningConfirmationAsync("Warning",
                "This will clear your current canvas. Are you sure you want to proceed?");
            if (!confirmed) return;
        }

        WeakReferenceMessenger.Default.Send(new ClearCanvasMessage());
    }
}