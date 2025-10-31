using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;

namespace Scribble.Views;

public partial class MainView : UserControl
{
    private Point _prevCoord;

    public MainView()
    {
        _prevCoord = new Point(-1, -1);
        InitializeComponent();
    }

    private void MainCanvas_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var pointerCoordinates = e.GetPosition(MainCanvas);
        var hasLastCoordinates = !_prevCoord.Equals(new Point(-1, -1));

        if (e.Properties.IsLeftButtonPressed && hasLastCoordinates)
        {
            DrawLine(_prevCoord, pointerCoordinates);
        }

        _prevCoord = e.GetPosition(MainCanvas);
    }

    private void MainCanvas_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Reset the last coordinates when the mouse is released
        _prevCoord = new Point(-1, -1);
    }

    private void PutPixel(Point coord, double opacity)
    {
        var pixel = new Rectangle
        {
            Width = 2,
            Height = 2,
            Fill = Brushes.Red,
            Opacity = opacity
        };
        Canvas.SetLeft(pixel, coord.X);
        Canvas.SetTop(pixel, coord.Y);
        MainCanvas.Children.Add(pixel);
    }

    // TODO: Implement Xiaolin Wu's line algorithm
    private void DrawLine(Point start, Point end)
    {
        // Check if the line segment is longer on the x or y-axis to know if we have a horizontal or vertical line
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

            for (var i = 0; i < (int)deltaX + 1; i++)
            {
                var x = start.X + i;
                var y = start.Y + (i * slope);
                var pixelIntegerCoord = new Point((int)x, (int)y);

                // Calculate the alpha values used for opacity
                var alpha = y - (int)y;

                PutPixel(pixelIntegerCoord, 1 - alpha);
                PutPixel(pixelIntegerCoord.WithY((int)y + 1), alpha);
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

            for (var i = 0; i < (int)deltaY + 1; i++)
            {
                var x = start.X + (i * slope);
                var y = start.Y + i;
                var pixelIntegerCoord = new Point((int)x, (int)y);

                // Calculate the alpha values used for opacity
                var alpha = x - (int)x;

                PutPixel(pixelIntegerCoord, 1 - alpha);
                PutPixel(pixelIntegerCoord.WithX((int)x + 1), alpha);
            }
        }
    }
}