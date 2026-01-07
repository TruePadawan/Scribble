using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Scribble.Lib;
using Scribble.ViewModels;

namespace Scribble.Tools.PointerTools.DrawTool;

public class DrawTool : PointerToolsBase
{
    private Color _strokeColor = Colors.Red;
    private int _strokeWidth = 1;
    private readonly List<Point> _currentStrokePoints = [];

    public DrawTool(string name, MainViewModel viewModel) : base(name, viewModel,
        LoadToolBitmap(typeof(DrawTool), "draw.png"))
    {
        Cursor = new Cursor(ToolIcon, new PixelPoint(0, 50));
    }

    public override void HandlePointerMove(Point prevCoord, Point currentCoord)
    {
        // Draw but don't save any event till the mouse/pointer is released
        using var frameBuffer = ViewModel.WhiteboardBitmap.Lock();
        ViewModel.BitmapRenderer.DrawStroke(frameBuffer, prevCoord, currentCoord, _strokeColor, _strokeWidth);

        // Accumulate points for the stroke
        _currentStrokePoints.Add(currentCoord);
    }

    public override void HandlePointerClick(Point coord)
    {
        _currentStrokePoints.Clear();

        using var frameBuffer = ViewModel.WhiteboardBitmap.Lock();
        ViewModel.BitmapRenderer.DrawSinglePoint(frameBuffer, coord, _strokeColor, _strokeWidth);

        _currentStrokePoints.Add(coord);
    }

    public override void HandlePointerRelease(Point prevCoord, Point currentCoord)
    {
        if (_currentStrokePoints.Count == 0) return;

        if (_currentStrokePoints.Count == 1)
        {
            // Dealing with a mouse/pointer click
            ViewModel.EventsManager.Apply(new PointDrawn(_currentStrokePoints[0], _strokeColor, _strokeWidth), true);
        }
        else
        {
            // Dealing with click + drag

            // Add a final dab if the line is long enough
            double dx = currentCoord.X - prevCoord.X;
            double dy = currentCoord.Y - prevCoord.Y;
            double dist2 = dx * dx + dy * dy;
            if (dist2 > 1e-4)
            {
                _currentStrokePoints.Add(currentCoord);
            }

            var fullStrokeEvent = new PointsDrawn([.._currentStrokePoints], _strokeColor, _strokeWidth);
            ViewModel.EventsManager.Apply(fullStrokeEvent, skipRendering: true);
        }
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
}