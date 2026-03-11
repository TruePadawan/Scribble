using System;
using Avalonia;
using Avalonia.Input;
using Scribble.Services;
using Scribble.Shared.Lib;
using SkiaSharp;

namespace Scribble.Tools.PointerTools.PencilTool;

public class PencilTool : StrokeTool
{
    private Guid _strokeId = Guid.NewGuid();
    private Guid _actionId = Guid.NewGuid();

    public PencilTool(string name, CanvasStateService canvasState) : base(name, canvasState,
        LoadToolBitmap(typeof(PencilTool), "pencil.png"))
    {
        ToolOptions = [ToolOption.StrokeColor, ToolOption.StrokeThickness];
        Cursor = new Cursor(ToolIcon.CreateScaledBitmap(new PixelSize(36, 36)), new PixelPoint(0, 36));
        HotKey = new KeyGesture(Key.D1);
        ToolTip = "Pencil Tool - 1";
    }

    public override void HandlePointerClick(Point coord)
    {
        var startPoint = new SKPoint((float)coord.X, (float)coord.Y);
        _strokeId = Guid.NewGuid();
        _actionId = Guid.NewGuid();
        CanvasState.ApplyEvent(
            new StartStrokeEvent(_actionId, _strokeId, startPoint, StrokePaint.Clone(), ToolType.Pencil, ToolOptions));
    }

    public override void HandlePointerMove(Point prevCoord, Point currentCoord)
    {
        var nextPoint = new SKPoint((float)currentCoord.X, (float)currentCoord.Y);
        CanvasState.ApplyEvent(new PencilStrokeLineToEvent(_actionId, _strokeId, nextPoint));
    }

    public override void HandlePointerRelease(Point prevCoord, Point currentCoord)
    {
        CanvasState.ApplyEvent(new EndStrokeEvent(_actionId));
    }
}