using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Scribble.Behaviours;
using Scribble.Lib;
using Scribble.Tools.PointerTools;
using Scribble.Tools.PointerTools.ArrowTool;
using Scribble.Tools.PointerTools.EllipseTool;
using Scribble.Tools.PointerTools.EraseTool;
using Scribble.Tools.PointerTools.LineTool;
using Scribble.Tools.PointerTools.PanningTool;
using Scribble.Tools.PointerTools.PencilTool;
using Scribble.Tools.PointerTools.RectangleTool;
using Scribble.Tools.PointerTools.SelectTool;
using Scribble.Tools.PointerTools.TextTool;
using Scribble.Utils;
using Scribble.ViewModels;
using SkiaSharp;

namespace Scribble.Views;

public partial class MainView : UserControl
{
    private Point _prevCoord;
    private const double MinZoom = 1f;
    private const double MaxZoom = 3f;
    private PointerToolsBase? _activePointerTool;
    private MainViewModel? _viewModel;
    private Point _selectionMoveCoord;
    private double _selectionRotationAngle;
    private SKRect _selectionBounds;
    private Point _selectionCenter;
    private Point _scalePivot;
    private Point _scalePrevCoord;
    private string? _activeScaleHandle;

    public MainView()
    {
        InitializeComponent();
        _prevCoord = new Point(-1, -1);
        _selectionMoveCoord = new Point(-1, -1);
        _selectionRotationAngle = double.NaN;
        _selectionBounds = SKRect.Empty;
        _selectionCenter = new Point(-1, -1);
        _scalePivot = new Point(-1, -1);
        _scalePrevCoord = new Point(-1, -1);

        var moveIconBitmap = Bitmap.DecodeToWidth(AssetLoader.Open(new Uri("avares://Scribble/Assets/move.png")), 36);
        var rotateIconBitmap =
            Bitmap.DecodeToWidth(AssetLoader.Open(new Uri("avares://Scribble/Assets/rotate.png")), 24);
        SelectionBorder.Cursor = new Cursor(moveIconBitmap, new PixelPoint(18, 18));
        SelectionRotationBtn.Cursor = new Cursor(rotateIconBitmap, new PixelPoint(12, 12));
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.RequestInvalidateSelection -= VisualizeSelection;
        }

        base.OnDataContextChanged(e);

