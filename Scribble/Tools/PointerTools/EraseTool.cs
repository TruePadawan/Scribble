using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Scribble.ViewModels;

namespace Scribble.Tools.PointerTools;

public class EraseTool(string name, MainViewModel viewModel, IImage icon) : PointerToolsBase(name, viewModel, icon)
{
    private int _strokeWidth = 5;

    public override void HandlePointerMove(Point prevCoord, Point currentCoord)
    {
        EraseSegmentNoCaps(prevCoord, currentCoord, _strokeWidth);
        EraseSinglePixel(currentCoord, _strokeWidth);
    }

    public override void HandlePointerClick(Point coord)
    {
        EraseSinglePixel(coord, _strokeWidth);
    }

    public override void RenderOptions(Panel parent)
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
    }

    // Solid (non-AA) circular dab for performance
    private void EraseSinglePixel(Point coord, int strokeWidth)
    {
        strokeWidth = Math.Max(1, strokeWidth);
        double halfWidth = strokeWidth / 2.0;
        double r2 = halfWidth * halfWidth;

        using var frame = ViewModel.WhiteboardBitmap.Lock();
        IntPtr address = frame.Address;
        int stride = frame.RowBytes;

        double cx = coord.X;
        double cy = coord.Y;
        int minX = (int)Math.Floor(cx - halfWidth);
        int maxX = (int)Math.Ceiling(cx + halfWidth);
        int minY = (int)Math.Floor(cy - halfWidth);
        int maxY = (int)Math.Ceiling(cy + halfWidth);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                double px = x + 0.5;
                double py = y + 0.5;
                double ddx = px - cx;
                double ddy = py - cy;
                double d2 = ddx * ddx + ddy * ddy;
                if (d2 > r2) continue;
                ViewModel.SetPixel(address, stride, new Point(x, y), ViewModel.BackgroundColor, 1f);
            }
        }
    }

    private void EraseSegmentNoCaps(Point start, Point end, int strokeWidth)
    {
        strokeWidth = Math.Max(1, strokeWidth);

        // Compute geometry
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double len2 = dx * dx + dy * dy;
        if (len2 < 1e-12) return;

        double len = Math.Sqrt(len2);
        double ux = dx / len;
        double uy = dy / len;
        double pxn = -uy;
        double pyn = ux;

        double halfWidth = strokeWidth / 2.0;
        double mx = (start.X + end.X) * 0.5;
        double my = (start.Y + end.Y) * 0.5;
        double halfLen = len * 0.5;

        using var frame = ViewModel.WhiteboardBitmap.Lock();
        IntPtr address = frame.Address;
        int stride = frame.RowBytes;

        // Bounding box expanded by halfWidth + 1 for AA fringe
        int minXi = (int)Math.Floor(Math.Min(start.X, end.X) - halfWidth - 1);
        int maxXi = (int)Math.Ceiling(Math.Max(start.X, end.X) + halfWidth + 1);
        int minYi = (int)Math.Floor(Math.Min(start.Y, end.Y) - halfWidth - 1);
        int maxYi = (int)Math.Ceiling(Math.Max(start.Y, end.Y) + halfWidth + 1);

        for (int y = minYi; y <= maxYi; y++)
        {
            for (int x = minXi; x <= maxXi; x++)
            {
                double cx = x + 0.5 - mx;
                double cy = y + 0.5 - my;
                double u = cx * ux + cy * uy;
                double v = cx * pxn + cy * pyn;

                // Solid (non-AA) rectangle coverage in the line's local space
                if (Math.Abs(u) <= halfLen && Math.Abs(v) <= halfWidth)
                {
                    ViewModel.SetPixel(address, stride, new Point(x, y), ViewModel.BackgroundColor, 1f);
                }
            }
        }
    }
}