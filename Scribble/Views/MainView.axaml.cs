using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Runtime.InteropServices;

namespace Scribble.Views;

public partial class MainView : UserControl
{
    private Point _prevCoord;
    private WriteableBitmap _whiteboardBitmap;
    private const int BytesPerPixel = 4;

    public MainView()
    {
        InitializeComponent();
        _prevCoord = new Point(-1, -1);

        var pixelSize = new PixelSize(2, 2);
        var dpi = new Vector(96, 96);

        _whiteboardBitmap = new WriteableBitmap(pixelSize, dpi, PixelFormat.Bgra8888);
        Whiteboard.Source = _whiteboardBitmap;
    }

    private void MainCanvas_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var pointerCoordinates = e.GetPosition(sender as Control);
        var hasLastCoordinates = !_prevCoord.Equals(new Point(-1, -1));

        if (e.Properties.IsLeftButtonPressed && hasLastCoordinates)
        {
            DrawLine(_prevCoord, pointerCoordinates, Colors.Red);
        }

        _prevCoord = e.GetPosition(MainCanvas);
    }

    private void MainCanvas_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Reset the last coordinates when the mouse is released
        _prevCoord = new Point(-1, -1);
    }

    // private void PutPixel(Point coord, double opacity)
    // {
    //     var pixel = new Rectangle
    //     {
    //         Width = 2,
    //         Height = 2,
    //         Fill = Brushes.Red,
    //         Opacity = opacity
    //     };
    //     Canvas.SetLeft(pixel, coord.X);
    //     Canvas.SetTop(pixel, coord.Y);
    //     MainCanvas.Children.Add(pixel);
    // }

    private unsafe void SetPixel(IntPtr address, int stride, Point coord, Color color, double opacity)
    {
        int width = _whiteboardBitmap.PixelSize.Width;
        int height = _whiteboardBitmap.PixelSize.Height;
        (double x, double y) = coord;
        
        if (x >= 0 && x < width && y >= 0 && y < height)
        {
            long offset = (long)y * stride + (long)x * BytesPerPixel;

            byte* p = (byte*)address.ToPointer();
            // Read destination (existing) pixel in BGRA order
            byte dB = p[offset + 0];
            byte dG = p[offset + 1];
            byte dR = p[offset + 2];
            byte dA = p[offset + 3];

            // Source with effective opacity
            double aS = (color.A / 255.0) * Math.Clamp(opacity, 0.0, 1.0);
            double aD = dA / 255.0;
            double outA = aS + aD * (1 - aS);

            byte oA, oR, oG, oB;
            if (outA <= 0)
            {
                oA = oR = oG = oB = 0;
            }
            else
            {
                // Source-over blending with straight alpha
                double sR = color.R / 255.0;
                double sG = color.G / 255.0;
                double sB = color.B / 255.0;
                double dRlin = dR / 255.0;
                double dGlin = dG / 255.0;
                double dBlin = dB / 255.0;

                double outR = (sR * aS + dRlin * aD * (1 - aS)) / outA;
                double outG = (sG * aS + dGlin * aD * (1 - aS)) / outA;
                double outB = (sB * aS + dBlin * aD * (1 - aS)) / outA;

                oA = (byte)Math.Round(outA * 255.0);
                oR = (byte)Math.Round(Math.Clamp(outR, 0.0, 1.0) * 255.0);
                oG = (byte)Math.Round(Math.Clamp(outG, 0.0, 1.0) * 255.0);
                oB = (byte)Math.Round(Math.Clamp(outB, 0.0, 1.0) * 255.0);
            }

            p[offset + 0] = oB;
            p[offset + 1] = oG;
            p[offset + 2] = oR;
            p[offset + 3] = oA;
        }
    }
    
    // Draw lines using Xiaolin Wu's Line Algorithm
    private void DrawLine(Point start, Point end, Color color)
    {
        // Check if the line segment is longer on the x or y-axis to know if we have a horizontal or vertical line
        using (var frame = _whiteboardBitmap.Lock())
        {
            IntPtr address = frame.Address;
            int stride = frame.RowBytes;

            if (Math.Abs(end.Y - start.Y) < Math.Abs(end.X - start.X))
            {
                // HORIZONTAL LINE
                // Handle lines drawn to the left by swapping the start and end coordinates
                if (end.X < start.X)
                {
                    var temp = new Point(end.X, end.Y);
                    end = start;
                    start = temp;
                }

                var deltaX = end.X - start.X;
                var deltaY = end.Y - start.Y;
                var slope = deltaX == 0 ? 1 : deltaY / deltaX;

                // Handle opacity for the first pixel
                var xOverlapDistance = 1 - ((start.X + 0.5) - (int)(start.X + 0.5));
                var firstPixelAlpha = start.Y - (int)start.Y;
                SetPixel(address, stride, new Point((int)(start.X + 0.5), (int)start.Y), color, (1 - firstPixelAlpha) * xOverlapDistance);
                SetPixel(address, stride, new Point((int)(start.X + 0.5), (int)start.Y + 1), color, firstPixelAlpha * xOverlapDistance);

                // Handle opacity for the last pixel
                xOverlapDistance = ((end.X - 0.5) - (int)(end.X - 0.5));
                firstPixelAlpha = end.Y - (int)end.Y;
                SetPixel(address, stride, new Point((int)(end.X + 0.5), (int)end.Y), color, (1 - firstPixelAlpha) * xOverlapDistance);
                SetPixel(address, stride, new Point((int)(end.X + 0.5), (int)end.Y + 1), color, firstPixelAlpha * xOverlapDistance);

                for (var i = 0; i < (int)deltaX + 1; i++)
                {
                    var x = start.X + i;
                    var y = start.Y + (i * slope);
                    var pixelIntegerCoord = new Point((int)x, (int)y);

                    // Calculate the alpha values used for opacity
                    var alpha = y - (int)y;

                    SetPixel(address, stride, pixelIntegerCoord, color, 1 - alpha);
                    SetPixel(address, stride, pixelIntegerCoord.WithY((int)y + 1), color, alpha);
                }
            }
            else
            {
                // VERTICAL LINE
                // Handle lines drawn to the left
                if (end.Y < start.Y)
                {
                    var temp = new Point(end.X, end.Y);
                    end = start;
                    start = temp;
                }

                var deltaX = end.X - start.X;
                var deltaY = end.Y - start.Y;
                var slope = deltaY == 0 ? 1 : deltaX / deltaY;

                // Handle opacity for the first pixel
                var yOverlapDistance = 1 - ((start.Y + 0.5) - (int)(start.Y + 0.5));
                var firstPixelAlpha = start.Y - (int)start.Y;
                SetPixel(address, stride, new Point((int)(start.X + 0.5), (int)start.Y), color, (1 - firstPixelAlpha) * yOverlapDistance);
                SetPixel(address, stride, new Point((int)(start.X + 0.5), (int)start.Y + 1), color, firstPixelAlpha * yOverlapDistance);

                // Handle opacity for the last pixel
                yOverlapDistance = ((end.Y - 0.5) - (int)(end.Y - 0.5));
                firstPixelAlpha = end.Y - (int)end.Y;
                SetPixel(address, stride, new Point((int)end.X, (int)(end.Y + 0.5)), color, (1 - firstPixelAlpha) * yOverlapDistance);
                SetPixel(address, stride, new Point((int)end.X + 1, (int)(end.Y + 0.5)), color, firstPixelAlpha * yOverlapDistance);

                for (var i = 0; i < (int)deltaY + 1; i++)
                {
                    var x = start.X + (i * slope);
                    var y = start.Y + i;
                    var pixelIntegerCoord = new Point((int)x, (int)y);

                    // Calculate the alpha values used for opacity
                    var alpha = x - (int)x;

                    SetPixel(address, stride, pixelIntegerCoord, color, 1 - alpha);
                    SetPixel(address, stride, pixelIntegerCoord.WithX((int)x + 1), color, alpha);
                }
            }
        }
    }
}