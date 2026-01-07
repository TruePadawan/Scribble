using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;

namespace Scribble.Lib.BitmapRenderer;

public abstract class BitmapRendererBase
{
    protected double SmoothStep(double edge0, double edge1, double x)
    {
        if (Math.Abs(edge1 - edge0) < 1e-9)
            return x < edge0 ? 1.0 : 0.0;
        double t = (x - edge0) / (edge1 - edge0);
        t = Math.Clamp(t, 0.0, 1.0);
        return t * t * (3 - 2 * t);
    }

    // Use SIMD instructions to quickly clear the bitmap
    public unsafe void ClearBitmap(ILockedFramebuffer buffer, Color backgroundColor)
    {
        var address = buffer.Address;
        var stride = buffer.RowBytes;
        var bitmapPtr = (byte*)address.ToPointer();
        int width = buffer.Size.Width;
        int height = buffer.Size.Height;

        // Construct the 32bit pixel value for the background color
        var pixelValue = (uint)((backgroundColor.A << 24) |
                                (backgroundColor.R << 16) |
                                (backgroundColor.G << 8) |
                                backgroundColor.B);

        // If stride matches width, memory is contiguous.
        // Fill the entire buffer in one go using SIMD
        if (stride == width * 4)
        {
            new Span<uint>(bitmapPtr, width * height).Fill(pixelValue);
        }
        else
        {
            // Fill row by row if there is padding
            for (int y = 0; y < height; y++)
            {
                var rowStart = (uint*)(bitmapPtr + y * stride);
                new Span<uint>(rowStart, width).Fill(pixelValue);
            }
        }
    }

    public abstract void DrawSinglePoint(ILockedFramebuffer buffer, Point coord, Color strokeColor, int strokeWidth);

    public abstract void DrawStroke(ILockedFramebuffer buffer, Point start, Point end, Color strokeColor,
        int strokeWidth);

    public abstract void EraseSinglePoint(ILockedFramebuffer buffer, Point coord, Color backgroundColor, int strokeWidth);

    public abstract void EraseStroke(ILockedFramebuffer buffer, Point start, Point end, Color backgroundColor,
        int strokeWidth);
}