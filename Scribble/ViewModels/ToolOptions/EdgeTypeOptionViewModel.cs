using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Scribble.Shared.Lib;

namespace Scribble.ViewModels.ToolOptions;

/// <summary>
/// View model for managing the edge type option
/// </summary>
public partial class EdgeTypeOptionViewModel(EdgeType initialEdgeType) : ToolOptionViewModelBase("Edges")
{
    [ObservableProperty] private EdgeType _edgeType = initialEdgeType;

    public Action<EdgeType>? EdgeTypeChanged;

    partial void OnEdgeTypeChanged(EdgeType value)
    {
        EdgeTypeChanged?.Invoke(value);
    }
}