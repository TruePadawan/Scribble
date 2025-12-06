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
    public Color BackgroundColor { get; }

    public WriteableBitmap WhiteboardBitmap { get; }
    public ScaleTransform ScaleTransform { get; }


    public MainViewModel()
    {
        BackgroundColor = Colors.Black;
        ScaleTransform = new ScaleTransform(1, 1);

        // Initialize the bitmap with a large dimension
        WhiteboardBitmap = new WriteableBitmap(new PixelSize(CanvasWidth, CanvasHeight), _dpi, PixelFormat.Bgra8888);
        ClearBitmap(BackgroundColor);
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

    public unsafe void SetPixel(IntPtr address, int stride, Point coord, Color color, double opacity)
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

    // TODO: Should keep track of anti-aliased pixels and update them
    public unsafe void ChangeBackgroundColor(Color color)
    {
        if (color == BackgroundColor) return;

        using var frame = WhiteboardBitmap.Lock();
        var address = frame.Address;
        int stride = frame.RowBytes;
        var bitmapPtr = (byte*)address.ToPointer();

        int width = WhiteboardBitmap.PixelSize.Width;
        int height = WhiteboardBitmap.PixelSize.Height;
        for (int y = 0; y < height; ++y)
        {
            for (int x = 0; x < width; ++x)
            {
                long offset = (long)y * stride + (long)x * BytesPerPixel;
                // Update all pixels that are the color of the previous background color to the new one
                var b = bitmapPtr[offset];
                var g = bitmapPtr[offset + 1];
                var r = bitmapPtr[offset + 2];
                var a = bitmapPtr[offset + 3];
                if (b != BackgroundColor.B || g != BackgroundColor.G || r != BackgroundColor.R ||
                    a != BackgroundColor.A) continue;
                bitmapPtr[offset] = color.B;
                bitmapPtr[offset + 1] = color.G;
                bitmapPtr[offset + 2] = color.R;
                bitmapPtr[offset + 3] = color.A;
            }
        }
    }
}