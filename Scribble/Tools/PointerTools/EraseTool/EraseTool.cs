using System;
using Avalonia;
using Avalonia.Input;
using Scribble.Services.CanvasStateService;
using Scribble.Shared.Lib.Events;
using SkiaSharp;

namespace Scribble.Tools.PointerTools.EraseTool;

public class EraseTool : PointerTool
{
    private Guid _strokeId = Guid.NewGuid();
    private Guid _actionId = Guid.NewGuid();

    public EraseTool(string name, ICanvasStateService canvasState)
        : base(name, canvasState, LoadToolBitmap(typeof(EraseTool), "eraser.png"))
    {
        Cursor = new Cursor(ToolIcon, new PixelPoint(6, 16));
        HotKey = new KeyGesture(Key.D2);
        ToolTip = "Erase Tool - 2";
    }

    public override void HandlePointerClick(SKPoint startPoint)
    {
        _strokeId = Guid.NewGuid();
        _actionId = Guid.NewGuid();
        CanvasState.ApplyEvent(new StartEraseStrokeEvent(_actionId, _strokeId, startPoint));
    }

    public override void HandlePointerMove(SKPoint prevCoord, SKPoint currentCoord)
    {
        CanvasState.ApplyEvent(new EraseStrokeLineToEvent(_actionId, _strokeId, currentCoord));
    }

    public override void HandlePointerRelease(SKPoint prevCoord, SKPoint currentCoord)
    {
        CanvasState.ApplyEvent(new TriggerEraseEvent(_actionId, _strokeId));
    }
}