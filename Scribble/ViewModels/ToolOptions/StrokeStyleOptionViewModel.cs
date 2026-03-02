using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Scribble.Shared.Lib;

namespace Scribble.ViewModels.ToolOptions;

/// <summary>
/// View model for managing the stroke style option
/// </summary>
public partial class StrokeStyleOptionViewModel(StrokeStyle initialStyle) : ToolOptionViewModelBase("Stroke Style")
{
    [ObservableProperty] private StrokeStyle _strokeStyle = initialStyle;

    public Action<StrokeStyle, float[]?>? StyleChanged;

    partial void OnStrokeStyleChanged(StrokeStyle value)
    {
        float[]? dashIntervals = value switch
        {
            StrokeStyle.Dash => [8f, 14f],
            StrokeStyle.Dotted => [0f, 16f],
            _ => null
        };
        StyleChanged?.Invoke(value, dashIntervals);
    }
}