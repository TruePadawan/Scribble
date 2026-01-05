using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Scribble.ViewModels;

namespace Scribble.Tools.PointerTools.DrawTool;

public class DrawTool : PointerToolsBase
{
    private Color _strokeColor = Colors.Red;
    private int _strokeWidth = 1;

    public DrawTool(string name, MainViewModel viewModel) : base(name, viewModel,
        LoadToolBitmap(typeof(DrawTool), "draw.png"))
    {
        Cursor = new Cursor(ToolIcon, new PixelPoint(0, 50));
    }

    public override void HandlePointerMove(Point prevCoord, Point currentCoord)
    {
        float opacity = _strokeColor.A / 255f;
        using var frame = ViewModel.WhiteboardBitmap.Lock();
        IntPtr address = frame.Address;
        int stride = frame.RowBytes;
        // Draw an interior segment without round caps to avoid over-dark joints,
        // then place a single circular dab at the current point to form a smooth join.
        DrawSegmentNoCaps(address, stride, prevCoord, currentCoord, _strokeColor, _strokeWidth, opacity);
        DrawSinglePixel(address, stride, currentCoord, _strokeColor, _strokeWidth, opacity);
    }

    public override void HandlePointerClick(Point coord)
    {
        ViewModel.StartStateCapture();
        float opacity = _strokeColor.A / 255f;
        using var frame = ViewModel.WhiteboardBitmap.Lock();
        IntPtr address = frame.Address;
        int stride = frame.RowBytes;
        DrawSinglePixel(address, stride, coord, _strokeColor, _strokeWidth, opacity);
    }

    public override void HandlePointerRelease(Point prevCoord, Point currentCoord)
    {
        double dx = currentCoord.X - prevCoord.X;
        double dy = currentCoord.Y - prevCoord.Y;
        double dist2 = dx * dx + dy * dy;
        if (dist2 > 1e-4)
        {
            HandlePointerClick(currentCoord);
        }

        ViewModel.StopStateCapture();
    }

    public override bool RenderOptions(Panel parent)
    {
        // Render a slider for controlling the stroke width and a color picker for stroke color
        Slider slider = new Slider
        {
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Minimum = 1,
            Maximum = 10,
            Value = _strokeWidth
        };
        slider.ValueChanged += ((sender, args) => { _strokeWidth = (int)args.NewValue; });
        slider.Padding = new Thickness(8, 0);

        ColorPicker colorPicker = new ColorPicker
        {
            Color = _strokeColor
        };
        colorPicker.ColorChanged += (sender, args) => { _strokeColor = args.NewColor; };

        parent.Children.Add(CreateOptionControl(colorPicker, "Color"));
        parent.Children.Add(CreateOptionControl(slider, "Thickness"));
        parent.Width = 180;
        return true;
    }

    private void DrawSinglePixel(IntPtr address, int stride, Point coord, Color color, int strokeWidth = 1,
        float opacity = 1f)
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
                ViewModel.SetPixel(address, stride, new Point(x, y), color, (float)(a * opacity));
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
                    ViewModel.SetPixel(address, stride, new Point(x, y), color, (float)(a * opacity));
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

                ViewModel.SetPixel(address, stride, new Point(x, y), color, (float)(a * opacity));
            }
        }
    }

    // Internal helper: draw an AA thick segment without round end caps (rectangle SDF).
    private void DrawSegmentNoCaps(IntPtr address, int stride, Point start, Point end, Color color, int strokeWidth = 1,
        float opacity = 1f)
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

                ViewModel.SetPixel(address, stride, new Point(x, y), color, (float)(a * opacity));
            }
        }
    }

    private static double SmoothStep(double edge0, double edge1, double x)
    {
        if (Math.Abs(edge1 - edge0) < 1e-9)
            return x < edge0 ? 1.0 : 0.0;
        double t = (x - edge0) / (edge1 - edge0);
        t = Math.Clamp(t, 0.0, 1.0);
        return t * t * (3 - 2 * t);
    }
}