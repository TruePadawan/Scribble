using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Scribble.ViewModels;

namespace Scribble.Views;

public partial class MainView : UserControl
{
    private Point _prevCoord;
    private const double MinZoom = 1f;
    private const double MaxZoom = 3f;

    public MainView()
    {
        InitializeComponent();
        _prevCoord = new Point(-1, -1);

        // Center the whiteboard
        (double canvasWidth, double canvasHeight) = GetDataContext().GetCanvasDimensions();
        CanvasScrollViewer.Offset = new Vector(canvasWidth / 2, canvasHeight / 2);
    }

    private MainViewModel GetDataContext() => DataContext as MainViewModel ?? new MainViewModel();

    private void MainCanvas_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var pointerCoordinates = e.GetPosition(sender as Control);
        var hasLastCoordinates = !_prevCoord.Equals(new Point(-1, -1));

        if (e.Properties.IsLeftButtonPressed && hasLastCoordinates)
        {
            GetDataContext().DrawLine(_prevCoord, pointerCoordinates, Colors.Red);
            WhiteboardRenderer.InvalidateVisual();
        }

        _prevCoord = e.GetPosition(sender as Control);
    }

    private void MainCanvas_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pointerCoordinates = e.GetPosition(sender as Control);
        if (e.Properties.IsLeftButtonPressed)
        {
            GetDataContext().DrawSinglePixel(pointerCoordinates, Colors.Red);
            WhiteboardRenderer.InvalidateVisual();
        }
    }
    
    private void MainCanvas_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Reset the last coordinates when the mouse is released
        _prevCoord = new Point(-1, -1);
    }

    private void MainCanvas_OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        bool ctrlKeyIsActive = (e.KeyModifiers & KeyModifiers.Control) != 0;
        if (!ctrlKeyIsActive) return;

        // ZOOM TO POINT
        double currentScale = GetDataContext().GetCurrentScale();
        Point mousePosOnViewPort = e.GetPosition(CanvasScrollViewer);
        Point mousePosOnCanvas = e.GetPosition(MainCanvas);

        // Multiplicative Zoom
        double zoomFactor = e.Delta.Y > 0 ? 1.1f : 0.9f;
        double newScale = currentScale * zoomFactor;

        // Clamp new zoom between min and max zoom
        newScale = Math.Max(MinZoom, Math.Min(newScale, MaxZoom));
        
        // Do nothing if there was no change in zoom, i.e., I'm at the min or max zoom
        if (Math.Abs(newScale - currentScale) < 0.0001f)
        {
            return;
        }

        GetDataContext().SetCurrentScale(newScale);

        // Force the scroll viewer to update its layout before calculating a new offset
        CanvasScrollViewer.UpdateLayout();

        // Implement zoom to point
        var newOffset = (mousePosOnCanvas * newScale) - mousePosOnViewPort;
        CanvasScrollViewer.Offset = new Vector(newOffset.X, newOffset.Y);
        
        // Stop the scroll viewer from applying its own scrolling logic
        e.Handled = true;
    }

}