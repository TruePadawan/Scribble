using System;
using Avalonia;
using Avalonia.Input;
using Scribble.Shared.Lib;
using Scribble.ViewModels;
using SkiaSharp;

namespace Scribble.Tools.PointerTools.PencilTool;

public class PencilTool : StrokeTool
{
    private Guid _strokeId = Guid.NewGuid();
    private Guid _actionId = Guid.NewGuid();

    public PencilTool(string name, MainViewModel viewModel) : base(name, viewModel,
        LoadToolBitmap(typeof(PencilTool), "pencil.png"))
    {
        ToolOptions = [ToolOption.StrokeColor, ToolOption.StrokeThickness];
        Cursor = new Cursor(ToolIcon.CreateScaledBitmap(new PixelSize(36, 36)), new PixelPoint(0, 36));
        StrokePaint = new StrokePaint
        {
            IsAntialias = true,
            IsStroke = true,
            StrokeCap = SKStrokeCap.Round,
            StrokeWidth = 1,
            Color = SKColors.Red
        };
    }

    public override void HandlePointerClick(Point coord)
    {
        var startPoint = new SKPoint((float)coord.X, (float)coord.Y);
        _strokeId = Guid.NewGuid();
        _actionId = Guid.NewGuid();
        ViewModel.ApplyEvent(
            new StartStrokeEvent(_actionId, _strokeId, startPoint, StrokePaint.Clone(), ToolType.Pencil));
    }

    public override void HandlePointerMove(Point prevCoord, Point currentCoord)
    {
        var nextPoint = new SKPoint((float)currentCoord.X, (float)currentCoord.Y);
        ViewModel.ApplyEvent(new PencilStrokeLineToEvent(_actionId, _strokeId, nextPoint));
    }

    public override void HandlePointerRelease(Point prevCoord, Point currentCoord)
    {
        ViewModel.ApplyEvent(new EndStrokeEvent(_actionId));
    }
}