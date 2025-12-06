using System;
using Avalonia;
using Avalonia.Media;
using Scribble.ViewModels;

namespace Scribble.Tools.PointerTools;

public class DrawTool(string name, MainViewModel viewModel, IImage icon) : PointerToolsBase(name, viewModel, icon)
{
    public override void HandlePointerMove(Point prevCoord, Point currentCoord)
    {
        DrawLine(prevCoord, currentCoord, Colors.Red, 2);
    }

    public override void HandlePointerClick(Point coord)
    {
        DrawSinglePixel(coord, Colors.Red, 2);
    }

    private void DrawSinglePixel(Point coord, Color color, int strokeWidth = 1)
    {
        using var frame = ViewModel.WhiteboardBitmap.Lock();
        IntPtr address = frame.Address;
        int stride = frame.RowBytes;

        for (int i = 0; i < strokeWidth; i++)
        {
            SetPixel(address, stride, coord.WithY(coord.Y + i), color, 1f);
            for (int j = 0; j < strokeWidth; j++)
            {
                SetPixel(address, stride, new Point(coord.X + j, coord.Y + i), color, 1f);
            }
        }
    }

    // Draw lines using Xiaolin Wu's Line Algorithm
    // Modified to allow drawing lines of a particular thickness; gotten from https://github.com/jambolo/thick-xiaolin-wu/blob/master/cs/thick-xiaolin-wu.coffee
    private void DrawLine(Point start, Point end, Color color, int strokeWidth = 1)
    {
        strokeWidth = Math.Max(1, strokeWidth);

        using var frame = ViewModel.WhiteboardBitmap.Lock();
        IntPtr address = frame.Address;
        int stride = frame.RowBytes;

        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        bool steep = Math.Abs(dy) > Math.Abs(dx);

        if (steep)
        {
            // Swap x and y coordinates for steep lines
            (start, end) = (new Point(start.Y, start.X), new Point(end.Y, end.X));
            (dx, dy) = (dy, dx);
        }

        if (start.X > end.X)
        {
            // Ensure we draw from left to right
            (start, end) = (end, start);
            dx = -dx;
            dy = -dy;
        }

        double gradient = dx == 0 ? 1 : dy / dx;

        strokeWidth = (int)(strokeWidth * Math.Sqrt(1 + (gradient * gradient)));

        // Handle the first endpoint
        double xend = Math.Round(start.X);
        double yend = start.Y - (strokeWidth - 1) * 0.5 + gradient * (xend - start.X);
        double xgap = 1 - (start.X + 0.5 - Math.Floor(start.X + 0.5));
        int xpxl1 = (int)xend; // This will be used in the main loop
        int ypxl1 = (int)Math.Floor(yend);

        if (steep)
        {
            SetPixel(address, stride, new Point(ypxl1, xpxl1), color, (1 - (yend - Math.Floor(yend))) * xgap);
            for (int i = 1; i < strokeWidth; i++)
            {
                SetPixel(address, stride, new Point(ypxl1 + i, xpxl1), color, 1);
            }

            SetPixel(address, stride, new Point(ypxl1 + strokeWidth, xpxl1), color, (yend - Math.Floor(yend)) * xgap);
        }
        else
        {
            SetPixel(address, stride, new Point(xpxl1, ypxl1), color, (1 - (yend - Math.Floor(yend))) * xgap);
            for (int i = 1; i < strokeWidth; i++)
            {
                SetPixel(address, stride, new Point(xpxl1, ypxl1 + i), color, 1);
            }

            SetPixel(address, stride, new Point(xpxl1, ypxl1 + strokeWidth), color, (yend - Math.Floor(yend)) * xgap);
        }

        double intery = yend + gradient; // First y-intersection for the main loop

        // Handle the second endpoint
        xend = Math.Round(end.X);
        yend = end.Y - (strokeWidth - 1) * 0.5 + gradient * (xend - end.X);
        xgap = end.X + 0.5 - Math.Floor(end.X + 0.5);
        int xpxl2 = (int)xend; // This will be used in the main loop
        int ypxl2 = (int)Math.Floor(yend);

        if (steep)
        {
            SetPixel(address, stride, new Point(ypxl2, xpxl2), color, (1 - (yend - Math.Floor(yend))) * xgap);
            for (int i = 1; i < strokeWidth; i++)
            {
                SetPixel(address, stride, new Point(ypxl2 + i, xpxl2), color, 1);
            }

            SetPixel(address, stride, new Point(ypxl2 + strokeWidth, xpxl2), color, (yend - Math.Floor(yend)) * xgap);
        }
        else
        {
            SetPixel(address, stride, new Point(xpxl2, ypxl2), color, (1 - (yend - Math.Floor(yend))) * xgap);
            for (int i = 1; i < strokeWidth; i++)
            {
                SetPixel(address, stride, new Point(xpxl2, ypxl2 + i), color, 1);
            }

            SetPixel(address, stride, new Point(xpxl2, ypxl2 + strokeWidth), color, (yend - Math.Floor(yend)) * xgap);
        }

        // Main loop
        for (int x = xpxl1 + 1; x < xpxl2; x++)
        {
            if (steep)
            {
                SetPixel(address, stride, new Point(Math.Floor(intery), x), color,
                    1 - (intery - Math.Floor(intery)));
                for (int i = 1; i < strokeWidth; i++)
                {
                    SetPixel(address, stride, new Point(Math.Floor(intery) + i, x), color, 1);
                }

                SetPixel(address, stride, new Point(Math.Floor(intery) + strokeWidth, x), color,
                    intery - Math.Floor(intery));
            }
            else
            {
                SetPixel(address, stride, new Point(x, Math.Floor(intery)), color,
                    1 - (intery - Math.Floor(intery)));
                for (int i = 1; i < strokeWidth; i++)
                {
                    SetPixel(address, stride, new Point(x, Math.Floor(intery) + i), color, 1);
                }

                SetPixel(address, stride, new Point(x, Math.Floor(intery) + strokeWidth), color,
                    intery - Math.Floor(intery));
            }

            intery += gradient;
        }
    }
}