using System;
using Avalonia;
using Avalonia.Input;
using Scribble.Lib;
using Scribble.ViewModels;
using SkiaSharp;

namespace Scribble.Tools.PointerTools.EraseTool;

public class EraseTool : PointerToolsBase
{
    private Guid _currentStrokeId = Guid.NewGuid();

    public EraseTool(string name, MainViewModel viewModel)
        : base(name, viewModel, LoadToolBitmap(typeof(EraseTool), "eraser.png"))
    {
        Cursor = new Cursor(ToolIcon, new PixelPoint(10, 40));
    }

    public override void HandlePointerMove(Point prevCoord, Point currentCoord)
    {
        var nextPoint = new SKPoint((float)currentCoord.X, (float)currentCoord.Y);
        ViewModel.ApplyStrokeEvent(new EraseStrokeLineToEvent(_currentStrokeId, nextPoint));
    }

    public override void HandlePointerClick(Point coord)
    {
        var startPoint = new SKPoint((float)coord.X, (float)coord.Y);
        _currentStrokeId = Guid.NewGuid();
        ViewModel.ApplyStrokeEvent(new StartEraseStrokeEvent(_currentStrokeId, startPoint));
    }

    public override void HandlePointerRelease(Point prevCoord, Point currentCoord)
    {
        ViewModel.ApplyStrokeEvent(new TriggerEraseEvent(_currentStrokeId));
    }
}