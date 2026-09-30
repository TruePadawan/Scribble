using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Scribble.Services.CanvasStateService;
using Scribble.Shared.Lib;

namespace Scribble.Tools.PointerTools.LineTool;

public class LineTool : PolylineToolBase
{
    public LineTool(string name, ICanvasStateService canvasState) : base(name, canvasState,
        LoadToolBitmap(typeof(LineTool), "line.png"))
    {
        ToolOptions = [ToolOption.StrokeColor, ToolOption.StrokeThickness, ToolOption.StrokeStyle, ToolOption.EdgeType];
        var plusBitmap = new Bitmap(AssetLoader.Open(new Uri("avares://Scribble/Assets/plus.png")));
        Cursor = new Cursor(plusBitmap, new PixelPoint(12, 12));
        HotKey = new KeyGesture(Key.D4);
        ToolTip = "Line Tool - 4";
    }

    protected override ToolType GetToolType() => ToolType.Line;
}