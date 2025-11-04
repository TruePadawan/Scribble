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
    private const int BytesPerPixel = 4;
    private readonly Vector _dpi = new(96, 96);
    
    public MainView()
    {
        InitializeComponent();
        _prevCoord = new Point(-1, -1);
        
        // Initialize bitmap with placeholder size
        _whiteboardBitmap = new WriteableBitmap(new PixelSize(10, 10), _dpi, PixelFormat.Bgra8888);
        
        this.SizeChanged += OnSizeChanged;
    }
    
    // Ensure that bitmap's dimensions match the window
    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (e.NewSize is { Width: > 0, Height: > 0 })
        {
            // Create a new Bitmap
            CreateBitmap((int)e.NewSize.Width, (int)e.NewSize.Height);
        }
    }

    private void CreateBitmap(int width, int height)
    {
        PixelSize whiteboardDimensions = new PixelSize(width, height);
        _whiteboardBitmap = new WriteableBitmap(whiteboardDimensions, _dpi, PixelFormat.Bgra8888);
        
        // Give it a white background
        ClearBitmap(Colors.White);
        WhiteboardRenderer.Source = _whiteboardBitmap;
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

    private unsafe void SetPixel(IntPtr address, int stride, Point coord, Color color, double opacity)
    {
        int width = _whiteboardBitmap.PixelSize.Width;
        int height = _whiteboardBitmap.PixelSize.Height;
        (double x, double y) = coord;
        
        if (x >= 0 && x < width && y >= 0 && y < height)
        {
            long offset = (long)y * stride + (long)x * BytesPerPixel;

            byte* p = (byte*)address.ToPointer();
            // Account for opacity
            byte alpha = (byte)(color.A * opacity);
            p[offset + 0] = color.B;
            p[offset + 1] = color.G;
            p[offset + 2] = color.R;
            p[offset + 3] = alpha;
        }
    }
    
    // Draw lines using Xiaolin Wu's Line Algorithm
    private void DrawLine(Point start, Point end, Color color)
    {
        // Check if the line segment is longer on the x or y-axis to know if we have a horizontal or vertical line
        using var frame = _whiteboardBitmap.Lock();
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
        
        // Render updated bitmap
        WhiteboardRenderer.InvalidateVisual();
    }
}