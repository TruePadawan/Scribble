using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Scribble.Shared.Lib;

namespace Scribble.ViewModels.ToolOptions;

public abstract partial class ToolOptionViewModelBase : ObservableObject
{
    public string Label { get; }
    public ToolOption OptionType { get; }

    protected ToolOptionViewModelBase(string label, ToolOption optionType)
    {
        Label = label;
        OptionType = optionType;
    }

    protected static List<Color> GenerateMaterialPalette()
    {
        var palette = new MaterialHalfColorPalette();
        var colors = new List<Color>();

        // Loop through every base color (columns) and every shade (rows)
        for (int colorIndex = 0; colorIndex < palette.ColorCount; colorIndex++)
        {
            for (int shadeIndex = 0; shadeIndex < palette.ShadeCount; shadeIndex++)
            {
                colors.Add(palette.GetColor(colorIndex, shadeIndex));
            }
        }

        return colors;
    }
}