using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Scribble.Lib;
using Scribble.Utils;
using Scribble.ViewModels;
using SkiaSharp;

namespace Scribble.Tools.PointerTools.DrawTool;

public class DrawTool : PointerToolsBase
{
    private Stroke _currentStroke = new();
    private readonly SKPaint _strokePaint;

    public DrawTool(string name, MainViewModel viewModel) : base(name, viewModel,
        LoadToolBitmap(typeof(DrawTool), "draw.png"))
    {
        Cursor = new Cursor(ToolIcon, new PixelPoint(0, 50));
        _strokePaint = new SKPaint
        {
            IsAntialias = true,
            IsStroke = true,
            StrokeCap = SKStrokeCap.Round,
            StrokeWidth = 1,
            Color = SKColors.Red
        };
    }

    public override void HandlePointerMove(Point prevCoord, Point currentCoord)
    {
        _currentStroke.Path.LineTo((float)currentCoord.X, (float)currentCoord.Y);
        ViewModel.TriggerCanvasRedraw();
    }

    public override void HandlePointerClick(Point coord)
    {
        _currentStroke = new Stroke
        {
            Paint = _strokePaint.Clone()
        };
        _currentStroke.Path.MoveTo((float)coord.X, (float)coord.Y);

        ViewModel.AddStroke(_currentStroke);
    }

    public override bool RenderOptions(Panel parent)
    {
        // Render a slider for controlling the stroke width and a color picker for stroke color
        Slider slider = new Slider
        {
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Minimum = 1,
            Maximum = 10,
            Value = 1
        };
        slider.ValueChanged += ((sender, args) => { _strokePaint.StrokeWidth = (float)args.NewValue; });
        slider.Padding = new Thickness(8, 0);

        ColorPicker colorPicker = new ColorPicker
        {
            Color = Colors.Red
        };
        colorPicker.ColorChanged += (sender, args) =>
        {
            var newColor = args.NewColor;
            _strokePaint.Color = Utilities.ToSkColor(newColor);
        };

        parent.Children.Add(CreateOptionControl(colorPicker, "Color"));
        parent.Children.Add(CreateOptionControl(slider, "Thickness"));
        parent.Width = 180;
        return true;
    }
}