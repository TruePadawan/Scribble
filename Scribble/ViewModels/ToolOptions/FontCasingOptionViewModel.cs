using System;
using CommunityToolkit.Mvvm.Input;
using Scribble.Shared.Lib.CanvasElements.Strokes;

namespace Scribble.ViewModels.ToolOptions;

public partial class FontCasingOptionViewModel() : ToolOptionViewModelBase("Text Casing")
{
    public Action<FontCasing>? FontCasingChanged;

    [RelayCommand]
    private void SetUppercase()
    {
        FontCasingChanged?.Invoke(FontCasing.UpperCase);
    }

    [RelayCommand]
    private void SetLowercase()
    {
        FontCasingChanged?.Invoke(FontCasing.LowerCase);
    }
}