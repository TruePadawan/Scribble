using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Scribble.ViewModels.ToolOptions;

/// <summary>
/// View model for managing the fill color option
/// </summary>
public partial class FillColorOptionViewModel(Color initialColor) : ToolOptionViewModelBase("Fill Color")
{
    [ObservableProperty] private Color _fillColor = initialColor;

    public Action<Color>? FillColorChanged;

    [RelayCommand]
    private void SetTransparent()
    {
        FillColor = Colors.Transparent;
    }

    partial void OnFillColorChanged(Color value)
    {
        FillColorChanged?.Invoke(value);
    }
}