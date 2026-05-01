using Avalonia;
using Avalonia.Input;
using Scribble.Services;
using Scribble.State;
using System;

namespace Scribble.Tools.PointerTools.PanningTool;

public class PanningTool : PointerTool
{
    private readonly Action _requestCanvasRedraw;

    public PanningTool(string name, CanvasStateService canvasState, Action requestCanvasRedraw)
        : base(name, canvasState, LoadToolBitmap(typeof(PanningTool), "hand.png"))
    {
        _requestCanvasRedraw = requestCanvasRedraw;
        Cursor = new Cursor(ToolIcon.CreateScaledBitmap(new PixelSize(30, 30)), new PixelPoint(15, 15));
        HotKey = new KeyGesture(Key.D3);
        ToolTip = "Panning Tool - 3";
    }

    /// <summary>
    /// PanningTool receives screen-space coordinates.
    /// The delta in screen pixels is divided by zoom
    /// to produce the equivalent world-space pan offset.
    /// </summary>
    public override void HandlePointerMove(Point prevScreenPos, Point currentScreenPos)
    {
        var screenDelta = currentScreenPos - prevScreenPos;
        // Convert screen-pixel delta to world-space delta
        CameraState.WorldOffSetX -= (float)(screenDelta.X / CameraState.Zoom);
        CameraState.WorldOffSetY -= (float)(screenDelta.Y / CameraState.Zoom);

        _requestCanvasRedraw();
    }
}