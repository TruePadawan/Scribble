using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Scribble.ViewModels;

namespace Scribble.Tools.PointerTools.DrawTool;

public class DrawTool : PointerToolsBase
{
    private Color _strokeColor = Colors.Red;
    private int _strokeWidth = 1;

    public DrawTool(string name, MainViewModel viewModel) : base(name, viewModel,
        LoadToolBitmap(typeof(DrawTool), "draw.png"))
    {
        Cursor = new Cursor(ToolIcon, new PixelPoint(0, 50));
    }

    public override void HandlePointerMove(Point prevCoord, Point currentCoord)
    {
        float opacity = _strokeColor.A / 255f;
        using var frame = ViewModel.WhiteboardBitmap.Lock();
        IntPtr address = frame.Address;
        int stride = frame.RowBytes;

        // Draw an interior segment without round caps to avoid over-dark joints,
        // then place a single circular dab at the current point to form a smooth join.
        ViewModel.DrawSegmentNoCaps(address, stride, prevCoord, currentCoord, _strokeColor, _strokeWidth, opacity);
        ViewModel.DrawSinglePixel(address, stride, currentCoord, _strokeColor, _strokeWidth, opacity);
    }

    public override void HandlePointerClick(Point coord)
    {
        ViewModel.StartStateCapture();
        float opacity = _strokeColor.A / 255f;
        using var frame = ViewModel.WhiteboardBitmap.Lock();
        IntPtr address = frame.Address;
        int stride = frame.RowBytes;
        ViewModel.DrawSinglePixel(address, stride, coord, _strokeColor, _strokeWidth, opacity);
    }

    public override void HandlePointerRelease(Point prevCoord, Point currentCoord)
    {
        double dx = currentCoord.X - prevCoord.X;
        double dy = currentCoord.Y - prevCoord.Y;
        double dist2 = dx * dx + dy * dy;
        if (dist2 > 1e-4)
        {
            HandlePointerClick(currentCoord);
        }

        ViewModel.StopStateCapture();
    }

    public override bool RenderOptions(Panel parent)
    {
        // Render a slider for controlling the stroke width and a color picker for stroke color
        Slider slider = new Slider
        {
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Minimum = 1,
            Maximum = 10,
            Value = _strokeWidth
        };
        slider.ValueChanged += ((sender, args) => { _strokeWidth = (int)args.NewValue; });
        slider.Padding = new Thickness(8, 0);

        ColorPicker colorPicker = new ColorPicker
        {
            Color = _strokeColor
        };
        colorPicker.ColorChanged += (sender, args) => { _strokeColor = args.NewColor; };

        parent.Children.Add(CreateOptionControl(colorPicker, "Color"));
        parent.Children.Add(CreateOptionControl(slider, "Thickness"));
        parent.Width = 180;
        return true;
    }
}