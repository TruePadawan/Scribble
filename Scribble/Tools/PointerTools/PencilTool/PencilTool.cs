using System;
using Avalonia;
using Avalonia.Input;
using Scribble.Services.CanvasStateService;
using Scribble.Shared.Lib;
using SkiaSharp;

namespace Scribble.Tools.PointerTools.PencilTool;

public class PencilTool : StrokeTool
{
    private Guid _strokeId = Guid.NewGuid();
    private Guid _actionId = Guid.NewGuid();
    private SKPoint _lastAcceptedPoint;
    private const float DistanceThreshold = 2.0f;

    public PencilTool(string name, CanvasStateService canvasState) : base(name, canvasState,
        LoadToolBitmap(typeof(PencilTool), "pencil.png"))
    {
        ToolOptions = [ToolOption.StrokeColor, ToolOption.StrokeThickness];
        Cursor = new Cursor(ToolIcon, new PixelPoint(0, 20));
        HotKey = new KeyGesture(Key.D1);
        ToolTip = "Pencil Tool - 1";
    }

    public override void HandlePointerClick(SKPoint startPoint)
    {
        _strokeId = Guid.NewGuid();
        _actionId = Guid.NewGuid();
        _lastAcceptedPoint = startPoint;
        CanvasState.ApplyEvent(
            new StartStrokeEvent(_actionId, _strokeId, startPoint, StrokePaint.Clone(), ToolType.Pencil, ToolOptions));
    }

    public override void HandlePointerMove(SKPoint prevCoord, SKPoint currentCoord)
    {
        var distance = SKPoint.Distance(_lastAcceptedPoint, currentCoord);
        if (distance < DistanceThreshold)
        {
            return;
        }

        _lastAcceptedPoint = currentCoord;
        CanvasState.ApplyEvent(new PencilStrokeLineToEvent(_actionId, _strokeId, currentCoord));
    }

    public override void HandlePointerRelease(SKPoint prevCoord, SKPoint currentCoord)
    {
        CanvasState.ApplyEvent(new EndStrokeEvent(_actionId));
    }
}