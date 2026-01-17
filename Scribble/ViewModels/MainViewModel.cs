using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Scribble.Lib;
using Scribble.Tools.PointerTools.ArrowTool;
using Scribble.Tools.PointerTools.EllipseTool;
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
    public event Action? RequestInvalidateCanvas;
    private List<StrokeEvent> CanvasEvents { get; } = [];
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

    public void TriggerCanvasRedraw()
    {
        RequestInvalidateCanvas?.Invoke();
    }

    public void Undo()
    {
        if (CanvasEvents.Count == 0) return;
        int latestEventIdx = _currentEventIndex;
        for (int i = _currentEventIndex - 1; i >= 0; i--)
        {
            if (CanvasEvents[latestEventIdx] is EndStrokeEvent && CanvasEvents[i] is StartStrokeEvent)
            {
                _currentEventIndex = i - 1;
                break;
            }

            if (CanvasEvents[latestEventIdx] is TriggerEraseEvent)
            {
                if (CanvasEvents[i] is EndStrokeEvent || CanvasEvents[i] is TriggerEraseEvent)
                {
                    _currentEventIndex = i;
                    break;
                }
            }
        }

        ReplayEvents();
    }

    public void Redo()
    {
        if (CanvasEvents.Count == 0 || _currentEventIndex == CanvasEvents.Count - 1) return;
        for (int i = _currentEventIndex + 1; i < CanvasEvents.Count; i++)
        {
            if (CanvasEvents[i] is TriggerEraseEvent || CanvasEvents[i] is EndStrokeEvent)
            {
                _currentEventIndex = i;
                break;
            }
        }

        ReplayEvents();
    }

    public void ApplyStrokeEvent(StrokeEvent @event)
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
        ReplayEvents();
    }

    private void ReplayEvents()
    {
        var drawStrokes = new Dictionary<Guid, DrawStroke>();
        var eraserStrokes = new Dictionary<Guid, EraserStroke>();
        var staleEraseStrokes = new List<Guid>();
        var eraserHeads = new Dictionary<Guid, SKPoint>();

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
                        Paint = ev.Paint,
                        Path = textPath,
                        ToolType = StrokeTool.Text,
                        Text = ev.Text
                    };
                    break;
            }
        }

        // Remove erase events that don't actually erase anything
        if (staleEraseStrokes.Count > 0)
        {
            foreach (var staleEraseStrokeId in staleEraseStrokes)
            {
                CanvasEvents.RemoveAll(ev => ev.StrokeId == staleEraseStrokeId);
            }

            _currentEventIndex = CanvasEvents.Count - 1;
        }


        CanvasStrokes = new List<Stroke>(drawStrokes.Values.ToList());
    }

    private void CheckAndErase(SKPoint eraserPoint, Dictionary<Guid, DrawStroke> drawStrokes, EraserStroke eraserStroke)
    {
        foreach (var (strokeId, stroke) in drawStrokes)
        {
            switch (stroke.ToolType)
            {
                case StrokeTool.Text:
                    if (stroke.Path.PointCount > 0)
                    {
                        var pos = stroke.Path[0];
                        if (SKPoint.Distance(eraserPoint, pos) < 30)
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
}