using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Scribble.Services.CanvasStateService;
using Scribble.Shared.Lib;

namespace Scribble.Tools.PointerTools.ArrowTool;

public class ArrowTool : PolylineToolBase
{
    public ArrowTool(string name, ICanvasStateService canvasState) : base(name, canvasState,
        LoadToolBitmap(typeof(ArrowTool), "arrow.png"))
    {
        ToolOptions = [ToolOption.StrokeColor, ToolOption.StrokeThickness, ToolOption.StrokeStyle, ToolOption.EdgeType];
        var plusBitmap = new Bitmap(AssetLoader.Open(new Uri("avares://Scribble/Assets/plus.png")));
        Cursor = new Cursor(plusBitmap, new PixelPoint(12, 12));
        HotKey = new KeyGesture(Key.A);
        ToolTip = "Arrow Tool (A)";
    }

    protected override ToolType GetToolType() => ToolType.Arrow;
}
