using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Scribble.Services.CanvasStateService;
using Scribble.Shared.Lib;
using SkiaSharp;

namespace Scribble.Tools.PointerTools.ArrowTool;

public class ArrowTool : PolylineToolBase
{
    public ArrowTool(string name, ICanvasStateService canvasState) : base(name, canvasState,
        LoadToolBitmap(typeof(ArrowTool), "arrow.png"))
    {
        ToolOptions = [ToolOption.StrokeColor, ToolOption.StrokeThickness, ToolOption.StrokeStyle];
        var plusBitmap = new Bitmap(AssetLoader.Open(new Uri("avares://Scribble/Assets/plus.png")));
        Cursor = new Cursor(plusBitmap, new PixelPoint(12, 12));
        HotKey = new KeyGesture(Key.D5);
        ToolTip = "Arrow Tool - 5";
    }

    protected override ToolType GetToolType() => ToolType.Arrow;

    public static (SKPoint, SKPoint) GetArrowHeadPoints(SKPoint start, SKPoint end, float strokeWidth)
    {
        float arrowLength = strokeWidth * 10.0f;
        float arrowAngle = (float)(Math.PI / 6);

        // Calculate the angle of the main line
        float dy = end.Y - start.Y;
        float dx = end.X - start.X;
        float lineAngle = (float)Math.Atan2(dy, dx);

        // Calculate the angles for the left and right wings of the arrow head
        float leftWingAngle = lineAngle + (float)Math.PI - arrowAngle;
        float rightWingAngle = lineAngle + (float)Math.PI + arrowAngle;

        // Calculate the coordinates (Polar -> Cartesian)
        // x = r * cos(theta), y = r * sin(theta)
        SKPoint leftPoint = new SKPoint(
            end.X + arrowLength * (float)Math.Cos(leftWingAngle),
            end.Y + arrowLength * (float)Math.Sin(leftWingAngle)
        );

        SKPoint rightPoint = new SKPoint(
            end.X + arrowLength * (float)Math.Cos(rightWingAngle),
            end.Y + arrowLength * (float)Math.Sin(rightWingAngle)
        );

        return (leftPoint, rightPoint);
    }
}