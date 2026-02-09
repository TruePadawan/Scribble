using System;
using Avalonia;
using Avalonia.Input;
using Scribble.Shared.Lib;
using Scribble.ViewModels;
using SkiaSharp;

namespace Scribble.Tools.PointerTools.EraseTool;

public class EraseTool : PointerToolsBase
{
    private Guid _strokeId = Guid.NewGuid();
    private Guid _actionId = Guid.NewGuid();

    public EraseTool(string name, MainViewModel viewModel)
        : base(name, viewModel, LoadToolBitmap(typeof(EraseTool), "eraser.png"))
    {
        Cursor = new Cursor(ToolIcon.CreateScaledBitmap(new PixelSize(36, 36)), new PixelPoint(10, 30));
    }

    public override void HandlePointerClick(Point coord)
    {
        var startPoint = new SKPoint((float)coord.X, (float)coord.Y);
        _strokeId = Guid.NewGuid();
        _actionId = Guid.NewGuid();
        ViewModel.ApplyEvent(new StartEraseStrokeEvent(_actionId, _strokeId, startPoint));
    }

    public override void HandlePointerMove(Point prevCoord, Point currentCoord)
    {
        var nextPoint = new SKPoint((float)currentCoord.X, (float)currentCoord.Y);
        ViewModel.ApplyEvent(new EraseStrokeLineToEvent(_actionId, _strokeId, nextPoint));
    }

    public override void HandlePointerRelease(Point prevCoord, Point currentCoord)
    {
        ViewModel.ApplyEvent(new TriggerEraseEvent(_actionId, _strokeId));
    }
}