using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Scribble.Behaviours;
using Scribble.Shared.Lib;
using Scribble.Utils;
using Scribble.ViewModels;
using SkiaSharp;

namespace Scribble.Tools.PointerTools.EllipseTool;

public class EllipseTool : PointerToolsBase
{
    private readonly StrokePaint _strokePaint;
    private SKPoint? _startPoint;
    private Guid _strokeId = Guid.NewGuid();
    private StrokeStyle _strokeStyle = StrokeStyle.Solid;

    public EllipseTool(string name, MainViewModel viewModel) : base(name, viewModel,
        LoadToolBitmap(typeof(EllipseTool), "ellipse.png"))
    {
        var plusBitmap = new Bitmap(AssetLoader.Open(new Uri("avares://Scribble/Assets/plus.png")));
        Cursor = new Cursor(plusBitmap, new PixelPoint(12, 12));
        _strokePaint = new StrokePaint
        {
            IsAntialias = true,
            IsStroke = true,
            StrokeWidth = 1,
            Color = SKColors.Red,
        };
        _startPoint = null;
    }

    public override void HandlePointerClick(Point coord)
    {
        _startPoint = new SKPoint((float)coord.X, (float)coord.Y);
        _strokeId = Guid.NewGuid();
        ViewModel.ApplyEvent(new StartStrokeEvent(_strokeId, _startPoint.Value, _strokePaint.Clone(),
            StrokeTool.Ellipse));
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
        parent.Children.Add(CreateOptionControl(GetStrokeColorOption(), "Stroke Color"));
        parent.Children.Add(CreateOptionControl(GetFillColorOption(), "Fill Color"));
        parent.Children.Add(CreateOptionControl(GetStrokeThicknessOption(), "Stroke Thickness"));
        parent.Children.Add(CreateOptionControl(GetStrokeStyleOption(), "Stroke style"));
        parent.Width = 180;
        return true;
    }

    private void StrokeStyleChangeHandler(object? sender, RoutedEventArgs args)
    {
        if (sender is ToggleButton { IsChecked: true } toggleButton)
        {
            switch (toggleButton.Name)
            {
                case "Solid":
                    _strokePaint.DashIntervals = null;
                    _strokeStyle = StrokeStyle.Solid;
                    break;
                case "Dashed":
                    _strokePaint.DashIntervals = [8f, 14f];
                    _strokeStyle = StrokeStyle.Dash;
                    break;
                case "Dotted":
                    _strokePaint.DashIntervals = [0f, 16f];
                    _strokeStyle = StrokeStyle.Dotted;
                    break;
            }
        }
    }

    private StackPanel GetStrokeStyleOption()
    {
        var strokeStylePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8f
        };
        var solidStyleIcon =
            Bitmap.DecodeToWidth(AssetLoader.Open(new Uri("avares://Scribble/Assets/line.png")), 20);
        var dashedStyleIcon =
            Bitmap.DecodeToWidth(AssetLoader.Open(new Uri("avares://Scribble/Assets/dashed_line.png")), 20);
        var dottedStyleIcon =
            Bitmap.DecodeToWidth(AssetLoader.Open(new Uri("avares://Scribble/Assets/dotted_line.png")), 20);
        var solidStyle = new ToggleButton
        {
            Name = "Solid",
            Width = 36,
            Height = 36,
            IsChecked = _strokeStyle == StrokeStyle.Solid,
            Content = new Image { Source = solidStyleIcon }
        };
        ToggleButtonGroup.SetGroupName(solidStyle, "LineStyle");
        solidStyle.IsCheckedChanged += StrokeStyleChangeHandler;
        var dashedStyle = new ToggleButton
        {
            Name = "Dashed",
            Width = 36,
            Height = 36,
            IsChecked = _strokeStyle == StrokeStyle.Dash,
            Content = new Image { Source = dashedStyleIcon }
        };
        dashedStyle.IsCheckedChanged += StrokeStyleChangeHandler;
        ToggleButtonGroup.SetGroupName(dashedStyle, "LineStyle");
        var dottedStyle = new ToggleButton
        {
            Name = "Dotted",
            Width = 36,
            Height = 36,
            IsChecked = _strokeStyle == StrokeStyle.Dotted,
            Content = new Image { Source = dottedStyleIcon }
        };
        dottedStyle.IsCheckedChanged += StrokeStyleChangeHandler;
        ToggleButtonGroup.SetGroupName(dottedStyle, "LineStyle");
        strokeStylePanel.Children.AddRange([solidStyle, dashedStyle, dottedStyle]);
        return strokeStylePanel;
    }

    private ColorPicker GetStrokeColorOption()
    {
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
        return colorPicker;
    }

    private Slider GetStrokeThicknessOption()
    {
        var slider = new Slider
        {
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Minimum = 1,
            Maximum = 10,
            Value = _strokePaint.StrokeWidth
        };
        slider.ValueChanged += (sender, args) => { _strokePaint.StrokeWidth = (float)args.NewValue; };
        slider.Padding = new Thickness(8, 0);
        return slider;
    }

    private StackPanel GetFillColorOption()
    {
        var stackPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8f
        };

        var colorPicker = new ColorPicker
        {
            Color = Utilities.FromSkColor(_strokePaint.FillColor),
            IsColorSpectrumSliderVisible = false,
            Width = 124
        };
        colorPicker.ColorChanged += (sender, args) =>
        {
            var newColor = args.NewColor;
            _strokePaint.FillColor = Utilities.ToSkColor(newColor);
        };

        var transparentImage = new Image
        {
            Source = new Bitmap(AssetLoader.Open(new Uri("avares://Scribble/Assets/transparent.png"))),
        };
        var transparentColorBtn = new Button
        {
            Content = transparentImage,
            Width = 30,
            Height = 30,
            Padding = new Thickness(0)
        };
        transparentColorBtn.Click += (sender, args) =>
        {
            colorPicker.Color = Utilities.FromSkColor(SKColors.Transparent);
        };

        stackPanel.Children.AddRange([transparentColorBtn, colorPicker]);
        return stackPanel;
    }
}