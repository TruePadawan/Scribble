using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Scribble.Lib;
using Scribble.Utils;
using Scribble.ViewModels;
using SkiaSharp;

namespace Scribble.Tools.PointerTools.RectangleTool;

public class RectangleTool : PointerToolsBase
{
    private readonly SKPaint _strokePaint;
    private SKPoint? _startPoint;
    private Guid _strokeId = Guid.NewGuid();

    public RectangleTool(string name, MainViewModel viewModel) : base(name, viewModel,
        LoadToolBitmap(typeof(RectangleTool), "rectangle.png"))
    {
        var plusBitmap = new Bitmap(AssetLoader.Open(new Uri("avares://Scribble/Assets/plus.png")));
        Cursor = new Cursor(plusBitmap, new PixelPoint(12, 12));
        _strokePaint = new SKPaint
        {
            IsAntialias = true,
            IsStroke = true,
            StrokeCap = SKStrokeCap.Round,
            StrokeWidth = 1,
            Color = SKColors.Red
        };
        _startPoint = null;
    }

    public override void HandlePointerClick(Point coord)
    {
        _startPoint = new SKPoint((float)coord.X, (float)coord.Y);
        _strokeId = Guid.NewGuid();
        ViewModel.ApplyEvent(new StartStrokeEvent(_strokeId, _startPoint.Value, _strokePaint.Clone(),
            StrokeTool.Rectangle));
    }


    public override void HandlePointerMove(Point prevCoord, Point currentCoord)
    {
        var endPoint = new SKPoint((float)currentCoord.X, (float)currentCoord.Y);
        ViewModel.ApplyEvent(new LineStrokeLineToEvent(_strokeId, endPoint));
    }

    public override void HandlePointerRelease(Point prevCoord, Point currentCoord)
    {
        ViewModel.ApplyEvent(new EndStrokeEvent(_strokeId));
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