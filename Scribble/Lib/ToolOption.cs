using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Scribble.Behaviours;
using Scribble.Shared.Lib;

namespace Scribble.Lib;

public record ToolOptionsValues
{
    public float StrokeThickness { get; set; } = 1f;
    public Color StrokeColor { get; set; } = Colors.White;
    public StrokeStyle StrokeStyle { get; set; } = StrokeStyle.Solid;
    public float[]? DashIntervals { get; set; }
    public Color FillColor { get; set; } = Colors.Transparent;
    public EdgeType EdgeType { get; set; } = EdgeType.Sharp;
    public float FontSize { get; set; } = 10;
}

internal static class ToolOptionsControlFactory
{
    public static ColorPicker GetStrokeColorOption()
    {
        var colorPicker = new ColorPicker
        {
            IsColorSpectrumSliderVisible = false,
            Width = 164
        };
        return colorPicker;
    }

    public static Slider GetStrokeThicknessOption()
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

    public static StackPanel GetStrokeStyleOption(StrokeStyle selectedStyle)
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
            IsChecked = selectedStyle == StrokeStyle.Solid,
            Content = new Image { Source = solidStyleIcon }
        };
        ToggleButtonGroup.SetGroupName(solidStyle, "LineStyle");
        var dashedStyle = new ToggleButton
        {
            Name = "Dashed",
            Width = 36,
            Height = 36,
            IsChecked = selectedStyle == StrokeStyle.Dash,
            Content = new Image { Source = dashedStyleIcon }
        };
        ToggleButtonGroup.SetGroupName(dashedStyle, "LineStyle");
        var dottedStyle = new ToggleButton
        {
            Name = "Dotted",
            Width = 36,
            Height = 36,
            IsChecked = selectedStyle == StrokeStyle.Dotted,
            Content = new Image { Source = dottedStyleIcon }
        };
        ToggleButtonGroup.SetGroupName(dottedStyle, "LineStyle");
        strokeStylePanel.Children.AddRange([solidStyle, dashedStyle, dottedStyle]);
        return strokeStylePanel;
    }

    public static StackPanel GetFillColorOption()
    {
        var stackPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8f
        };

        var colorPicker = new ColorPicker
        {
            Color = Colors.Transparent,
            IsColorSpectrumSliderVisible = false,
            Width = 124
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

        stackPanel.Children.AddRange([transparentColorBtn, colorPicker]);
        return stackPanel;
    }

    public static StackPanel GetEdgesOption(EdgeType selectedEdgeType)
    {
        var edgesPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8f
        };

        var sharpEdgesIcon =
            Bitmap.DecodeToWidth(
                AssetLoader.Open(new Uri("avares://Scribble/Assets/sharp_edge.png")), 20);
        var roundedEdgesIcon =
            Bitmap.DecodeToWidth(
                AssetLoader.Open(new Uri("avares://Scribble/Assets/rounded_edge.png")), 20);
        var sharpEdgeBtn = new ToggleButton
        {
            Name = "Sharp",
            Width = 36,
            Height = 36,
            IsChecked = selectedEdgeType == EdgeType.Sharp,
            Content = new Image { Source = sharpEdgesIcon }
        };
        ToggleButtonGroup.SetGroupName(sharpEdgeBtn, "EdgeStyle");

        var roundedEdgeBtn = new ToggleButton
        {
            Name = "Rounded",
            Width = 36,
            Height = 36,
            IsChecked = selectedEdgeType == EdgeType.Rounded,
            Content = new Image { Source = roundedEdgesIcon }
        };
        ToggleButtonGroup.SetGroupName(roundedEdgeBtn, "EdgeStyle");

        edgesPanel.Children.AddRange([sharpEdgeBtn, roundedEdgeBtn]);
        return edgesPanel;
    }

    public static Slider GetFontSizeOption()
    {
        var slider = new Slider
        {
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Minimum = 10,
            Maximum = 40,
            Padding = new Thickness(8, 0)
        };
        return slider;
    }
}