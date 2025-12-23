using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Scribble.ViewModels;

namespace Scribble.Tools.PointerTools;

public class PanningTool(string name, MainViewModel viewModel, IImage icon, ScrollViewer scrollViewer)
    : PointerToolsBase(name, viewModel, icon)
{
    public override void HandlePointerMove(Point prevCoord, Point currentCoord)
    {
        var distance = currentCoord - prevCoord;
        scrollViewer.Offset -= new Vector(distance.X, distance.Y);
    }
}