using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Scribble.Shared.Lib;

namespace Scribble.ViewModels.ToolOptions;

/// <summary>
/// View model for managing the font size option
/// </summary>
public partial class FontSizeOptionViewModel : ToolOptionViewModelBase
{
    [ObservableProperty] private float _fontSize;

    public Action<float>? FontSizeChanged { get; set; }

    public FontSizeOptionViewModel(float initialSize)
        : base("Font Size", ToolOption.FontSize)
    {
        _fontSize = initialSize;
    }

    partial void OnFontSizeChanged(float value)
    {
        FontSizeChanged?.Invoke(value);
    }
}