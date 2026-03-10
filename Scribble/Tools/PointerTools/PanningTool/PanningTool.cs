using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Scribble.Services.CanvasState;

namespace Scribble.Tools.PointerTools.PanningTool;

public class PanningTool : PointerTool
{
    private readonly ScrollViewer _scrollViewer;

    public PanningTool(string name, CanvasStateService canvasState, ScrollViewer scrollViewer)
        : base(name, canvasState, LoadToolBitmap(typeof(PanningTool), "hand.png"))
    {
        _scrollViewer = scrollViewer;
        Cursor = new Cursor(ToolIcon.CreateScaledBitmap(new PixelSize(30, 30)), new PixelPoint(15, 15));
        HotKey = new KeyGesture(Key.D3);
        ToolTip = "Panning Tool - 3";
    }

    public override void HandlePointerMove(Point prevCoord, Point currentCoord)
    {
        var distance = currentCoord - prevCoord;
        _scrollViewer.Offset -= new Vector(distance.X, distance.Y);
    }
}