using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.AspNetCore.SignalR.Client;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using Scribble.Lib;
using Scribble.Shared.Lib;
using Scribble.Tools.PointerTools.ArrowTool;
using Scribble.Utils;
using SkiaSharp;

namespace Scribble.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public static int CanvasWidth => 10000;
    public static int CanvasHeight => 10000;

    [ObservableProperty] private SKColor _backgroundColor;
    public ScaleTransform ScaleTransform { get; }
    [ObservableProperty] private List<Stroke> _canvasStrokes = [];
    public Dictionary<Guid, List<Guid>> SelectionTargets { get; private set; } = new();
    public event Action? RequestInvalidateSelection;
    private List<Event> CanvasEvents { get; } = [];
    private readonly LiveDrawingService _liveDrawingService;
    private string? _joinedRoomId;
    private readonly Stack<Guid> _undoStack = new();
    private readonly Stack<Guid> _redoStack = new();


    public MainViewModel()
    {
        BackgroundColor = SKColors.Transparent;
        ScaleTransform = new ScaleTransform(1, 1);
        _liveDrawingService = new LiveDrawingService("https://localhost:7189/liveDrawingHub");

        _liveDrawingService.EventReceived += OnNetworkEventReceived;
        _liveDrawingService.CanvasStateReceived += OnCanvasStateReceived;
        _liveDrawingService.CanvasStateRequested += OnCanvasStateRequested;
    }

    // Event handler for when another client in the room draws something
    private void OnNetworkEventReceived(Event @event)
    {
        Dispatcher.UIThread.Post(() => { ProcessEvent(@event); });
    }

    // Event handler for sending canvas state to client that just joined the room
    private void OnCanvasStateRequested(string targetConnectionId)
    {
        Dispatcher.UIThread.Post(async void () =>
        {
            var strokesSnapshot = CanvasStrokes.ToList();
            await _liveDrawingService.SendCanvasStateToClientAsync(targetConnectionId, strokesSnapshot);
        });
    }

    // Event handler for processing the canvas state snapshot received from room host
    private void OnCanvasStateReceived(List<Stroke> strokes)
    {
        Dispatcher.UIThread.Post(() => { ProcessEvent(new RestoreCanvasEvent(strokes)); });
    }

    public Vector GetCanvasDimensions() => new Vector(CanvasWidth, CanvasHeight);

    public double GetCurrentScale() => ScaleTransform.ScaleX;

    public void SetCurrentScale(double newScale)
    {
        ScaleTransform.ScaleX = newScale;
        ScaleTransform.ScaleY = newScale;
    }

    private void AddToMyHistory(Guid strokeId)
    {
        _undoStack.Push(strokeId);
        _redoStack.Clear();
    }

    public void Undo()
    {
        if (_undoStack.Count == 0) return;
        var strokeId = _undoStack.Pop();
        _redoStack.Push(strokeId);
        ApplyEvent(new UndoEvent(strokeId), isLocalEvent: true);
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;
        var strokeId = _redoStack.Pop();
        _undoStack.Push(strokeId);
        ApplyEvent(new RedoEvent(strokeId), isLocalEvent: true);
    }

    public void ApplyEvent(Event @event, bool isLocalEvent = true)
    {
        ProcessEvent(@event, isLocalEvent);

        if (!string.IsNullOrEmpty(_joinedRoomId))
        {
            _ = _liveDrawingService.BroadcastEventAsync(_joinedRoomId, @event);
        }
    }

    private void ProcessEvent(Event @event, bool isLocalEvent = false)
    {
        // Keep track of locally drawn strokes for undo/redo functionality
        if (@event is StartStrokeEvent or StartEraseStrokeEvent or AddTextEvent)
        {
            if (isLocalEvent)
            {
                var strokeId = ((StrokeEvent)@event).StrokeId;
                AddToMyHistory(strokeId);
            }
        }

        CanvasEvents.Add(@event);

        if (@event is EndStrokeEvent) return;
        var staleIds = ReplayEvents();

        if (staleIds.Count > 0)
        {
            bool changed = false;
            foreach (var id in staleIds)
            {
                int removed = CanvasEvents.RemoveAll(ev => ev is StrokeEvent { StrokeId: var sid } && sid == id);
                if (removed > 0) changed = true;
            }

            if (changed)
            {
                ReplayEvents();
            }
        }
    }

    private List<Guid> ReplayEvents()
    {
        var hiddenStrokeIds = new HashSet<Guid>();
        foreach (var canvasEvent in CanvasEvents)
        {
            if (canvasEvent is UndoEvent ev)
            {
                hiddenStrokeIds.Add(ev.TargetStrokeId);
            }
            else if (canvasEvent is RedoEvent redoEv)
            {
                hiddenStrokeIds.Remove(redoEv.TargetStrokeId);
            }
        }

        var drawStrokes = new Dictionary<Guid, DrawStroke>();
        var eraserStrokes = new Dictionary<Guid, EraserStroke>();
        var staleEraseStrokes = new List<Guid>();
        var eraserHeads = new Dictionary<Guid, SKPoint>();
        var selectionBounds = new Dictionary<Guid, SelectionBound>();
        var staleSelectionBounds = new List<Guid>();
        var clearsSomething = new Dictionary<Guid, bool>();

        foreach (var canvasEvent in CanvasEvents)
        {
            if (canvasEvent is StrokeEvent strokeEv && hiddenStrokeIds.Contains(strokeEv.StrokeId))
            {
                continue;
            }

            switch (canvasEvent)
            {
                case StartStrokeEvent ev:
                    var newLinePath = new SKPath();
                    newLinePath.MoveTo(ev.StartPoint);
                    drawStrokes[ev.StrokeId] = new DrawStroke
                    {
                        Id = ev.StrokeId,
                        Paint = ev.StrokePaint,
                        Path = newLinePath,
                        ToolType = ev.ToolType,
                    };
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
                        }

                        if (eraserStrokes[ev.StrokeId].Targets.Count == 0)
                        {
                            staleEraseStrokes.Add(ev.StrokeId);
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

                        if (stroke.ToolType == StrokeTool.Rectangle)
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
                        else if (stroke.ToolType == StrokeTool.Ellipse)
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

                            if (stroke.ToolType == StrokeTool.Arrow)
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
                        Paint = ev.Paint,
                        Path = textPath,
                        ToolType = StrokeTool.Text,
                    };
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
                            staleSelectionBounds.Add(ev.BoundId);
                        }
                    }

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
                case RestoreCanvasEvent ev:
                    drawStrokes.Clear();
                    foreach (Stroke stroke in ev.Strokes)
                    {
                        if (stroke is DrawStroke drawStroke)
                        {
                            drawStrokes[drawStroke.Id] = drawStroke;
                        }
                    }

                    break;
            }
        }

        CanvasStrokes = new List<Stroke>(drawStrokes.Values.ToList());
        SelectionTargets = selectionBounds.ToDictionary(k => k.Key, v => v.Value.Targets.ToList());
        RequestInvalidateSelection?.Invoke();

        var allStale = new List<Guid>();
        allStale.AddRange(staleEraseStrokes);
        allStale.AddRange(staleSelectionBounds);
        return allStale;
    }

    private void CheckAndErase(SKPoint eraserPoint, Dictionary<Guid, DrawStroke> drawStrokes, EraserStroke eraserStroke)
    {
        foreach (var (strokeId, stroke) in drawStrokes)
        {
            switch (stroke.ToolType)
            {
                case StrokeTool.Line or StrokeTool.Arrow:
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

    public async Task SaveCanvasToFile(IStorageFile file)
    {
        await using var stream = await file.OpenWriteAsync();
        using var streamWriter = new StreamWriter(stream);

        var serializerOptions = new JsonSerializerOptions { WriteIndented = true };
        var jsonCanvasStrokes = new JsonArray();
        for (int i = 0; i < CanvasStrokes.Count; i++)
        {
            var stroke = CanvasStrokes[i];
            jsonCanvasStrokes.Add(JsonSerializer.SerializeToNode(stroke, serializerOptions));
        }

        var canvasState = new JsonObject
        {
            ["strokes"] = jsonCanvasStrokes,
            ["backgroundColor"] = BackgroundColor.ToString()
        };
        await streamWriter.WriteAsync(canvasState.ToJsonString(serializerOptions));
    }

    public bool HasEvents()
    {
        return CanvasEvents.Count > 0;
    }

    public async Task RestoreCanvasFromFile(IStorageFile file)
    {
        await using var stream = await file.OpenReadAsync();
        using var streamReader = new StreamReader(stream);
        var json = await streamReader.ReadToEndAsync();
        var canvasState = JsonNode.Parse(json);
        var rawStrokes = canvasState?["strokes"]?.AsArray();
        if (canvasState is null || rawStrokes is null) return;

        if (CanvasEvents.Count > 0)
        {
            var box = MessageBoxManager
                .GetMessageBoxStandard("Warning",
                    "This will clear your current canvas. Are you sure you want to proceed?",
                    ButtonEnum.YesNo,
                    Icon.Warning);

            var result = await box.ShowAsync();
            if (result != ButtonResult.Yes) return;
        }

        List<Stroke> strokes = [];
        foreach (var stroke in rawStrokes)
        {
            if (stroke is null) throw new Exception("Invalid canvas file");
            var deserializedEvent = JsonSerializer.Deserialize<Stroke>(stroke.ToJsonString());
            if (deserializedEvent is null) throw new Exception("Invalid canvas file");
            strokes.Add(deserializedEvent);
        }

        ApplyEvent(new RestoreCanvasEvent(strokes));

        // Restore background color
        var bgColor = canvasState["backgroundColor"]?.ToString();
        if (bgColor != null)
        {
            BackgroundColor = SKColor.Parse(bgColor);
        }
    }

    public async Task ResetCanvas()
    {
        if (CanvasEvents.Count > 0)
        {
            var box = MessageBoxManager
                .GetMessageBoxStandard("Warning",
                    "This will clear your current canvas. Are you sure you want to proceed?",
                    ButtonEnum.YesNo,
                    Icon.Warning);

            var result = await box.ShowAsync();
            if (result != ButtonResult.Yes) return;
        }

        ApplyEvent(new RestoreCanvasEvent([]));
    }

    public void ChangeBackgroundColor(Color color)
    {
        BackgroundColor = Utilities.ToSkColor(color);
    }

    public async Task JoinRoom(string roomId)
    {
        await _liveDrawingService.StartAsync();
        await _liveDrawingService.JoinRoomAsync(roomId);
        _joinedRoomId = roomId;
    }

    public async Task LeaveRoom()
    {
        if (_liveDrawingService.ConnectionState != HubConnectionState.Disconnected)
        {
            await _liveDrawingService.StopAsync();
        }
    }

    public HubConnectionState GetLiveDrawingServiceConnectionState() => _liveDrawingService.ConnectionState;
}