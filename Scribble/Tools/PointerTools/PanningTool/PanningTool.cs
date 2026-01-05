using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Scribble.ViewModels;

namespace Scribble.Tools.PointerTools.PanningTool;

public class PanningTool : PointerToolsBase
{
    private readonly ScrollViewer _scrollViewer;

    public PanningTool(string name, MainViewModel viewModel, ScrollViewer scrollViewer)
        : base(name, viewModel, LoadToolBitmap(typeof(PanningTool), "hand.png"))
    {
        _scrollViewer = scrollViewer;
        Cursor = new Cursor(ToolIcon, new PixelPoint(25, 25));
    }

    public override void HandlePointerMove(Point prevCoord, Point currentCoord)
    {
        var distance = currentCoord - prevCoord;
        _scrollViewer.Offset -= new Vector(distance.X, distance.Y);
    }
}