using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Scribble.Behaviours;
using Scribble.Tools.PointerTools;
using Scribble.Tools.PointerTools.DrawTool;
using Scribble.Tools.PointerTools.EraseTool;
using Scribble.Tools.PointerTools.PanningTool;
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
            RegisterPointerTool(new DrawTool("DrawToolButton", viewModel));
            RegisterPointerTool(new EraseTool("EraseTool", viewModel));
            RegisterPointerTool(new PanningTool("PanningTool", viewModel, CanvasScrollViewer));
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Select the first tool by default
        if (Toolbar.Children.Count > 0 && Toolbar.Children[0] is ToggleButton firstButton)
        {
            firstButton.IsChecked = true;

            // Schedules the focus attempt for the next UI cycle
            // This is required for undo/redo to work immediately
            Dispatcher.UIThread.Post(() => firstButton.Focus());
        }
    }

    private void RegisterPointerTool(PointerToolsBase tool)
    {
        var toggleButton = new ToggleButton
        {
            Name = tool.Name,
            Width = 50,
            Height = 50,
            Margin = new Thickness(4),
        };
        ToggleButtonGroup.SetGroupName(toggleButton, "PointerTools");
        toggleButton.IsCheckedChanged += (object? sender, RoutedEventArgs e) =>
        {
            if (toggleButton.IsChecked != true) return;
            _activePointerTool = tool;

            // Render tool options
            ToolOptions.Children.Clear();
            if (tool.RenderOptions(ToolOptions))
            {
                ToolOptionsContainer.IsVisible = true;
                ToolOptionsContainer.Opacity = 1;
            }
            else
            {
                ToolOptionsContainer.IsVisible = false;
            }

            if (tool.Cursor != null)
            {
                MainCanvas.Cursor = tool.Cursor;
            }
        };

        var toolIcon = new Image
        {
            Source = tool.ToolIcon,
            Margin = new Thickness(4)
        };
        toggleButton.Content = toolIcon;

        Toolbar.Children.Add(toggleButton);
    }

    private Point GetPointerPosition(PointerEventArgs e)
    {
        // For panning, we need coordinates relative to the viewport (ScrollViewer)
        // because Canvas itself moves, creating a feedback loop if we use its coordinates
        if (_activePointerTool is PanningTool)
        {
            return e.GetPosition(CanvasScrollViewer);
        }

        // For drawing/erasing, we need coordinates relative to the Canvas content
        return e.GetPosition(MainCanvas);
    }

    private void MainCanvas_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var pointerCoordinates = GetPointerPosition(e);
        var hasLastCoordinates = !_prevCoord.Equals(new Point(-1, -1));

        if (e.Properties.IsLeftButtonPressed && hasLastCoordinates)
        {
            _activePointerTool?.HandlePointerMove(_prevCoord, pointerCoordinates);
            WhiteboardRenderer.InvalidateVisual();
        }

        _prevCoord = pointerCoordinates;
    }

    private void MainCanvas_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pointerCoordinates = GetPointerPosition(e);
        if (e.Properties.IsLeftButtonPressed)
        {
            _prevCoord = pointerCoordinates;
            _activePointerTool?.HandlePointerClick(pointerCoordinates);
            WhiteboardRenderer.InvalidateVisual();
        }
    }

    private void MainCanvas_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var pointerCoordinates = GetPointerPosition(e);
        // If the stroke was active with the left button, place a final round dab at the release point
        // only if we didn't already place one there on the last move (to avoid over-darkening).
        if (e.InitialPressMouseButton == MouseButton.Left)
        {
            _activePointerTool?.HandlePointerRelease(_prevCoord, pointerCoordinates);
            WhiteboardRenderer.InvalidateVisual();
        }

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

    public void Undo()
    {
        _viewModel?.UndoLastOperation();
        WhiteboardRenderer.InvalidateVisual();
    }

    public void Redo()
    {
        _viewModel?.RedoLastOperation();
        WhiteboardRenderer.InvalidateVisual();
    }
}