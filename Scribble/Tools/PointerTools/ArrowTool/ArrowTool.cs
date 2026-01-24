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
using Scribble.Lib;
using Scribble.Utils;
using Scribble.ViewModels;
using SkiaSharp;

namespace Scribble.Tools.PointerTools.ArrowTool;

public class ArrowTool : PointerToolsBase
{
    private readonly SKPaint _strokePaint;
    private SKPoint? _startPoint;
    private Guid _strokeId = Guid.NewGuid();
    private StrokeStyle _strokeStyle = StrokeStyle.Solid;

    public ArrowTool(string name, MainViewModel viewModel) : base(name, viewModel,
        LoadToolBitmap(typeof(ArrowTool), "arrow.png"))
    {
        var plusBitmap = new Bitmap(AssetLoader.Open(new Uri("avares://Scribble/Assets/plus.png")));
        Cursor = new Cursor(plusBitmap, new PixelPoint(12, 12));
        _strokePaint = new SKPaint
        {
            IsAntialias = true,
            IsStroke = true,
            StrokeCap = SKStrokeCap.Round,
            StrokeWidth = 4,
            Color = SKColors.Red
        };
        _startPoint = null;
    }

    public override void HandlePointerClick(Point coord)
    {
        _startPoint = new SKPoint((float)coord.X, (float)coord.Y);
        _strokeId = Guid.NewGuid();
        ViewModel.ApplyEvent(new StartStrokeEvent(_strokeId, _startPoint.Value, _strokePaint.Clone(),
            StrokeTool.Arrow));
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
                    _strokePaint.PathEffect = null;
                    _strokeStyle = StrokeStyle.Solid;
                    break;
                case "Dashed":
                    _strokePaint.PathEffect = SKPathEffect.CreateDash([8f, 14f], 0);
                    _strokeStyle = StrokeStyle.Dash;
                    break;
                case "Dotted":
                    _strokePaint.PathEffect = SKPathEffect.CreateDash([0f, 16f], 0);
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

    public static (SKPoint, SKPoint) GetArrowHeadPoints(SKPoint start, SKPoint end, float strokeWidth)
    {
        float arrowLength = strokeWidth * 10.0f;
        float arrowAngle = (float)(Math.PI / 6);

        // Calculate the angle of the main line
        float dy = end.Y - start.Y;
        float dx = end.X - start.X;
        float lineAngle = (float)Math.Atan2(dy, dx);

        // Calculate the angles for the left and right wings of the arrow head
        float leftWingAngle = lineAngle + (float)Math.PI - arrowAngle;
        float rightWingAngle = lineAngle + (float)Math.PI + arrowAngle;

        // Calculate the coordinates (Polar -> Cartesian)
        // x = r * cos(theta), y = r * sin(theta)
        SKPoint leftPoint = new SKPoint(
            end.X + arrowLength * (float)Math.Cos(leftWingAngle),
            end.Y + arrowLength * (float)Math.Sin(leftWingAngle)
        );

        SKPoint rightPoint = new SKPoint(
            end.X + arrowLength * (float)Math.Cos(rightWingAngle),
            end.Y + arrowLength * (float)Math.Sin(rightWingAngle)
        );

        return (leftPoint, rightPoint);
    }
}