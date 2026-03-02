using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Scribble.ViewModels.ToolOptions;

/// <summary>
/// View model for managing the font size option
/// </summary>
public partial class FontSizeOptionViewModel(float initialSize) : ToolOptionViewModelBase("Font Size")
{
    [ObservableProperty] private float _fontSize = initialSize;

    public Action<float>? FontSizeChanged;

    partial void OnFontSizeChanged(float value)
    {
        FontSizeChanged?.Invoke(value);
    }
}