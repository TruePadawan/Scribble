using System;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;

namespace Scribble.ViewModels.ToolOptions;

public partial class FontStyleOptionViewModel() : ToolOptionViewModelBase("Font Style")
{
    public Action<Text>? FontStyleChanged;

    [RelayCommand]
    private void SetBoldStyle()
    {
        FontStyleChanged?.Invoke();
    }
}