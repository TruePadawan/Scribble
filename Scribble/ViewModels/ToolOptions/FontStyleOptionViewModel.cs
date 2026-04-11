using System;
using CommunityToolkit.Mvvm.Input;
using Scribble.Shared.Lib.CanvasElements.Strokes;

namespace Scribble.ViewModels.ToolOptions;

public partial class FontStyleOptionViewModel() : ToolOptionViewModelBase("Font Style")
{
    public Action<FontStyle>? FontStyleChanged;

    [RelayCommand]
    private void SetBoldStyle()
    {
        FontStyleChanged?.Invoke(FontStyle.Bold);
    }

    [RelayCommand]
    private void SetItalicStyle()
    {
        FontStyleChanged?.Invoke(FontStyle.Italic);
    }

    [RelayCommand]
    private void SetNormalStyle()
    {
        FontStyleChanged?.Invoke(FontStyle.Normal);
    }
}