using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Scribble.Lib;
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
    private int _currentEventIndex = -1;


    public MainViewModel()
    {
        BackgroundColor = SKColors.Black;
        ScaleTransform = new ScaleTransform(1, 1);
    }

    public Vector GetCanvasDimensions() => new Vector(CanvasWidth, CanvasHeight);

    public double GetCurrentScale() => ScaleTransform.ScaleX;

    public void SetCurrentScale(double newScale)
    {
        ScaleTransform.ScaleX = newScale;
        ScaleTransform.ScaleY = newScale;
    }

    public void Undo()
    {
        if (CanvasEvents.Count == 0 || _currentEventIndex == -1) return;

        // Move back to find the previous terminal event
        int i = _currentEventIndex;

        // If we are currently AT a terminal event, we want to go BEFORE it and find the next previous one
        if (CanvasEvents[i] is ITerminalEvent)
        {
            i--;
        }

        while (i >= 0 && CanvasEvents[i] is not ITerminalEvent)
        {
            i--;
        }

        _currentEventIndex = i;
        ReplayEvents();
    }

    public void Redo()
    {
        if (CanvasEvents.Count == 0 || _currentEventIndex == CanvasEvents.Count - 1) return;

        int i = _currentEventIndex + 1;
        while (i < CanvasEvents.Count && CanvasEvents[i] is not ITerminalEvent)
        {
            i++;
        }

        if (i < CanvasEvents.Count)
        {
            _currentEventIndex = i;
            ReplayEvents();
        }
    }

    public void ApplyEvent(Event @event)
    {
        var eventsCount = CanvasEvents.Count;
        // Remove all stale events if we branch off
        if (eventsCount > 0 && _currentEventIndex < eventsCount - 1)
        {
            CanvasEvents.RemoveRange(_currentEventIndex + 1, eventsCount - _currentEventIndex - 1);
        }

        CanvasEvents.Add(@event);
        _currentEventIndex = CanvasEvents.Count - 1;

        if (@event is EndStrokeEvent) return;
        var staleIds = ReplayEvents();

        if (staleIds.Count > 0 && _currentEventIndex == CanvasEvents.Count - 1)
        {
            bool changed = false;
            foreach (var id in staleIds)
            {
                int removed = CanvasEvents.RemoveAll(ev => ev is StrokeEvent { StrokeId: var sid } && sid == id);
                if (removed > 0) changed = true;
            }

            if (changed)
            {
                _currentEventIndex = CanvasEvents.Count - 1;
                ReplayEvents();
            }
        }
    }

    private List<Guid> ReplayEvents()
    {
        var drawStrokes = new Dictionary<Guid, DrawStroke>();
        var eraserStrokes = new Dictionary<Guid, EraserStroke>();
        var staleEraseStrokes = new List<Guid>();
        var eraserHeads = new Dictionary<Guid, SKPoint>();
        var selectionBounds = new Dictionary<Guid, SelectionBound>();
        var staleSelectionBounds = new List<Guid>();
        var clearsSomething = new Dictionary<Guid, bool>();

        for (var i = 0; i <= _currentEventIndex; i++)
        {
            var canvasEvent = CanvasEvents[i];
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
                        ToolType = ev.ToolType
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
                        stroke.Path.Reset();
                        stroke.Path.MoveTo(lineStartPoint);
                        stroke.Path.LineTo(ev.EndPoint);

                        if (stroke.ToolType == StrokeTool.Arrow)
                        {
                            var (p1, p2) =
                                ArrowTool.GetArrowHeadPoints(lineStartPoint, ev.EndPoint, stroke.Paint.StrokeWidth);

                            stroke.Path.MoveTo(ev.EndPoint);
                            stroke.Path.LineTo(p1);

                            stroke.Path.MoveTo(ev.EndPoint);
                            stroke.Path.LineTo(p2);
                        }
                    }

                    break;
                case AddTextEvent ev:
                    var textPath = new SKPath();
                    textPath.MoveTo(ev.Position);
                    drawStrokes[ev.StrokeId] = new TextStroke
                    {
                        Id = ev.StrokeId,
                        Paint = ev.Paint,
                        Path = textPath,
                        ToolType = StrokeTool.Text,
                        Text = ev.Text
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
                case StrokeTool.Text:
                    if (stroke is TextStroke textStroke && stroke.Path.PointCount > 0)
                    {
                        var pos = stroke.Path[0];
                        var bounds = new SKRect();
                        textStroke.Paint.MeasureText(textStroke.Text, ref bounds);
                        bounds.Offset(pos);
                        bounds.Inflate(10, 10);

                        if (bounds.Contains(eraserPoint))
                        {
                            stroke.IsToBeErased = true;
                            eraserStroke.Targets.Add(strokeId);
                        }
                    }

                    break;
                case StrokeTool.Ellipse or StrokeTool.Rectangle:
                {
                    var start = stroke.Path[0];
                    var end = stroke.Path[1];
                    var rect = SKRect.Create(start, Utilities.GetSize(start, end));
                    var tolerance = 10.0f;

                    // Quick bounds check
                    var bounds = rect;
                    bounds.Inflate(tolerance, tolerance);
                    if (!bounds.Contains(eraserPoint)) continue;

                    using var path = new SKPath();
                    if (stroke.ToolType == StrokeTool.Rectangle)
                    {
                        path.AddRect(rect);
                    }
                    else
                    {
                        path.AddOval(rect);
                    }

                    using var paint = new SKPaint();
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = tolerance * 2;
                    using var strokePath = new SKPath();
                    paint.GetFillPath(path, strokePath);

                    if (strokePath.Contains(eraserPoint.X, eraserPoint.Y))
                    {
                        stroke.IsToBeErased = true;
                        eraserStroke.Targets.Add(strokeId);
                    }

                    break;
                }
                // Check if the erase point is visually on the line
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
            SKRect strokeBounds;

            if (stroke.ToolType == StrokeTool.Text && stroke is TextStroke textStroke && stroke.Path.PointCount > 0)
            {
                var pos = stroke.Path[0];
                var bounds = new SKRect();
                textStroke.Paint.MeasureText(textStroke.Text, ref bounds);
                bounds.Offset(pos);
                strokeBounds = bounds;
            }
            else
            {
                strokeBounds = stroke.Path.Bounds;
            }

            if (boundRect.Contains(strokeBounds))
            {
                bound.Targets.Add(id);
            }
        }
    }
}