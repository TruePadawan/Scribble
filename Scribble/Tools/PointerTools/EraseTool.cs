using System;
using Avalonia;
using Avalonia.Media;
using Scribble.ViewModels;

namespace Scribble.Tools.PointerTools;

public class EraseTool(string name, MainViewModel viewModel, IImage icon) : PointerToolsBase(name, viewModel, icon)
{
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
        Erase(currentCoord);
    }

    public override void HandlePointerClick(Point coord)
    {
        Erase(coord);
    }
}