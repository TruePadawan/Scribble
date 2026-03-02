using CommunityToolkit.Mvvm.ComponentModel;

namespace Scribble.ViewModels.ToolOptions;

/// <summary>
/// Base class for all tool options view models
/// </summary>
public abstract class ToolOptionViewModelBase(string label) : ObservableObject
{
    public string Label { get; } = label;
}