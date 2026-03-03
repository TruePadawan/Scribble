using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Scribble.ViewModels.ToolOptions;

/// <summary>
/// View model for managing the stroke thickness option
/// </summary>
public partial class StrokeThicknessOptionViewModel(float initialValue) : ToolOptionViewModelBase("Stroke Thickness")
{
    [ObservableProperty] private float _thickness = initialValue;

    public Action<float>? ThicknessChanged;

    partial void OnThicknessChanged(float value)
    {
        ThicknessChanged?.Invoke(value);
    }
}