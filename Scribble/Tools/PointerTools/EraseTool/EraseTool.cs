using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Scribble.Lib;
using Scribble.ViewModels;

namespace Scribble.Tools.PointerTools.EraseTool;

public class EraseTool : PointerToolsBase
{
    private int _radius = 5;
    private readonly List<Point> _currentErasePoints = [];

    public EraseTool(string name, MainViewModel viewModel)
        : base(name, viewModel, LoadToolBitmap(typeof(EraseTool), "eraser.png"))
    {
        Cursor = new Cursor(ToolIcon, new PixelPoint(10, 40));
    }

    public override void HandlePointerMove(Point prevCoord, Point currentCoord)
    {
        // Erase but don't save the event till the mouse/pointer is released
        using var frameBuffer = ViewModel.WhiteboardBitmap.Lock();
        var address = frameBuffer.Address;
        var stride = frameBuffer.RowBytes;

        ViewModel.EraseSegmentNoCaps(address, stride, prevCoord, currentCoord, _radius);
        ViewModel.EraseSinglePixel(address, stride, currentCoord, _radius);

        // Accumulate points for the stroke
        _currentErasePoints.Add(currentCoord);
    }

    public override void HandlePointerClick(Point coord)
    {
        _currentErasePoints.Clear();

        using var frameBuffer = ViewModel.WhiteboardBitmap.Lock();
        var address = frameBuffer.Address;
        var stride = frameBuffer.RowBytes;
        ViewModel.EraseSinglePixel(address, stride, coord, _radius);

        _currentErasePoints.Add(coord);
    }

    public override void HandlePointerRelease(Point prevCoord, Point currentCoord)
    {
        if (_currentErasePoints.Count == 0) return;

        if (_currentErasePoints.Count == 1)
        {
            ViewModel.EventsManager.Apply(new PointErased(_currentErasePoints[0], _radius), true);
        }
        else
        {
            var fullStrokeEvent = new PointsErased([.._currentErasePoints], _radius);
            ViewModel.EventsManager.Apply(fullStrokeEvent, skipRendering: true);
        }
    }

    public override bool RenderOptions(Panel parent)
    {
        // Render a slider for controlling the eraser thickness
        Slider slider = new Slider
        {
            TickFrequency = 5,
            IsSnapToTickEnabled = true,
            Minimum = 1,
            Maximum = 40,
            Value = _radius
        };
        slider.ValueChanged += ((sender, args) => { _radius = (int)args.NewValue; });
        slider.Padding = new Thickness(8, 0);

        parent.Children.Add(CreateOptionControl(slider, "Thickness"));
        return true;
    }
}