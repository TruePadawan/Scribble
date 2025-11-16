using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Scribble.Views;

enum WindowQuadrant
{
    UpperLeft, UpperRight, LowerLeft, LowerRight
}

public partial class MainView : UserControl
{
    private Point _prevCoord;
    private readonly WriteableBitmap _whiteboardBitmap;
    private readonly Vector _dpi = new(96, 96);
    private const int BytesPerPixel = 4;
    private const int CanvasWidth = 5000;
    private const int CanvasHeight = 5000;
    private readonly ScaleTransform _scaleTransform;
    private double _zoomFactor = 0.25f;
    private const double MinZoom = 1f;
    private const double MaxZoom = 3f;
    private Color _canvasBackgroundColor = Colors.White;

    public MainView()
    {
        InitializeComponent();
        _prevCoord = new Point(-1, -1);

        _scaleTransform = new ScaleTransform(1, 1);
        MainCanvas.RenderTransform = _scaleTransform;

        // Initialize the bitmap with a large dimension
        _whiteboardBitmap = new WriteableBitmap(new PixelSize(CanvasWidth, CanvasHeight), _dpi, PixelFormat.Bgra8888);
        ClearBitmap(_canvasBackgroundColor);
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
        } else if (e.Properties.IsRightButtonPressed && hasLastCoordinates)
        {
            DrawLine(_prevCoord, pointerCoordinates, _canvasBackgroundColor);
        }

        _prevCoord = e.GetPosition(sender as Control);
    }

    private void MainCanvas_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pointerCoordinates = e.GetPosition(sender as Control);
        if (e.Properties.IsLeftButtonPressed)
        {
            DrawSinglePixel(pointerCoordinates, Colors.Red);
        } else if (e.Properties.IsRightButtonPressed)
        {
            DrawSinglePixel(pointerCoordinates, _canvasBackgroundColor);
        }
    }
    
    private void MainCanvas_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Reset the last coordinates when the mouse is released
        _prevCoord = new Point(-1, -1);
    }

    /**
     * TODO: Refine the zoom functionality further
     * Center the zoom
     * The direction of the zoom should match what part of the window the cursor lies in
     */
    private void MainCanvas_OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        bool ctrlKeyIsActive = (e.KeyModifiers & KeyModifiers.Control) != 0;
        if (!ctrlKeyIsActive) return;

        Point pointerCanvasPosPreZoom = e.GetPosition(sender as Control) / _scaleTransform.ScaleX;
        if (e.Delta.Y > 0)
        {
            ZoomIn(GetPointerQuadrant(e.GetPosition(this)));
        }
        else
        {
            ZoomOut(GetPointerQuadrant(e.GetPosition(this)));
        }
        Point pointerCanvasPosPostZoom = e.GetPosition(sender as Control) / _scaleTransform.ScaleX;

        Console.WriteLine("--BEGIN--");
        Console.WriteLine(pointerCanvasPosPreZoom);
        Console.WriteLine(pointerCanvasPosPostZoom);
        Console.WriteLine("--END--");

        // Zoom correction, the zoom should be about the pointer position
        var scrollViewerOffset = pointerCanvasPosPreZoom - pointerCanvasPosPostZoom;
        (double offsetX, double offsetY) = CanvasScrollViewer.Offset;
        CanvasScrollViewer.Offset = new Vector(offsetX + scrollViewerOffset.X, offsetY + scrollViewerOffset.Y);
        Console.WriteLine(e.GetPosition(MainCanvas));
    }

    private WindowQuadrant GetPointerQuadrant(Point pointerPos)
    {
        if (pointerPos.X > this.Bounds.Width || pointerPos.Y > this.Bounds.Height)
        {
            throw new Exception("Pointer position must be relative to the app window!");
        }

        var halfWidth = this.Bounds.Width / 2;
        var halfHeight = this.Bounds.Height / 2;

        if (pointerPos.Y < halfHeight)
        {
            return pointerPos.X < halfWidth ? WindowQuadrant.UpperLeft : WindowQuadrant.UpperRight;
        }
        else
        {
            return pointerPos.X < halfWidth ? WindowQuadrant.LowerLeft : WindowQuadrant.LowerRight;
        }
    }
    
    // TODO: Offset shouldn't go outside the canvas
    private void ZoomIn(WindowQuadrant pointerQuadrant)
    {
        _scaleTransform.ScaleX = double.Min(_scaleTransform.ScaleX + _zoomFactor, MaxZoom);
        _scaleTransform.ScaleY = double.Min(_scaleTransform.ScaleY + _zoomFactor, MaxZoom);
    }

    // TODO: Fix buggy zoom out; the logic is insufficient for when the pointer quadrant is changed from where it was at zoom in
    private void ZoomOut(WindowQuadrant pointerQuadrant)
    {
        _scaleTransform.ScaleX = double.Max(_scaleTransform.ScaleX - _zoomFactor, MinZoom);
        _scaleTransform.ScaleY = double.Max(_scaleTransform.ScaleY - _zoomFactor, MinZoom);
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

    private void DrawSinglePixel(Point coord, Color color)
    {
        using var frame = _whiteboardBitmap.Lock();
        IntPtr address = frame.Address;
        int stride = frame.RowBytes;
        
        SetPixel(address, stride, coord, color, 1f);
        
        // Render updated bitmap
        WhiteboardRenderer.InvalidateVisual();
    }
}