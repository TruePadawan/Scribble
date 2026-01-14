using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    [ObservableProperty] private ObservableCollection<Stroke> _canvasStrokes = [];
    private readonly Stack<Stroke> _redoStack = [];
    public event Action? RequestInvalidateCanvas;
    private List<Event> CanvasEvents { get; } = [];


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
        if (CanvasStrokes.Count > 0)
        {
            var lastStroke = CanvasStrokes.Last();
            CanvasStrokes.Remove(lastStroke);
            _redoStack.Push(lastStroke);
        }
    }

    public void Redo()
    {
        if (_redoStack.Count > 0)
        {
            var nextStroke = _redoStack.Pop();
            CanvasStrokes.Add(nextStroke);
        }
    }

    public void AddStroke(Stroke newDrawStroke)
    {
        CanvasStrokes.Add(newDrawStroke);
        _redoStack.Clear();
    }

    public void ApplyEvent(Event @event)
    {
        CanvasEvents.Add(@event);
        ReplayEvents();
    }

    private void ReplayEvents()
    {
        var drawStrokes = new Dictionary<Guid, DrawStroke>();
        var eraserStrokes = new Dictionary<Guid, EraserStroke>();
        foreach (var canvasEvent in CanvasEvents)
        {
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

                        eraserStrokes.Remove(ev.StrokeId);
                    }

                    break;
            }
        }

        CanvasStrokes = new ObservableCollection<Stroke>(drawStrokes.Values.ToList());

        TriggerCanvasRedraw();
    }
}