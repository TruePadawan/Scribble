using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Scribble.Lib;
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
            if (CanvasEvents[latestEventIdx] is EndDrawStrokeEvent && CanvasEvents[i] is NewDrawStrokeEvent)
            {
                _currentEventIndex = i - 1;
                break;
            }

            if (CanvasEvents[latestEventIdx] is TriggerEraseEvent && CanvasEvents[i] is EndDrawStrokeEvent)
            {
                _currentEventIndex = i;
                break;
            }
        }

        ReplayEvents();
    }

    public void Redo()
    {
        if (CanvasEvents.Count == 0 || _currentEventIndex == CanvasEvents.Count - 1) return;
        for (int i = _currentEventIndex + 1; i < CanvasEvents.Count; i++)
        {
            if (CanvasEvents[i] is TriggerEraseEvent || CanvasEvents[i] is EndDrawStrokeEvent)
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
        if (@event is EndDrawStrokeEvent) return;
        ReplayEvents();
    }

    private void ReplayEvents()
    {
        var drawStrokes = new Dictionary<Guid, DrawStroke>();
        var eraserStrokes = new Dictionary<Guid, EraserStroke>();
        var staleEraseStrokes = new List<Guid>();
        for (var i = 0; i <= _currentEventIndex; i++)
        {
            var canvasEvent = CanvasEvents[i];
            switch (canvasEvent)
            {
                case NewDrawStrokeEvent ev:
                    var newPath = new SKPath();
                    newPath.MoveTo(ev.StartPoint);
                    drawStrokes[ev.StrokeId] = new DrawStroke
                    {
                        Paint = ev.StrokePaint,
                        Path = newPath
                    };
                    break;
                case NewEraseStrokeEvent ev:
                    var eraserPath = new SKPath();
                    eraserPath.MoveTo(ev.StartPoint);
                    var newEraserStroke = new EraserStroke
                    {
                        Path = eraserPath
                    };

                    // Find all targets for erasing
                    foreach (var keyValuePair in drawStrokes)
                    {
                        if (keyValuePair.Value.Path.Contains(ev.StartPoint.X, ev.StartPoint.Y))
                        {
                            keyValuePair.Value.IsToBeErased = true;
                            newEraserStroke.Targets.Add(keyValuePair.Key);
                        }
                    }

                    eraserStrokes[ev.StrokeId] = newEraserStroke;
                    break;
                case DrawStrokeLineToEvent ev:
                    if (drawStrokes.ContainsKey(ev.StrokeId))
                    {
                        drawStrokes[ev.StrokeId].Path.LineTo(ev.Point);
                    }

                    break;
                case EraseStrokeLineToEvent ev:
                    if (eraserStrokes.ContainsKey(ev.StrokeId))
                    {
                        // Find all targets for erasing
                        foreach (var keyValuePair in drawStrokes)
                        {
                            if (keyValuePair.Value.Path.Contains(ev.Point.X, ev.Point.Y))
                            {
                                keyValuePair.Value.IsToBeErased = true;
                                eraserStrokes[ev.StrokeId].Targets.Add(keyValuePair.Key);
                            }
                        }

                        eraserStrokes[ev.StrokeId].Path.LineTo(ev.Point);
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
}