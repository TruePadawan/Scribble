using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Scribble.Views;

public partial class MainView : UserControl
{
    private Point _prevCoord;
    private WriteableBitmap _whiteboardBitmap;
    private readonly Vector _dpi = new(96, 96);
    private const int BytesPerPixel = 4;
    private const int CanvasWidth = 5000;
    private const int CanvasHeight = 5000;
    private readonly ScaleTransform _scaleTransform;
    private double zoomFactor = 1f;

    public MainView()
    {
        InitializeComponent();
        _prevCoord = new Point(-1, -1);

        _scaleTransform = new ScaleTransform(1, 1);
        MainCanvas.RenderTransform = _scaleTransform;

        // Initialize the bitmap with a large dimension
        _whiteboardBitmap = new WriteableBitmap(new PixelSize(CanvasWidth, CanvasHeight), _dpi, PixelFormat.Bgra8888);
        ClearBitmap(Colors.White);
        WhiteboardRenderer.Source = _whiteboardBitmap;

        // Move the whiteboard from the top-left edge
        CanvasScrollViewer.Offset = new Vector(2500d, 2500d);
    }

    private unsafe void ClearBitmap(Color backgroundColor)
    {
        using var frame = _whiteboardBitmap.Lock();
        var address = frame.Address;
        int stride = frame.RowBytes;
        byte* bitmapPtr = (byte*)address.ToPointer();

        int width = _whiteboardBitmap.PixelSize.Width;
        int height = _whiteboardBitmap.PixelSize.Height;
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

    private void MainCanvas_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var pointerCoordinates = e.GetPosition(sender as Control);
        var hasLastCoordinates = !_prevCoord.Equals(new Point(-1, -1));

        if (e.Properties.IsLeftButtonPressed && hasLastCoordinates)
        {
            DrawLine(_prevCoord, pointerCoordinates, Colors.Red);
        }

        _prevCoord = e.GetPosition(sender as Control);
    }

    private void MainCanvas_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Reset the last coordinates when the mouse is released
        _prevCoord = new Point(-1, -1);
    }

    private void MainCanvas_OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        bool ctrlKeyIsActive = (e.KeyModifiers & KeyModifiers.Control) != 0;
        if (!ctrlKeyIsActive) return;

        if (e.Delta.Y > 0)
        {
            ZoomIn();
        }
        else
        {
            ZoomOut();
        }
    }

    private void ZoomIn()
    {
        if (zoomFactor >= 3) return;
        zoomFactor += 0.5f;
        _scaleTransform.ScaleX = zoomFactor;
        _scaleTransform.ScaleY = zoomFactor;
    }

    private void ZoomOut()
    {
        if (zoomFactor <= 1) return;
        zoomFactor -= 0.5f;
        _scaleTransform.ScaleX = zoomFactor;
        _scaleTransform.ScaleY = zoomFactor;
    }

    private unsafe void SetPixel(IntPtr address, int stride, Point coord, Color color, double opacity)
    {
        int width = _whiteboardBitmap.PixelSize.Width;
        int height = _whiteboardBitmap.PixelSize.Height;
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
    private void DrawLine(Point start, Point end, Color color)
    {
        using var frame = _whiteboardBitmap.Lock();
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

        // Handle the first endpoint
        double xend = Math.Round(start.X);
        double yend = start.Y + gradient * (xend - start.X);
        double xgap = 1 - (start.X + 0.5 - Math.Floor(start.X + 0.5));
        int xpxl1 = (int)xend; // This will be used in the main loop
        int ypxl1 = (int)Math.Floor(yend);

        if (steep)
        {
            SetPixel(address, stride, new Point(ypxl1, xpxl1), color, (1 - (yend - Math.Floor(yend))) * xgap);
            SetPixel(address, stride, new Point(ypxl1 + 1, xpxl1), color, (yend - Math.Floor(yend)) * xgap);
        }
        else
        {
            SetPixel(address, stride, new Point(xpxl1, ypxl1), color, (1 - (yend - Math.Floor(yend))) * xgap);
            SetPixel(address, stride, new Point(xpxl1, ypxl1 + 1), color, (yend - Math.Floor(yend)) * xgap);
        }

        double intery = yend + gradient; // First y-intersection for the main loop

        // Handle the second endpoint
        xend = Math.Round(end.X);
        yend = end.Y + gradient * (xend - end.X);
        xgap = end.X + 0.5 - Math.Floor(end.X + 0.5);
        int xpxl2 = (int)xend; // This will be used in the main loop
        int ypxl2 = (int)Math.Floor(yend);

        if (steep)
        {
            SetPixel(address, stride, new Point(ypxl2, xpxl2), color, (1 - (yend - Math.Floor(yend))) * xgap);
            SetPixel(address, stride, new Point(ypxl2 + 1, xpxl2), color, (yend - Math.Floor(yend)) * xgap);
        }
        else
        {
            SetPixel(address, stride, new Point(xpxl2, ypxl2), color, (1 - (yend - Math.Floor(yend))) * xgap);
            SetPixel(address, stride, new Point(xpxl2, ypxl2 + 1), color, (yend - Math.Floor(yend)) * xgap);
        }

        // Main loop
        for (int x = xpxl1 + 1; x < xpxl2; x++)
        {
            if (steep)
            {
                SetPixel(address, stride, new Point((int)Math.Floor(intery), x), color,
                    1 - (intery - Math.Floor(intery)));
                SetPixel(address, stride, new Point((int)Math.Floor(intery) + 1, x), color,
                    intery - Math.Floor(intery));
            }
            else
            {
                SetPixel(address, stride, new Point(x, (int)Math.Floor(intery)), color,
                    1 - (intery - Math.Floor(intery)));
                SetPixel(address, stride, new Point(x, (int)Math.Floor(intery) + 1), color,
                    intery - Math.Floor(intery));
            }

            intery += gradient;
        }

        // Render updated bitmap
        WhiteboardRenderer.InvalidateVisual();
    }
}