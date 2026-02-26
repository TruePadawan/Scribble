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
}