        if (DataContext is MainViewModel viewModel)
        {
            _viewModel = viewModel;
            _viewModel.RequestInvalidateSelection += VisualizeSelection;

            Toolbar.Children.Clear();

            // Center the whiteboard
            (double canvasWidth, double canvasHeight) = viewModel.GetCanvasDimensions();
            CanvasScrollViewer.Offset = new Vector(canvasWidth / 2, canvasHeight / 2);

            // Register pointer tools
            RegisterPointerTool(new PencilTool("PencilTool", viewModel));
            RegisterPointerTool(new EraseTool("EraseTool", viewModel));
            RegisterPointerTool(new PanningTool("PanningTool", viewModel, CanvasScrollViewer));
            RegisterPointerTool(new LineTool("LineTool", viewModel));
            RegisterPointerTool(new ArrowTool("ArrowTool", viewModel));
            RegisterPointerTool(new EllipseTool("EllipseTool", viewModel));
            RegisterPointerTool(new RectangleTool("RectangleTool", viewModel));
            RegisterPointerTool(new TextTool("TextTool", viewModel, CanvasContainer));
            RegisterPointerTool(new SelectTool("SelectTool", viewModel, CanvasContainer));

            VisualizeSelection();
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
            if (toggleButton.IsChecked == true)
            {
                _activePointerTool?.Dispose();
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

    private void VisualizeSelection()
    {
        if (_viewModel == null) return;

        var allSelectedIds = _viewModel.SelectionTargets.Values.SelectMany(x => x).Distinct().ToList();

        if (allSelectedIds.Count > 0)
        {
            var selectedStrokes = _viewModel.CanvasStrokes
                .Where(stroke => allSelectedIds.Contains(stroke.Id) && stroke is DrawStroke)
                .Cast<DrawStroke>()
                .ToList();
            if (selectedStrokes.Count == 0)
            {
                SelectionOverlay.IsVisible = false;
                return;
            }

            SKRect combinedBounds = SKRect.Empty;

            foreach (var stroke in selectedStrokes)
            {
                SKRect strokeBounds = stroke.Path.Bounds;
                if (combinedBounds == SKRect.Empty)
                {
                    combinedBounds = strokeBounds;
                }
                else
                {
                    combinedBounds.Union(strokeBounds);
                }
            }

            Canvas.SetLeft(SelectionOverlay, combinedBounds.Left);
            Canvas.SetTop(SelectionOverlay, combinedBounds.Top - 15 - 6);

            SelectionBoxContainer.Width = combinedBounds.Width;
            SelectionBoxContainer.Height = combinedBounds.Height;
            SelectionOverlay.IsVisible = true;
            _selectionBounds = combinedBounds;
            bool isRotating = !double.IsNaN(_selectionRotationAngle);
            bool isScaling = _activeScaleHandle != null;
            if (isRotating || isScaling)
            {
                SelectionOverlay.IsVisible = false;
            }
        }
        else
        {
            SelectionOverlay.IsVisible = false;
            _selectionBounds = SKRect.Empty;
        }
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
        }
    }

    private void MainCanvas_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left)
        {
            var pointerCoordinates = GetPointerPosition(e);
            _activePointerTool?.HandlePointerRelease(_prevCoord, pointerCoordinates);
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

    private void SelectionBorder_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Properties.IsLeftButtonPressed)
        {
            _selectionMoveCoord = GetPointerPosition(e);
        }
    }

    private void SelectionBorder_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var pointerCoordinates = GetPointerPosition(e);
        var hasLastCoordinates = !_selectionMoveCoord.Equals(new Point(-1, -1));
        if (e.Properties.IsLeftButtonPressed && hasLastCoordinates && _viewModel != null)
        {
            // Move selected elements
            Point delta = pointerCoordinates - _selectionMoveCoord;
            foreach (var selection in _viewModel.SelectionTargets)
            {
                _viewModel.ApplyEvent(new MoveStrokesEvent(selection.Key, Utilities.ToSkPoint(delta)));
            }
        }

        _selectionMoveCoord = pointerCoordinates;
    }

    private void SelectionBorder_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && _viewModel != null)
        {
            foreach (var selection in _viewModel.SelectionTargets)
            {
                _viewModel.ApplyEvent(new EndStrokeEvent(selection.Key));
            }
        }

        _selectionMoveCoord = new Point(-1, -1);
    }

    private void SelectionRotationBtn_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Properties.IsLeftButtonPressed)
        {
            if (_selectionBounds.IsEmpty)
            {
                _selectionRotationAngle = double.NaN;
                return;
            }

            var pointerCoordinates = GetPointerPosition(e);
            _selectionCenter = new Point(_selectionBounds.Left + (_selectionBounds.Width / 2),
                _selectionBounds.Top + (_selectionBounds.Height / 2));
            _selectionRotationAngle = Math.Atan2(pointerCoordinates.Y - _selectionCenter.Y,
                pointerCoordinates.X - _selectionCenter.X);
        }
    }

    private void SelectionRotationBtn_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var pointerCoordinates = GetPointerPosition(e);
        var hasLastAngle = !double.IsNaN(_selectionRotationAngle);

        if (e.Properties.IsLeftButtonPressed && hasLastAngle && _viewModel != null && !_selectionBounds.IsEmpty)
        {
            var angleRad = Math.Atan2(pointerCoordinates.Y - _selectionCenter.Y,
                pointerCoordinates.X - _selectionCenter.X);
            var deltaRad = angleRad - _selectionRotationAngle;

            // Keep delta in [-pi, pi] to avoid jumps across the wrap boundary.
            if (deltaRad > Math.PI)
            {
                deltaRad -= Math.PI * 2;
            }
            else if (deltaRad < -Math.PI)
            {
                deltaRad += Math.PI * 2;
            }

            foreach (var selection in _viewModel.SelectionTargets)
            {
                _viewModel.ApplyEvent(new RotateStrokesEvent(selection.Key, (float)deltaRad,
                    Utilities.ToSkPoint(_selectionCenter)));
            }

            _selectionRotationAngle = angleRad;
        }
    }

    private void SelectionRotationBtn_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && _viewModel != null)
        {
            foreach (var selection in _viewModel.SelectionTargets)
            {
                _viewModel.ApplyEvent(new EndStrokeEvent(selection.Key));
            }
        }

        _selectionRotationAngle = double.NaN;
        _selectionCenter = new Point(-1, -1);
        VisualizeSelection();
    }

    private void ScaleHandle_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Properties.IsLeftButtonPressed && sender is Control control && _viewModel != null)
        {
            _activeScaleHandle = control.Name;
            _scalePrevCoord = GetPointerPosition(e);

            // Determine pivot based on the handle (opposite corner)
            // _selectionBounds contains the current bounds in Canvas coordinates
            switch (_activeScaleHandle)
            {
                case "ScaleHandleTl":
                    _scalePivot = new Point(_selectionBounds.Right, _selectionBounds.Bottom);
                    break;
                case "ScaleHandleTr":
                    _scalePivot = new Point(_selectionBounds.Left, _selectionBounds.Bottom);
                    break;
                case "ScaleHandleBl":
                    _scalePivot = new Point(_selectionBounds.Right, _selectionBounds.Top);
                    break;
                case "ScaleHandleBr":
                    _scalePivot = new Point(_selectionBounds.Left, _selectionBounds.Top);
                    break;
            }

            e.Handled = true;
        }
    }

    private void ScaleHandle_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Properties.IsLeftButtonPressed && _activeScaleHandle != null && _viewModel != null)
        {
            var currentCoord = GetPointerPosition(e);
            var prevVector = _scalePrevCoord - _scalePivot;
            var currVector = currentCoord - _scalePivot;

            // Avoid division by zero and extremely small scales that collapse geometry
            if (Math.Abs(prevVector.X) < 1 || Math.Abs(prevVector.Y) < 1 ||
                Math.Abs(currVector.X) < 1 || Math.Abs(currVector.Y) < 1)
            {
                return;
            }

            var scaleX = currVector.X / prevVector.X;
            var scaleY = currVector.Y / prevVector.Y;

            foreach (var selection in _viewModel.SelectionTargets)
            {
                _viewModel.ApplyEvent(new ScaleStrokesEvent(selection.Key, new SKPoint((float)scaleX, (float)scaleY),
                    Utilities.ToSkPoint(_scalePivot)));
            }

            _scalePrevCoord = currentCoord;
            e.Handled = true;
        }
    }

    private void ScaleHandle_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && _viewModel != null && _activeScaleHandle != null)
        {
            foreach (var selection in _viewModel.SelectionTargets)
            {
                _viewModel.ApplyEvent(new EndStrokeEvent(selection.Key));
            }

            _activeScaleHandle = null;
            _scalePivot = new Point(-1, -1);
            _scalePrevCoord = new Point(-1, -1);
            VisualizeSelection();
            e.Handled = true;
        }
    }

    private void MenuButton_OnClick(object? sender, RoutedEventArgs e)
    {
        MenuOptions.IsVisible = !MenuOptions.IsVisible;
        MenuOverlay.IsVisible = MenuOptions.IsVisible;
    }

    private void MenuOverlay_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        MenuOptions.IsVisible = false;
        MenuOverlay.IsVisible = false;
    }

    private async void SaveToFileMenuOption_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "Scribble",
            Title = "Save canvas state to file",
            DefaultExtension = ".scribble",
        });
        if (file is not null)
        {
            _viewModel?.SaveCanvasToFile(file);
        }
    }
}