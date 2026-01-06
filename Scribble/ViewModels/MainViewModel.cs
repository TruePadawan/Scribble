using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Scribble.Lib;

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

    public readonly EventsManager EventsManager;
    private List<PixelState> _pixelsState = [];
    private Stack<List<PixelState>> _undoOperations = [];
    private Stack<List<PixelState>> _redoOperations = [];
    private bool _isCapturingState = false;


    public MainViewModel()
    {
        BackgroundColor = Colors.Black;
        ScaleTransform = new ScaleTransform(1, 1);
        EventsManager = new EventsManager(this);

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

    private unsafe void SetPixel(IntPtr address, int stride, Point coord, Color color, double opacity)
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

            if (_isCapturingState)
            {
                _pixelsState.Add(new PixelState(offset, new Color(dstA8, dstR8, dstG8, dstB8)));
            }
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

    public void StartStateCapture()
    {
        _isCapturingState = true;
        _pixelsState = [];
    }

    public void StopStateCapture()
    {
        _isCapturingState = false;
        _undoOperations.Push(_pixelsState);

        // Clear the redo stack when the undo 'root' changes
        if (_redoOperations.Count > 0)
        {
            _redoOperations.Clear();
        }
    }

    public unsafe void UndoLastOperation()
    {
        if (_undoOperations.Count == 0) return;

        using var frame = WhiteboardBitmap.Lock();
        IntPtr address = frame.Address;
        byte* p = (byte*)address.ToPointer();

        var operationsToBeUndone = _undoOperations.Pop();
        List<PixelState> operationsToBeRedone = new List<PixelState>(operationsToBeUndone.Count);

        for (int i = operationsToBeUndone.Count - 1; i >= 0; i--)
        {
            var (offset, color) = operationsToBeUndone[i];
            byte dstB8 = p[offset + 0];
            byte dstG8 = p[offset + 1];
            byte dstR8 = p[offset + 2];
            byte dstA8 = p[offset + 3];
            var currentPixelState = new PixelState(offset, new Color(dstA8, dstR8, dstG8, dstB8));
            operationsToBeRedone.Add(currentPixelState);

            p[offset] = color.B;
            p[offset + 1] = color.G;
            p[offset + 2] = color.R;
            p[offset + 3] = color.A;
        }

        _redoOperations.Push(operationsToBeRedone);
    }

    public unsafe void RedoLastOperation()
    {
        if (_redoOperations.Count == 0) return;

        using var frame = WhiteboardBitmap.Lock();
        IntPtr address = frame.Address;
        var p = (byte*)address.ToPointer();

        var operationsToBeRedone = _redoOperations.Pop();
        List<PixelState> operationsToBeUndone = new List<PixelState>(operationsToBeRedone.Count);

        for (int i = operationsToBeRedone.Count - 1; i >= 0; i--)
        {
            var (offset, color) = operationsToBeRedone[i];
            byte dstB8 = p[offset + 0];
            byte dstG8 = p[offset + 1];
            byte dstR8 = p[offset + 2];
            byte dstA8 = p[offset + 3];
            var currentPixelState = new PixelState(offset, new Color(dstA8, dstR8, dstG8, dstB8));
            operationsToBeUndone.Add(currentPixelState);

            p[offset] = color.B;
            p[offset + 1] = color.G;
            p[offset + 2] = color.R;
            p[offset + 3] = color.A;
        }

        _undoOperations.Push(operationsToBeUndone);
    }

    private double SmoothStep(double edge0, double edge1, double x)
    {
        if (Math.Abs(edge1 - edge0) < 1e-9)
            return x < edge0 ? 1.0 : 0.0;
        double t = (x - edge0) / (edge1 - edge0);
        t = Math.Clamp(t, 0.0, 1.0);
        return t * t * (3 - 2 * t);
    }

    public void DrawSinglePixel(IntPtr address, int stride, Point coord, Color color, int strokeWidth, float opacity)
    {
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
                SetPixel(address, stride, new Point(x, y), color, (float)(a * opacity));
            }
        }
    }

    // Draw an AA thick segment without round end caps
    public void DrawSegmentNoCaps(IntPtr address, int stride, Point start, Point end, Color color, int strokeWidth,
        float opacity)
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

                SetPixel(address, stride, new Point(x, y), color, (float)(a * opacity));
            }
        }
    }

    // Solid (non-AA) circular dab for performance
    public void EraseSinglePixel(IntPtr address, int stride, Point coord, int strokeWidth)
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
                SetPixel(address, stride, new Point(x, y), BackgroundColor, 1f);
            }
        }
    }

    public void EraseSegmentNoCaps(IntPtr address, int stride, Point start, Point end, int strokeWidth)
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
                    SetPixel(address, stride, new Point(x, y), BackgroundColor, 1f);
                }
            }
        }
    }

    // Anti-aliased thick line with butt/square caps (signed-distance field)
    // Notes: Bounding box is expanded by radius+1 for AA fringe; zero-length strokes render a circular dab.
    private void DrawLine(IntPtr address, int stride, Point start, Point end, Color color, int strokeWidth = 1,
        float opacity = 1f)
    {
        strokeWidth = Math.Max(1, strokeWidth);

        // Compute geometry
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double len2 = dx * dx + dy * dy;
        double len = Math.Sqrt(len2);

        double halfWidth = strokeWidth / 2.0;

        // Fast path for zero-length (tap): render a circular dab with AA
        if (len < 1e-6)
        {
            double cx = start.X;
            double cy = start.Y;
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
                    // Smooth edge from r to r+1 pixel
                    double a = SmoothStep(halfWidth + 1.0, halfWidth, d);
                    if (a <= 0) continue;
                    SetPixel(address, stride, new Point(x, y), color, (float)(a * opacity));
                }
            }

            return;
        }

        // Unit direction and perpendicular
        double ux = dx / len;
        double uy = dy / len;
        double pxn = -uy;
        double pyn = ux;

        // Midpoint and half-length (for round caps)
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
                // Pixel center in line's local coordinates (u along, v across), centered at segment midpoint
                double cx = x + 0.5 - mx;
                double cy = y + 0.5 - my;
                double u = cx * ux + cy * uy; // along the segment
                double v = cx * pxn + cy * pyn; // perpendicular to the segment

                // Signed distance to a capsule (segment with round caps)
                double du = Math.Max(Math.Abs(u) - halfLen, 0.0);
                double sd = Math.Sqrt(du * du + v * v) - halfWidth;

                // Convert geometric distance to coverage using a 1px smooth edge
                double a = SmoothStep(1.0, 0.0, sd);
                if (a <= 0) continue;

                SetPixel(address, stride, new Point(x, y), color, (float)(a * opacity));
            }
        }
    }
}