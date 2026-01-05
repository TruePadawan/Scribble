using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Scribble.ViewModels;

namespace Scribble.Tools.PointerTools.EraseTool;

public class EraseTool : PointerToolsBase
{
    private int _strokeWidth = 5;

    public EraseTool(string name, MainViewModel viewModel)
        : base(name, viewModel, LoadToolBitmap(typeof(EraseTool), "eraser.png"))
    {
        Cursor = new Cursor(ToolIcon, new PixelPoint(10, 40));
    }

    public override void HandlePointerMove(Point prevCoord, Point currentCoord)
    {
        using var frame = ViewModel.WhiteboardBitmap.Lock();
        IntPtr address = frame.Address;
        int stride = frame.RowBytes;

        ViewModel.EraseSegmentNoCaps(address, stride, prevCoord, currentCoord, _strokeWidth);
        ViewModel.EraseSinglePixel(address, stride, currentCoord, _strokeWidth);
    }

    public override void HandlePointerClick(Point coord)
    {
        ViewModel.StartStateCapture();
        using var frame = ViewModel.WhiteboardBitmap.Lock();
        IntPtr address = frame.Address;
        int stride = frame.RowBytes;
        ViewModel.EraseSinglePixel(address, stride, coord, _strokeWidth);
    }

    public override void HandlePointerRelease(Point prevCoord, Point currentCoord)
    {
        ViewModel.StopStateCapture();
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
            Value = _strokeWidth
        };
        slider.ValueChanged += ((sender, args) => { _strokeWidth = (int)args.NewValue; });
        slider.Padding = new Thickness(8, 0);

        parent.Children.Add(CreateOptionControl(slider, "Thickness"));
        return true;
    }
}