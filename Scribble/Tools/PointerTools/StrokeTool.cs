using System.Collections.Generic;
using Avalonia.Media.Imaging;
using Scribble.Lib;
using Scribble.Shared.Lib;
using Scribble.ViewModels;

namespace Scribble.Tools.PointerTools;

public class StrokeTool(string name, MainViewModel viewModel, Bitmap icon) : PointerTool(name, viewModel, icon)
{
    public StrokePaint StrokePaint { get; protected init; } = new();
    public HashSet<ToolOption> ToolOptions { get; protected init; } = [];
}