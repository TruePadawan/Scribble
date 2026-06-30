using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Scribble.Services.CanvasStateService;
using Scribble.Shared.Lib;
using SkiaSharp;

namespace Scribble.Tools.PointerTools.LineTool;

public class LineTool : StrokeTool
{
    private Guid _strokeId = Guid.NewGuid();
    private Guid _actionId = Guid.NewGuid();

    public LineTool(string name, CanvasStateService canvasState) : base(name, canvasState,
        LoadToolBitmap(typeof(LineTool), "line.png"))
    {
        ToolOptions = [ToolOption.StrokeColor, ToolOption.StrokeThickness, ToolOption.StrokeStyle];
        var plusBitmap = new Bitmap(AssetLoader.Open(new Uri("avares://Scribble/Assets/plus.png")));
        Cursor = new Cursor(plusBitmap, new PixelPoint(12, 12));
        HotKey = new KeyGesture(Key.D4);
        ToolTip = "Line Tool - 4";
    }

    public override void HandlePointerClick(SKPoint startPoint)
    {
        _strokeId = Guid.NewGuid();
        _actionId = Guid.NewGuid();
        CanvasState.ApplyEvent(new StartStrokeEvent(_actionId, _strokeId, startPoint, StrokePaint.Clone(),
            ToolType.Line, ToolOptions));
    }

    public override void HandlePointerMove(SKPoint prevCoord, SKPoint currentCoord)
    {
        CanvasState.ApplyEvent(new LineStrokeLineToEvent(_actionId, _strokeId, currentCoord));
    }

    public override void HandlePointerRelease(SKPoint prevCoord, SKPoint currentCoord)
    {
        CanvasState.ApplyEvent(new EndStrokeEvent(_actionId));
    }
}