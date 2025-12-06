using System;
using Avalonia;
using Avalonia.Media;
using Scribble.ViewModels;

namespace Scribble.Tools.PointerTools;

/**
 * Base class for all Pointer Tools
 * It enforces the data that all Pointer Tools should have
 * Name - The name of the Tool
 * Every PointerTool should consume the ViewModel to perform its operation
 */
public abstract class PointerToolsBase(string name, MainViewModel viewModel, IImage icon)
{
    public string Name { get; } = name;
    protected MainViewModel ViewModel { get; } = viewModel;
    public readonly IImage ToolIcon = icon;

    public abstract void HandlePointerMove(Point prevCoord, Point currentCoord);
    public abstract void HandlePointerClick(Point coord);

    protected unsafe void SetPixel(IntPtr address, int stride, Point coord, Color color, double opacity)
    {
        int width = ViewModel.WhiteboardBitmap.PixelSize.Width;
        int height = ViewModel.WhiteboardBitmap.PixelSize.Height;
        (double x, double y) = coord;

        if (x >= 0 && x < width && y >= 0 && y < height)
        {
            long offset = (long)y * stride + (long)x * MainViewModel.BytesPerPixel;
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
}