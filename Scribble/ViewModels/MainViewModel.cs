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
    public ObservableCollection<DrawStroke> CanvasStrokes { get; } = [];
    private readonly Stack<DrawStroke> _redoStack = [];
    public event Action? RequestInvalidateCanvas;


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

    public void AddStroke(DrawStroke newDrawStroke)
    {
        CanvasStrokes.Add(newDrawStroke);
        _redoStack.Clear();
    }
}