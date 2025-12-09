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

            // Existing destination pixel (straight/un-premultiplied BGRA)
            byte dstB8 = p[offset + 0];
            byte dstG8 = p[offset + 1];
            byte dstR8 = p[offset + 2];
            byte dstA8 = p[offset + 3];

            // Effective source alpha: callers pass coverage-weighted opacity in [0..1]
            double srcA = Math.Clamp(opacity, 0.0, 1.0);
            if (srcA <= 0.0) return;

            // Normalize to [0..1]
            double dstA = dstA8 / 255.0;
            double dstB = dstB8 / 255.0;
            double dstG = dstG8 / 255.0;
            double dstR = dstR8 / 255.0;

            double srcB = color.B / 255.0;
            double srcG = color.G / 255.0;
            double srcR = color.R / 255.0;

            // Porter-Duff "source over" compositing in straight alpha
            double outA = srcA + dstA * (1.0 - srcA);
            double outB, outG, outR;
            if (outA > 1e-6)
            {
                outB = (srcB * srcA + dstB * dstA * (1.0 - srcA)) / outA;
                outG = (srcG * srcA + dstG * dstA * (1.0 - srcA)) / outA;
                outR = (srcR * srcA + dstR * dstA * (1.0 - srcA)) / outA;
            }
            else
            {
                outB = outG = outR = 0.0;
            }

            p[offset + 0] = (byte)Math.Round(Math.Clamp(outB, 0.0, 1.0) * 255.0);
            p[offset + 1] = (byte)Math.Round(Math.Clamp(outG, 0.0, 1.0) * 255.0);
            p[offset + 2] = (byte)Math.Round(Math.Clamp(outR, 0.0, 1.0) * 255.0);
            p[offset + 3] = (byte)Math.Round(Math.Clamp(outA, 0.0, 1.0) * 255.0);
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