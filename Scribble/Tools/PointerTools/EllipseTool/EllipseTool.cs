using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Scribble.Services.CanvasStateService;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.Events;
using SkiaSharp;

namespace Scribble.Tools.PointerTools.EllipseTool;

public class EllipseTool : StrokeTool
{
    private Guid _strokeId = Guid.NewGuid();
    private Guid _actionId = Guid.NewGuid();

    public EllipseTool(string name, ICanvasStateService canvasState) : base(name, canvasState,
        LoadToolBitmap(typeof(EllipseTool), "ellipse.png"))
    {
        ToolOptions =
        [
            ToolOption.StrokeColor, ToolOption.StrokeThickness, ToolOption.StrokeStyle,
            ToolOption.FillColor
        ];
        var plusBitmap = new Bitmap(AssetLoader.Open(new Uri("avares://Scribble/Assets/plus.png")));
        Cursor = new Cursor(plusBitmap, new PixelPoint(12, 12));
        HotKey = new KeyGesture(Key.D6);
        ToolTip = "Ellipse Tool - 6";
    }

    public override void HandlePointerClick(SKPoint startPoint)
    {
        _strokeId = Guid.NewGuid();
        _actionId = Guid.NewGuid();
        CanvasState.ApplyEvent(new StartStrokeEvent(_actionId, _strokeId, startPoint, StrokePaint.Clone(),
            ToolType.Ellipse, ToolOptions));
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