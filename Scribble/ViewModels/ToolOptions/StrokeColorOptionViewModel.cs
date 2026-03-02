using System;
using System.Collections.Generic;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Scribble.Shared.Lib;

namespace Scribble.ViewModels.ToolOptions;

/// <summary>
/// View model for managing the stroke color option
/// </summary>
public partial class StrokeColorOptionViewModel : ToolOptionViewModelBase
{
    [ObservableProperty] private Color _color;

    public Action<Color>? ColorChanged { get; set; }

    public StrokeColorOptionViewModel(Color initialColor)
        : base("Stroke Color", ToolOption.StrokeColor)
    {
        _color = initialColor;
    }

    partial void OnColorChanged(Color value)
    {
        ColorChanged?.Invoke(value);
    }
}