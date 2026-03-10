using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Scribble.Services.CanvasState;
using Scribble.Shared.Lib;
using SkiaSharp;

namespace Scribble.Tools.PointerTools.RectangleTool;

public class RectangleTool : StrokeTool
{
    private SKPoint? _startPoint;
    private Guid _strokeId = Guid.NewGuid();
    private Guid _actionId = Guid.NewGuid();

    public RectangleTool(string name, CanvasStateService canvasState) : base(name, canvasState,
        LoadToolBitmap(typeof(RectangleTool), "rectangle.png"))
    {
        ToolOptions =
        [
            ToolOption.StrokeColor, ToolOption.StrokeThickness, ToolOption.StrokeStyle, ToolOption.FillColor,
            ToolOption.EdgeType
        ];
        var plusBitmap = new Bitmap(AssetLoader.Open(new Uri("avares://Scribble/Assets/plus.png")));
        Cursor = new Cursor(plusBitmap, new PixelPoint(12, 12));
        HotKey = new KeyGesture(Key.D7);
        ToolTip = "Rectangle Tool - 7";
    }

    public override void HandlePointerClick(Point coord)
    {
        _startPoint = new SKPoint((float)coord.X, (float)coord.Y);
        _strokeId = Guid.NewGuid();
        _actionId = Guid.NewGuid();
        CanvasState.ApplyEvent(new StartStrokeEvent(_actionId, _strokeId, _startPoint.Value, StrokePaint.Clone(),
            ToolType.Rectangle, ToolOptions));
    }

    public override void HandlePointerMove(Point prevCoord, Point currentCoord)
    {
        var endPoint = new SKPoint((float)currentCoord.X, (float)currentCoord.Y);
        CanvasState.ApplyEvent(new LineStrokeLineToEvent(_actionId, _strokeId, endPoint));
    }

    public override void HandlePointerRelease(Point prevCoord, Point currentCoord)
    {
        CanvasState.ApplyEvent(new EndStrokeEvent(_actionId));
    }
}