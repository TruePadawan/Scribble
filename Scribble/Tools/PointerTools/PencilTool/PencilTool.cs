using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Scribble.Lib;
using Scribble.Utils;
using Scribble.ViewModels;
using SkiaSharp;

namespace Scribble.Tools.PointerTools.PencilTool;

public class PencilTool : PointerToolsBase
{
    private readonly SKPaint _strokePaint;
    private Guid _currentStrokeId = Guid.NewGuid();

    public PencilTool(string name, MainViewModel viewModel) : base(name, viewModel,
        LoadToolBitmap(typeof(PencilTool), "pencil.png"))
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
        var nextPoint = new SKPoint((float)currentCoord.X, (float)currentCoord.Y);
        ViewModel.ApplyEvent(new PencilStrokeLineToEvent(_currentStrokeId, nextPoint));
    }

    public override void HandlePointerClick(Point coord)
    {
        var startPoint = new SKPoint((float)coord.X, (float)coord.Y);
        _currentStrokeId = Guid.NewGuid();
        ViewModel.ApplyEvent(new StartStrokeEvent(_currentStrokeId, startPoint, _strokePaint.Clone(), StrokeTool.Pencil));
    }

    public override void HandlePointerRelease(Point prevCoord, Point currentCoord)
    {
        ViewModel.ApplyEvent(new EndStrokeEvent(_currentStrokeId));
    }

    public override bool RenderOptions(Panel parent)
    {
        // Render a slider for controlling the stroke width and a color picker for stroke color
        var slider = new Slider
        {
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Minimum = 1,
            Maximum = 10,
            Value = _strokePaint.StrokeWidth
        };
        slider.ValueChanged += ((sender, args) => { _strokePaint.StrokeWidth = (float)args.NewValue; });
        slider.Padding = new Thickness(8, 0);

        var colorPicker = new ColorPicker
        {
            Color = Utilities.FromSkColor(_strokePaint.Color),
            IsColorSpectrumSliderVisible = false,
            Width = 164
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