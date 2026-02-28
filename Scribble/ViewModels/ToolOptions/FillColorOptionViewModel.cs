using System;
using System.Collections.Generic;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Scribble.Shared.Lib;

namespace Scribble.ViewModels.ToolOptions;

public partial class FillColorOptionViewModel : ToolOptionViewModelBase
{
    [ObservableProperty] private Color _fillColor;

    public Action<Color>? FillColorChanged { get; set; }
    public List<Color> PaletteColors { get; } = GenerateMaterialPalette();

    public FillColorOptionViewModel(Color initialColor)
        : base("Fill Color", ToolOption.FillColor)
    {
        _fillColor = initialColor;
    }

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