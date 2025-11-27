using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Scribble.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly Vector _dpi = new(96, 96);
    private const int BytesPerPixel = 4;
    private const int CanvasWidth = 10000;
    private const int CanvasHeight = 10000;

    public WriteableBitmap WhiteboardBitmap { get; }
    public ScaleTransform ScaleTransform { get; }


    public MainViewModel()
    {
        ScaleTransform = new ScaleTransform(1, 1);

        // Initialize the bitmap with a large dimension
        WhiteboardBitmap = new WriteableBitmap(new PixelSize(CanvasWidth, CanvasHeight), _dpi, PixelFormat.Bgra8888);
        ClearBitmap(Colors.White);
    }

    public Vector GetCanvasDimensions() => new Vector(CanvasWidth, CanvasHeight);

    public double GetCurrentScale() => ScaleTransform.ScaleX;

    public void SetCurrentScale(double newScale)
    {
        ScaleTransform.ScaleX = newScale;
        ScaleTransform.ScaleY = newScale;
    }

    private unsafe void ClearBitmap(Color backgroundColor)
    {
        using var frame = WhiteboardBitmap.Lock();
        var address = frame.Address;
        int stride = frame.RowBytes;
        byte* bitmapPtr = (byte*)address.ToPointer();

        int width = WhiteboardBitmap.PixelSize.Width;
        int height = WhiteboardBitmap.PixelSize.Height;
        for (int y = 0; y < height; ++y)
        {
            for (int x = 0; x < width; ++x)
            {
                long offset = (long)y * stride + (long)x * BytesPerPixel;
                bitmapPtr[offset] = backgroundColor.B;
                bitmapPtr[offset + 1] = backgroundColor.G;
                bitmapPtr[offset + 2] = backgroundColor.R;
                bitmapPtr[offset + 3] = backgroundColor.A;
            }
        }
    }

    private unsafe void SetPixel(IntPtr address, int stride, Point coord, Color color, double opacity)
    {
        int width = WhiteboardBitmap.PixelSize.Width;
        int height = WhiteboardBitmap.PixelSize.Height;
        (double x, double y) = coord;

        if (x >= 0 && x < width && y >= 0 && y < height)
        {
            long offset = (long)y * stride + (long)x * BytesPerPixel;
            byte* p = (byte*)address.ToPointer();

            // Get existing pixel values for alpha blending
            byte existingB = p[offset + 0];
            byte existingG = p[offset + 1];
            byte existingR = p[offset + 2];
            byte existingA = p[offset + 3];

            // Calculate blended values using alpha compositing
            double alpha = opacity;
            byte newB = (byte)(color.B * alpha + existingB * (1 - alpha));
            byte newG = (byte)(color.G * alpha + existingG * (1 - alpha));
            byte newR = (byte)(color.R * alpha + existingR * (1 - alpha));
            byte newA = (byte)Math.Min(255, existingA + color.A * alpha);

            p[offset + 0] = newB;
            p[offset + 1] = newG;
            p[offset + 2] = newR;
            p[offset + 3] = newA;
        }
    }

    // Draw lines using Xiaolin Wu's Line Algorithm
    // Modified to allow drawing lines of a particular thickness; gotten from https://github.com/jambolo/thick-xiaolin-wu/blob/master/cs/thick-xiaolin-wu.coffee
    public void DrawLine(Point start, Point end, Color color, int strokeWidth = 1)
    {
        strokeWidth = Math.Max(1, strokeWidth);

        using var frame = WhiteboardBitmap.Lock();
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

    public void DrawSinglePixel(Point coord, Color color)
    {
        using var frame = WhiteboardBitmap.Lock();
        IntPtr address = frame.Address;
        int stride = frame.RowBytes;

        SetPixel(address, stride, coord, color, 1f);
    }
}