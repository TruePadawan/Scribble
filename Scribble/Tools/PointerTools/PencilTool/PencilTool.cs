using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Scribble.Shared.Lib;
using Scribble.Utils;
using Scribble.ViewModels;
using SkiaSharp;

namespace Scribble.Tools.PointerTools.PencilTool;

public class PencilTool : PointerToolsBase
{
    private readonly StrokePaint _strokePaint;
    private Guid _strokeId = Guid.NewGuid();
    private Guid _actionId = Guid.NewGuid();

    public PencilTool(string name, MainViewModel viewModel) : base(name, viewModel,
        LoadToolBitmap(typeof(PencilTool), "pencil.png"))
    {
        Cursor = new Cursor(ToolIcon.CreateScaledBitmap(new PixelSize(36, 36)), new PixelPoint(0, 36));
        _strokePaint = new StrokePaint
        {
            IsAntialias = true,
            IsStroke = true,
            StrokeCap = SKStrokeCap.Round,
            StrokeWidth = 1,
            Color = SKColors.Red
        };
    }

    public override void HandlePointerClick(Point coord)
    {
        var startPoint = new SKPoint((float)coord.X, (float)coord.Y);
        _strokeId = Guid.NewGuid();
        _actionId = Guid.NewGuid();
        ViewModel.ApplyEvent(
            new StartStrokeEvent(_actionId, _strokeId, startPoint, _strokePaint.Clone(), StrokeTool.Pencil));
    }

    public override void HandlePointerMove(Point prevCoord, Point currentCoord)
    {
        var nextPoint = new SKPoint((float)currentCoord.X, (float)currentCoord.Y);
        ViewModel.ApplyEvent(new PencilStrokeLineToEvent(_actionId, _strokeId, nextPoint));
    }

    public override void HandlePointerRelease(Point prevCoord, Point currentCoord)
    {
        ViewModel.ApplyEvent(new EndStrokeEvent(_actionId));
    }

    public override bool RenderOptions(Panel parent)
    {
        var strokeColorPicker = GetStrokeColorOption();
        var thicknessSlider = GetStrokeThicknessOption();

        strokeColorPicker.Color = Utilities.FromSkColor(_strokePaint.Color);
        strokeColorPicker.ColorChanged += (sender, args) =>
        {
            _strokePaint.Color = Utilities.ToSkColor(args.NewColor);
        };

        thicknessSlider.Value = _strokePaint.StrokeWidth;
        thicknessSlider.ValueChanged += (sender, args) =>
        {
            var newThickness = (float)args.NewValue;
            _strokePaint.StrokeWidth = newThickness;
        };


        parent.Children.Add(CreateOptionControl(strokeColorPicker, "Stroke Color"));
        parent.Children.Add(CreateOptionControl(thicknessSlider, "Stroke Thickness"));
        parent.Width = 180;
        return true;
    }

    private static ColorPicker GetStrokeColorOption()
    {
        var colorPicker = new ColorPicker
        {
            IsColorSpectrumSliderVisible = false,
            Width = 164
        };
        return colorPicker;
    }

    private static Slider GetStrokeThicknessOption()
    {
        var slider = new Slider
        {
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Minimum = 1,
            Maximum = 10,
            Padding = new Thickness(8, 0),
        };
        return slider;
    }


    public static void RenderEditOptions(Panel parent, List<Guid> strokeIds, MainViewModel viewModel)
    {
        var strokeColorPicker = GetStrokeColorOption();
        var thicknessSlider = GetStrokeThicknessOption();

        strokeColorPicker.Color = Colors.White;
        strokeColorPicker.ColorChanged += (sender, args) =>
        {
            var newColor = Utilities.ToSkColor(args.NewColor);
            viewModel.ApplyEvent(new UpdateStrokeColorEvent(Guid.NewGuid(), strokeIds, newColor));
        };

        thicknessSlider.Value = 1;
        thicknessSlider.ValueChanged += (sender, args) =>
        {
            var newThickness = (float)args.NewValue;
            viewModel.ApplyEvent(new UpdateStrokeThicknessEvent(Guid.NewGuid(), strokeIds, newThickness));
        };

        parent.Children.Add(CreateOptionControl(strokeColorPicker, "Stroke Color"));
        parent.Children.Add(CreateOptionControl(thicknessSlider, "Stroke Thickness"));
        parent.Width = 180;
    }
}