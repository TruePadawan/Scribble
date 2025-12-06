using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Scribble.Behaviours;
using Scribble.Tools.PointerTools;
using Scribble.ViewModels;

namespace Scribble.Views;

public partial class MainView : UserControl
{
    private Point _prevCoord;
    private const double MinZoom = 1f;
    private const double MaxZoom = 3f;
    private PointerToolsBase? _activePointerTool;
    private MainViewModel? _viewModel;

    public MainView()
    {
        InitializeComponent();
        _prevCoord = new Point(-1, -1);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainViewModel viewModel)
        {
            _viewModel = viewModel;

            Toolbar.Children.Clear();

            // Center the whiteboard
            (double canvasWidth, double canvasHeight) = viewModel.GetCanvasDimensions();
            CanvasScrollViewer.Offset = new Vector(canvasWidth / 2, canvasHeight / 2);

            // Register pointer tools
            RegisterPointerTool(new DrawTool("DrawToolButton", viewModel,
                new Bitmap(AssetLoader.Open(new Uri("avares://Scribble/Assets/draw.png")))));
            RegisterPointerTool(new EraseTool("EraseTool", viewModel,
                new Bitmap(AssetLoader.Open(new Uri("avares://Scribble/Assets/eraser.png")))));
        }
    }

    private void RegisterPointerTool(PointerToolsBase tool)
    {
        var toggleButton = new ToggleButton
        {
            Name = tool.Name,
            Width = 50,
            Margin = new Thickness(4),
        };
        ToggleButtonGroup.SetGroupName(toggleButton, "PointerTools");
        toggleButton.IsCheckedChanged += (object? sender, RoutedEventArgs e) =>
        {
            if (toggleButton.IsChecked == true)
            {
                _activePointerTool = tool;
            }
        };

        var toolIcon = new Image
        {
            Source = tool.ToolIcon
        };
        toggleButton.Content = toolIcon;

        Toolbar.Children.Add(toggleButton);
    }

    private void MainCanvas_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var pointerCoordinates = e.GetPosition(sender as Control);
        var hasLastCoordinates = !_prevCoord.Equals(new Point(-1, -1));

        if (e.Properties.IsLeftButtonPressed && hasLastCoordinates)
        {
            _activePointerTool?.HandlePointerMove(_prevCoord, pointerCoordinates);
            WhiteboardRenderer.InvalidateVisual();
        }

        _prevCoord = e.GetPosition(sender as Control);
    }

    private void MainCanvas_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pointerCoordinates = e.GetPosition(sender as Control);
        if (e.Properties.IsLeftButtonPressed)
        {
            _activePointerTool?.HandlePointerClick(pointerCoordinates);
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
        if (_viewModel == null) throw new Exception("View Model not initialized");

        double currentScale = _viewModel.GetCurrentScale();
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

        _viewModel.SetCurrentScale(newScale);

        // Force the scroll viewer to update its layout before calculating a new offset
        CanvasScrollViewer.UpdateLayout();

        // Implement zoom to point
        var newOffset = (mousePosOnCanvas * newScale) - mousePosOnViewPort;
        CanvasScrollViewer.Offset = new Vector(newOffset.X, newOffset.Y);

        // Stop the scroll viewer from applying its own scrolling logic
        e.Handled = true;
    }
}