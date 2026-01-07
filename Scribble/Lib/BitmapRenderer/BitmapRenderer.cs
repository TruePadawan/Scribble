using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;

namespace Scribble.Lib.BitmapRenderer;

public class BitmapRenderer : BitmapRendererBase
{
    private const int BytesPerPixel = 4;

    // Draw an AA thick segment without round end caps
    private void DrawSegmentNoCaps(ILockedFramebuffer buffer, Point start, Point end, Color color, int strokeWidth)
    {
        var opacity = color.A / 255f;
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

                // Signed distance to an axis-aligned rectangle in the line's local space
                double qx = Math.Abs(u) - halfLen;
                double qy = Math.Abs(v) - halfWidth;
                double ox = Math.Max(qx, 0.0);
                double oy = Math.Max(qy, 0.0);
                double outside = Math.Sqrt(ox * ox + oy * oy);
                double inside = Math.Min(Math.Max(qx, qy), 0.0);
                double sd = outside + inside; // signed distance to rectangle (negative inside)

                // Convert geometric distance to coverage using a ~1px smooth edge
                double a = SmoothStep(1.0, 0.0, sd);
                if (a <= 0) continue;

                SetPixel(buffer, new Point(x, y), color, (float)(a * opacity));
            }
        }
    }

    private void EraseSegmentNoCaps(ILockedFramebuffer buffer, Point start, Point end, Color bgColor, int strokeWidth)
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
                    SetPixel(buffer, new Point(x, y), bgColor, 1f);
                }
            }
        }
    }

    private unsafe void SetPixel(ILockedFramebuffer buffer, Point coord, Color color, double opacity)
    {
        var address = buffer.Address;
        var stride = buffer.RowBytes;

        int width = buffer.Size.Width;
        int height = buffer.Size.Height;
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

            // Bypass complex Porter-Duff alpha blending when the source opacity is 1.0.
            if (outA > 1.0 - 1e-6 && srcA > 1.0 - 1e-6)
            {
                p[offset + 0] = color.B;
                p[offset + 1] = color.G;
                p[offset + 2] = color.R;
                p[offset + 3] = color.A;
            }
            else
            {
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
    }

    public override void DrawSinglePoint(ILockedFramebuffer buffer, Point coord, Color color, int strokeWidth)
    {
        var opacity = color.A / 255f;

        strokeWidth = Math.Max(1, strokeWidth);
        double halfWidth = strokeWidth / 2.0;

        // Render an anti-aliased circular dab centered at coord
        double cx = coord.X;
        double cy = coord.Y;
        int minX = (int)Math.Floor(cx - halfWidth - 1);
        int maxX = (int)Math.Ceiling(cx + halfWidth + 1);
        int minY = (int)Math.Floor(cy - halfWidth - 1);
        int maxY = (int)Math.Ceiling(cy + halfWidth + 1);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                double px = x + 0.5;
                double py = y + 0.5;
                double ddx = px - cx;
                double ddy = py - cy;
                double d = Math.Sqrt(ddx * ddx + ddy * ddy);
                // Signed distance to the circle boundary (negative inside)
                double sd = d - halfWidth;
                // 1-pixel soft edge
                double a = SmoothStep(1.0, 0.0, sd);
                if (a <= 0) continue;
                SetPixel(buffer, new Point(x, y), color, (float)(a * opacity));
            }
        }
    }

    // Draw an interior segment without round caps to avoid over-dark joints,
    // then place a single circular dab at the current point to form a smooth join.
    public override void DrawStroke(ILockedFramebuffer buffer, Point start, Point end, Color strokeColor,
        int strokeWidth)
    {
        DrawSegmentNoCaps(buffer, start, end, strokeColor, strokeWidth);
        DrawSinglePoint(buffer, end, strokeColor, strokeWidth);
    }

    // Solid (non-AA) circular dab for performance
    public override void EraseSinglePoint(ILockedFramebuffer buffer, Point coord, Color bgColor,
        int strokeWidth)
    {
        strokeWidth = Math.Max(1, strokeWidth);
        double halfWidth = strokeWidth / 2.0;
        double r2 = halfWidth * halfWidth;

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
                SetPixel(buffer, new Point(x, y), bgColor, 1f);
            }
        }
    }

    public override void EraseStroke(ILockedFramebuffer buffer, Point start, Point end, Color bgColor,
        int strokeWidth)
    {
        EraseSegmentNoCaps(buffer, start, end, bgColor, strokeWidth);
        EraseSinglePoint(buffer, end, bgColor, strokeWidth);
    }
}