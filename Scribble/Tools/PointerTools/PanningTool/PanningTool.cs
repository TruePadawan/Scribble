using System;
using Avalonia;
using Avalonia.Input;
using Scribble.Services.CanvasStateService;
using Scribble.State;
using SkiaSharp;

namespace Scribble.Tools.PointerTools.PanningTool;

public class PanningTool : PointerTool
{
    private readonly Action _requestCanvasRedraw;

    public PanningTool(string name, CanvasStateService canvasState, Action requestCanvasRedraw)
        : base(name, canvasState, LoadToolBitmap(typeof(PanningTool), "pan.png"))
    {
        _requestCanvasRedraw = requestCanvasRedraw;
        Cursor = new Cursor(ToolIcon, new PixelPoint(10, 10));
        HotKey = new KeyGesture(Key.D3);
        ToolTip = "Panning Tool - 3";
    }

    /// <summary>
    /// PanningTool receives screen-space coordinates.
    /// The delta in screen pixels is divided by zoom
    /// to produce the equivalent world-space pan offset.
    /// </summary>
    public override void HandlePointerMove(SKPoint prevScreenPos, SKPoint currentScreenPos)
    {
        var screenDelta = currentScreenPos - prevScreenPos;
        // Convert screen-pixel delta to world-space delta
        // The camera is 'looking' at the direction opposite to where we're panning so we subtract
        CameraState.WorldOffSetX -= screenDelta.X / CameraState.Zoom;
        CameraState.WorldOffSetY -= screenDelta.Y / CameraState.Zoom;

        _requestCanvasRedraw();
    }
}