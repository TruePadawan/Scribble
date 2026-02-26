using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Scribble.Shared.Lib;

namespace Scribble.ViewModels.ToolOptions;

public partial class StrokeThicknessOptionViewModel : ToolOptionViewModelBase
{
    [ObservableProperty] private float _thickness;

    public Action<float>? ThicknessChanged { get; set; }

    public StrokeThicknessOptionViewModel(float initialValue)
        : base("Stroke Thickness", ToolOption.StrokeThickness)
    {
        _thickness = initialValue;
    }

    partial void OnThicknessChanged(float value)
    {
        ThicknessChanged?.Invoke(value);
    }
}
