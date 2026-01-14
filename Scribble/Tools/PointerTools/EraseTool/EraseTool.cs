using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Scribble.Lib;
using Scribble.ViewModels;
using SkiaSharp;

namespace Scribble.Tools.PointerTools.EraseTool;

public class EraseTool : PointerToolsBase
{
    // private EraserStroke _eraserStroke = new();
    private Guid _currentStrokeId = Guid.NewGuid();

    public EraseTool(string name, MainViewModel viewModel)
        : base(name, viewModel, LoadToolBitmap(typeof(EraseTool), "eraser.png"))
    {
        Cursor = new Cursor(ToolIcon, new PixelPoint(10, 40));
    }

    public override void HandlePointerMove(Point prevCoord, Point currentCoord)
    {
        var nextPoint = new SKPoint((float)currentCoord.X, (float)currentCoord.Y);
        ViewModel.ApplyEvent(new EraseStrokeLineToEvent(_currentStrokeId, nextPoint));
        // _eraserStroke.Path.LineTo((float)currentCoord.X, (float)currentCoord.Y);
        // ViewModel.TriggerCanvasRedraw();
    }

    public override void HandlePointerClick(Point coord)
    {
        var startPoint = new SKPoint((float)coord.X, (float)coord.Y);
        _currentStrokeId = Guid.NewGuid();
        ViewModel.ApplyEvent(new NewEraseStrokeEvent(_currentStrokeId, startPoint));
        // _eraserStroke = new EraserStroke();
        // _eraserStroke.Path.MoveTo((float)coord.X, (float)coord.Y);
        //
        // ViewModel.AddStroke(_eraserStroke);
    }

    public override void HandlePointerRelease(Point prevCoord, Point currentCoord)
    {
        ViewModel.ApplyEvent(new TriggerEraseEvent(_currentStrokeId));
        // ViewModel.CanvasStrokes.Remove(_eraserStroke);
        //
        // _eraserStroke.Path.Dispose();
        // _eraserStroke.Paint.Dispose();
    }

    // public override bool RenderOptions(Panel parent)
    // {
    //     // Render a slider for controlling the eraser thickness
    //     var slider = new Slider
    //     {
    //         TickFrequency = 5,
    //         IsSnapToTickEnabled = true,
    //         Minimum = 1,
    //         Maximum = 40,
    //         Value = _strokePaint.StrokeWidth
    //     };
    //     slider.ValueChanged += ((sender, args) => { _strokePaint.StrokeWidth = (float)args.NewValue; });
    //     slider.Padding = new Thickness(8, 0);
    //
    //     parent.Children.Add(CreateOptionControl(slider, "Thickness"));
    //     return true;
    // }
}