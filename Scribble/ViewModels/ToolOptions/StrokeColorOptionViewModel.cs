using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Scribble.ViewModels.ToolOptions;

/// <summary>
/// View model for managing the stroke color option
/// </summary>
public partial class StrokeColorOptionViewModel(Color initialColor) : ToolOptionViewModelBase("Stroke Color")
{
    [ObservableProperty] private Color _color = initialColor;

    public Action<Color>? ColorChanged;

    partial void OnColorChanged(Color value)
    {
        ColorChanged?.Invoke(value);
    }
}