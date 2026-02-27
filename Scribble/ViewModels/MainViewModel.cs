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
    [ObservableProperty] private List<Stroke> _canvasStrokes = [];
    private readonly HashSet<Guid> _deletedActions = [];
    public Dictionary<Guid, List<Guid>> SelectionTargets { get; private set; } = [];
    public Queue<Event> CanvasEvents { get; private set; } = [];

    private readonly IDialogService _dialogService;

    private readonly Stack<Guid> _undoStack = [];
    private readonly Stack<Guid> _redoStack = [];
    private readonly HashSet<Guid> _mySelections = [];

    public MultiUserDrawingViewModel MultiUserDrawingViewModel { get; }
    public DocumentViewModel DocumentViewModel { get; }
    public UiStateViewModel UiStateViewModel { get; }

    public MainViewModel(MultiUserDrawingViewModel multiplayer, DocumentViewModel documentViewModel,
        UiStateViewModel uiStateViewModel,
        IDialogService dialogService)
    {
        _dialogService = dialogService;
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
                message.Reply(new CanvasDataPayload(mainViewModel.CanvasStrokes,
                    mainViewModel.UiStateViewModel.BackgroundColor));
            });

        // Load canvas data when the DocumentViewModel reads a file
        WeakReferenceMessenger.Default.Register<MainViewModel, LoadCanvasDataMessage>(this,
            (mainViewModel, message) =>
            {
                mainViewModel.ApplyEvent(new LoadCanvasEvent(Guid.NewGuid(), message.Strokes));
            });

        // Clear data when the DocumentViewModel triggers a reset
        WeakReferenceMessenger.Default.Register<MainViewModel, ClearCanvasMessage>(this,
            (mainViewModel, m) => { mainViewModel.ApplyEvent(new LoadCanvasEvent(Guid.NewGuid(), [])); });
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
            _mySelections.Add(ev.BoundId);
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
        var clearsSomething = new Dictionary<Guid, bool>();
        var staleActionIds = new List<Guid>();
        var strokeToActionMap = new Dictionary<Guid, Guid>();
        var strokeTexts = new Dictionary<Guid, string>();

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
                    strokeToActionMap[ev.StrokeId] = ev.ActionId;
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
                    CheckAndErase(ev.StartPoint, drawStrokes, newEraserStroke);

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
                            CheckAndErase(new SKPoint((float)checkX, (float)checkY), drawStrokes, currentEraserStroke);
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
                            _deletedActions.Add(strokeToActionMap[targetId]);
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
                        var stroke = drawStrokes[ev.StrokeId];
                        var lineStartPoint = stroke.Path.Points[0];
                        var lineEndPoint = ev.EndPoint;

                        stroke.Path.Reset();

                        if (stroke.ToolType == ToolType.Rectangle)
                        {
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
                        }
                        else if (stroke.ToolType == ToolType.Ellipse)
                        {
                            stroke.Path.MoveTo(lineStartPoint);
                            var left = Math.Min(lineStartPoint.X, lineEndPoint.X);
                            var top = Math.Min(lineStartPoint.Y, lineEndPoint.Y);
                            var rect = SKRect.Create(new SKPoint(left, top),
                                Utilities.GetSize(lineStartPoint, lineEndPoint));
                            stroke.Path.AddOval(rect);
                        }
                        else
                        {
                            stroke.Path.MoveTo(lineStartPoint);
                            stroke.Path.LineTo(lineEndPoint);

                            if (stroke.ToolType == ToolType.Arrow)
                            {
                                var (p1, p2) =
                                    ArrowTool.GetArrowHeadPoints(lineStartPoint, lineEndPoint,
                                        stroke.Paint.StrokeWidth);

                                stroke.Path.MoveTo(lineEndPoint);
                                stroke.Path.LineTo(p1);

                                stroke.Path.MoveTo(lineEndPoint);
                                stroke.Path.LineTo(p2);
                            }
                        }
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
                    strokeToActionMap[ev.StrokeId] = ev.ActionId;
                    break;
                case CreateSelectionBoundEvent ev:
                    var selectionPath = new SKPath();
                    selectionPath.MoveTo(ev.StartPoint);

                    // Track if there's any active selection before clearing
                    clearsSomething[ev.BoundId] = selectionBounds.Any(sb => sb.Value.Targets.Count > 0);
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
                        CheckAndSelect(boundRect, bound, drawStrokes);
                    }

                    break;
                case EndSelectionEvent ev:
                    if (selectionBounds.ContainsKey(ev.BoundId))
                    {
                        if (selectionBounds[ev.BoundId].Targets.Count == 0 &&
                            !clearsSomething.GetValueOrDefault(ev.BoundId))
                        {
                            staleActionIds.Add(ev.ActionId);
                        }
                    }

                    break;
                case ClearSelectionEvent:
                    selectionBounds.Clear();
                    break;
                case MoveStrokesEvent ev:
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
                        }
                    }

                    break;
                case RotateStrokesEvent ev:
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
                        }
                    }

                    break;
                case ScaleStrokesEvent ev:
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
                        }
                    }

                    break;
                case LoadCanvasEvent ev:
                    drawStrokes.Clear();
                    foreach (Stroke stroke in ev.Strokes)
                    {
                        if (stroke is DrawStroke drawStroke)
                        {
                            drawStrokes[drawStroke.Id] = drawStroke;
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
            }
        }

        CanvasStrokes = new List<Stroke>(drawStrokes.Values.ToList());
        // Show the selection only on the client that is doing the selection
        SelectionTargets = selectionBounds.Where(pair => _mySelections.Contains(pair.Key))
            .ToDictionary(k => k.Key, v => v.Value.Targets.ToList());
        RequestInvalidateSelection?.Invoke();

        return staleActionIds;
    }

    private void CheckAndErase(SKPoint eraserPoint, Dictionary<Guid, DrawStroke> drawStrokes, EraserStroke eraserStroke)
    {
        foreach (var (strokeId, stroke) in drawStrokes)
        {
            switch (stroke.ToolType)
            {
                case ToolType.Line or ToolType.Arrow:
                {
                    var endPoints = new[] { stroke.Path[0], stroke.Path[1] };
                    if (IsPointNearLine(eraserPoint, endPoints, 10.0f))
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
    }

    private bool IsPointNearLine(SKPoint point, SKPoint[] lineEndPoints, float tolerance)
    {
        var start = lineEndPoints[0];
        var end = lineEndPoints[1];

        float lineLenSq = float.Pow(end.X - start.X, 2) + float.Pow(end.Y - start.Y, 2);

        // Check if the 'line' is actually just a point
        if (lineLenSq == 0)
        {
            return SKPoint.Distance(point, start) < tolerance;
        }

        float t = ((point.X - start.X) * (end.X - start.X) +
                   (point.Y - start.Y) * (end.Y - start.Y)) / lineLenSq;

        // Clamp t to the segment [0, 1] to handle the endpoints correctly
        t = float.Clamp(t, 0f, 1f);

        // Find the closest point on the line segment
        var closest = new SKPoint(
            start.X + t * (end.X - start.X),
            start.Y + t * (end.Y - start.Y)
        );

        return SKPoint.Distance(point, closest) < tolerance;
    }

    private void CheckAndSelect(SKRect boundRect, SelectionBound bound, Dictionary<Guid, DrawStroke> drawStrokes)
    {
        bound.Targets.Clear();
        foreach (var (id, stroke) in drawStrokes)
        {
            SKRect strokeBounds = stroke.Path.Bounds;

            if (boundRect.Contains(strokeBounds))
            {
                bound.Targets.Add(id);
            }
        }
    }

    private bool HasEvents()
    {
        return CanvasEvents.Count > 0;
    }

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

    public void ClearSelection()
    {
        ApplyEvent(new ClearSelectionEvent(Guid.NewGuid()));
    }

    [RelayCommand]
    private async Task ExitAsync()
    {
        if (HasEvents())
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
}