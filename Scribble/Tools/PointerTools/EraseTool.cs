using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Scribble.ViewModels;

namespace Scribble.Tools.PointerTools;

public class EraseTool(string name, MainViewModel viewModel, IImage icon) : PointerToolsBase(name, viewModel, icon)
{
    private int _radius = 8;

    private void Erase(Point coord, int radius = 8)
    {
        using var frame = ViewModel.WhiteboardBitmap.Lock();
        IntPtr address = frame.Address;
        int stride = frame.RowBytes;

        for (int i = (int)coord.Y - radius; i < (int)coord.Y + radius; i++)
        {
            for (int j = (int)coord.X - radius; j < (int)coord.X + radius; j++)
            {
                ViewModel.SetPixel(address, stride, new Point(j, i), ViewModel.BackgroundColor, 1f);
            }
        }
    }

    public override void HandlePointerMove(Point prevCoord, Point currentCoord)
    {
        Erase(currentCoord, _radius);
    }

    public override void HandlePointerClick(Point coord)
    {
        Erase(coord, _radius);
    }

    public override void RenderOptions(Panel parent)
    {
        // Render a slider for controlling the eraser radius
        Slider slider = new Slider
        {
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Minimum = 1,
            Maximum = 20,
            Value = _radius
        };
        slider.ValueChanged += ((sender, args) => { _radius = (int)args.NewValue; });
        slider.Padding = new Thickness(8, 0);

        parent.Children.Add(CreateOptionControl(slider, "Radius"));
    }
}