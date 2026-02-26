using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Scribble.Shared.Lib;

namespace Scribble.ViewModels.ToolOptions;

public partial class StrokeStyleOptionViewModel : ToolOptionViewModelBase
{
    [ObservableProperty] private StrokeStyle _strokeStyle;

    public Action<StrokeStyle, float[]?>? StyleChanged { get; set; }

    public StrokeStyleOptionViewModel(StrokeStyle initialStyle)
        : base("Stroke Style", ToolOption.StrokeStyle)
    {
        _strokeStyle = initialStyle;
    }

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
