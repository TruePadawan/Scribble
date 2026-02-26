using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Scribble.Shared.Lib;

namespace Scribble.ViewModels.ToolOptions;

public partial class EdgeTypeOptionViewModel : ToolOptionViewModelBase
{
    [ObservableProperty] private EdgeType _edgeType;

    public Action<EdgeType>? EdgeTypeChanged { get; set; }

    public EdgeTypeOptionViewModel(EdgeType initialEdgeType)
        : base("Edges", ToolOption.EdgeType)
    {
        _edgeType = initialEdgeType;
    }

    partial void OnEdgeTypeChanged(EdgeType value)
    {
        EdgeTypeChanged?.Invoke(value);
    }
}
