using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Scribble.Lib;
using Scribble.ViewModels;
using SkiaSharp;

namespace Scribble.Tools.PointerTools.EraseTool;

public class EraseTool : PointerToolsBase
{
    private readonly SKPaint _strokePaint;
    private DrawStroke _eraserDrawStroke = new(true);

    public EraseTool(string name, MainViewModel viewModel)
        : base(name, viewModel, LoadToolBitmap(typeof(EraseTool), "eraser.png"))
    {
        Cursor = new Cursor(ToolIcon, new PixelPoint(10, 40));
        _strokePaint = new SKPaint
        {
            IsAntialias = false,
            IsStroke = true,
            StrokeCap = SKStrokeCap.Round,
            StrokeWidth = 5,
        };
    }

    public override void HandlePointerMove(Point prevCoord, Point currentCoord)
    {
        _eraserDrawStroke.Path.LineTo((float)currentCoord.X, (float)currentCoord.Y);
        ViewModel.TriggerCanvasRedraw();
    }

    public override void HandlePointerClick(Point coord)
    {
        _eraserDrawStroke = new DrawStroke
        {
            Paint = _strokePaint.Clone()
        };
        _eraserDrawStroke.Path.MoveTo((float)coord.X, (float)coord.Y);

        ViewModel.AddStroke(_eraserDrawStroke);
    }

    public override bool RenderOptions(Panel parent)
    {
        // Render a slider for controlling the eraser thickness
        var slider = new Slider
        {
            TickFrequency = 5,
            IsSnapToTickEnabled = true,
            Minimum = 1,
            Maximum = 40,
            Value = _strokePaint.StrokeWidth
        };
        slider.ValueChanged += ((sender, args) => { _strokePaint.StrokeWidth = (float)args.NewValue; });
        slider.Padding = new Thickness(8, 0);

        parent.Children.Add(CreateOptionControl(slider, "Thickness"));
        return true;
    }
}