using System;
using Avalonia;
using Avalonia.Input;
using Scribble.Services.CanvasState;
using Scribble.Shared.Lib;
using SkiaSharp;

namespace Scribble.Tools.PointerTools.EraseTool;

public class EraseTool : PointerTool
{
    private Guid _strokeId = Guid.NewGuid();
    private Guid _actionId = Guid.NewGuid();

    public EraseTool(string name, CanvasStateService canvasState)
        : base(name, canvasState, LoadToolBitmap(typeof(EraseTool), "eraser.png"))
    {
        Cursor = new Cursor(ToolIcon.CreateScaledBitmap(new PixelSize(36, 36)), new PixelPoint(10, 30));
        HotKey = new KeyGesture(Key.D2);
        ToolTip = "Erase Tool - 2";
    }

    public override void HandlePointerClick(Point coord)
    {
        var startPoint = new SKPoint((float)coord.X, (float)coord.Y);
        _strokeId = Guid.NewGuid();
        _actionId = Guid.NewGuid();
        CanvasState.ApplyEvent(new StartEraseStrokeEvent(_actionId, _strokeId, startPoint));
    }

    public override void HandlePointerMove(Point prevCoord, Point currentCoord)
    {
        var nextPoint = new SKPoint((float)currentCoord.X, (float)currentCoord.Y);
        CanvasState.ApplyEvent(new EraseStrokeLineToEvent(_actionId, _strokeId, nextPoint));
    }

    public override void HandlePointerRelease(Point prevCoord, Point currentCoord)
    {
        CanvasState.ApplyEvent(new TriggerEraseEvent(_actionId, _strokeId));
    }
}