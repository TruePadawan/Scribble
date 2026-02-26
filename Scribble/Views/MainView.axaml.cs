using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Scribble.Lib;
using Scribble.Shared.Lib;
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
    private PointerTool? _activePointerTool;
    private MainViewModel? _viewModel;
    private readonly Selection _selection;

    public MainView()
    {
        InitializeComponent();
        _prevCoord = new Point(-1, -1);
        _selection = new Selection();

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
            _viewModel.UiStateViewModel.CenterZoomRequested -= OnCenterZoomRequested;
            _viewModel.UiStateViewModel.ActiveToolChanged -= OnActiveToolChanged;
        }

        base.OnDataContextChanged(e);

        if (DataContext is MainViewModel viewModel)
        {
            _viewModel = viewModel;
            _viewModel.RequestInvalidateSelection += VisualizeSelection;
            _viewModel.UiStateViewModel.CenterZoomRequested += OnCenterZoomRequested;
            _viewModel.UiStateViewModel.ActiveToolChanged += OnActiveToolChanged;

            // Center the whiteboard
            (double canvasWidth, double canvasHeight) = viewModel.GetCanvasDimensions();
            CanvasScrollViewer.Offset = new Vector(canvasWidth / 2, canvasHeight / 2);

            viewModel.UiStateViewModel.AvailableTools.Clear();
            var tools = new List<PointerTool>
            {
                new PencilTool("PencilTool", viewModel),
                new EraseTool("EraseTool", viewModel),
                new PanningTool("PanningTool", viewModel, CanvasScrollViewer),
                new LineTool("LineTool", viewModel),
                new ArrowTool("ArrowTool", viewModel),
                new EllipseTool("EllipseTool", viewModel),
                new RectangleTool("RectangleTool", viewModel),
                new TextTool("TextTool", viewModel, CanvasContainer),
                new SelectTool("SelectTool", viewModel, CanvasContainer)
            };
            foreach (var tool in tools)
            {
                viewModel.UiStateViewModel.AvailableTools.Add(tool);

                // Map the tool's HotKey to trigger the SelectToolCommand
                if (tool.HotKey != null)
                {
                    KeyBindings.Add(new KeyBinding
                    {
                        Gesture = tool.HotKey,
                        Command = viewModel.UiStateViewModel.SwitchToolCommand,
                        CommandParameter = tool
                    });
                }
            }

            viewModel.UiStateViewModel.ActivePointerTool = tools.FirstOrDefault();
        }
    }

    private void OnActiveToolChanged(PointerTool? tool)
    {
        _activePointerTool = tool;
        if (tool == null) return;

        if (_activePointerTool is StrokeTool strokeTool)
        {
            _viewModel?.UiStateViewModel.BuildToolOptions(strokeTool);
        }
        else
        {
            _viewModel?.UiStateViewModel.ClearToolOptions();
        }

        if (tool.Cursor != null)
        {
            MainCanvas.Cursor = tool.Cursor;
        }
    }

    private void OnCenterZoomRequested(double zoomFactor)
    {
        if (_viewModel == null) return;

        // Zoom in as if the pointer was in the middle of the viewport
        Point centerOnViewport = new Point(
            CanvasScrollViewer.Viewport.Width / 2,
            CanvasScrollViewer.Viewport.Height / 2
        );

        double currentZoomLevel = _viewModel.UiStateViewModel.ZoomLevel;
        Vector currentOffset = CanvasScrollViewer.Offset;

        Point centerOnCanvas = new Point(
            (currentOffset.X + centerOnViewport.X) / currentZoomLevel,
            (currentOffset.Y + centerOnViewport.Y) / currentZoomLevel
        );

        PerformZoom(zoomFactor, centerOnViewport, centerOnCanvas);
    }

    private void PerformZoom(double zoomFactor, Point pointerViewPortPos, Point pointerCanvasPos)
    {
        if (_viewModel == null) return;

        double newScale = _viewModel.UiStateViewModel.ZoomLevel * zoomFactor;
        // Clamp new scale between min and max zoom
        newScale = Math.Max(UiStateViewModel.MinZoom, Math.Min(newScale, UiStateViewModel.MaxZoom));
        if (Math.Abs(newScale - _viewModel.UiStateViewModel.ZoomLevel) < 0.0001f) return;

        _viewModel.UiStateViewModel.ApplyZoom(newScale);

        // Needed to prevent weird zooming at the edge of the canvas
        CanvasScrollViewer.UpdateLayout();
        // Implement zoom to point
        var newOffset = (pointerCanvasPos * newScale) - pointerViewPortPos;
        CanvasScrollViewer.Offset = new Vector(newOffset.X, newOffset.Y);
    }



    private void VisualizeSelection()
    {
        if (_viewModel == null) return;

        var hasEvents = _viewModel.CanvasEvents.Count > 0;
        var triggeringSelectionAction = hasEvents && _viewModel.CanvasEvents.Last() is EndSelectionEvent;
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
            _selection.SelectionBounds = combinedBounds;
            bool isRotating = !double.IsNaN(_selection.SelectionRotationAngle);
            bool isScaling = _selection.ActiveScaleHandle != null;
            if (isRotating || isScaling)
            {
                SelectionOverlay.IsVisible = false;
            }

            if (triggeringSelectionAction)
            {
                ShowSelectedStrokesOptions(selectedStrokes);
            }
        }
        else
        {
            SelectionOverlay.IsVisible = false;
            _selection.SelectionBounds = SKRect.Empty;
            if (triggeringSelectionAction)
            {
                _viewModel.UiStateViewModel.ClearToolOptions();
            }
        }
    }

    private void ShowSelectedStrokesOptions(List<DrawStroke> selectedStrokes)
    {
        if (_viewModel == null) return;
        var filteredStrokeIds = new Dictionary<ToolOption, List<Guid>>();
        foreach (var selectedStroke in selectedStrokes)
        {
            var strokeOptions = selectedStroke.ToolOptions;
            foreach (var strokeOption in strokeOptions)
            {
                if (filteredStrokeIds.TryGetValue(strokeOption, out var strokeIds))
                {
                    strokeIds.Add(selectedStroke.Id);
                }
                else
                {
                    filteredStrokeIds[strokeOption] = [selectedStroke.Id];
                }
            }
        }

        _viewModel.UiStateViewModel.BuildSelectionEditOptions(filteredStrokeIds, e => _viewModel.ApplyEvent(e));
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
        if (_viewModel == null) throw new Exception("View Model not initialized");

        Point mousePosOnViewPort = e.GetPosition(CanvasScrollViewer);
        Point mousePosOnCanvas = e.GetPosition(MainCanvas);

        // Multiplicative Zoom
        double zoomFactor = e.Delta.Y > 0 ? 1.1f : 0.9f;
        PerformZoom(zoomFactor, mousePosOnViewPort, mousePosOnCanvas);

        // Stop the scroll viewer from applying its own scrolling logic
        e.Handled = true;
    }

    private void SelectionBorder_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Properties.IsLeftButtonPressed)
        {
            _selection.SelectionMoveCoord = GetPointerPosition(e);
            _selection.MoveActionId = Guid.NewGuid();
        }
    }

    private void SelectionBorder_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var pointerCoordinates = GetPointerPosition(e);
        var hasLastCoordinates = !_selection.SelectionMoveCoord.Equals(new Point(-1, -1));
        if (e.Properties.IsLeftButtonPressed && hasLastCoordinates && _viewModel != null)
        {
            // Move selected elements
            Point delta = pointerCoordinates - _selection.SelectionMoveCoord;
            foreach (var selection in _viewModel.SelectionTargets)
            {
                _viewModel.ApplyEvent(new MoveStrokesEvent(_selection.MoveActionId, selection.Key,
                    Utilities.ToSkPoint(delta)));
            }
        }

        _selection.SelectionMoveCoord = pointerCoordinates;
    }

    private void SelectionBorder_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && _viewModel != null)
        {
            foreach (var selection in _viewModel.SelectionTargets)
            {
                _viewModel.ApplyEvent(new EndStrokeEvent(_selection.MoveActionId));
            }
        }

        _selection.SelectionMoveCoord = new Point(-1, -1);
    }

    private void SelectionRotationBtn_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Properties.IsLeftButtonPressed)
        {
            if (_selection.SelectionBounds.IsEmpty)
            {
                _selection.SelectionRotationAngle = double.NaN;
                return;
            }

            var pointerCoordinates = GetPointerPosition(e);
            _selection.RefreshSelectionCenter();
            _selection.UpdateSelectionRotationAngle(pointerCoordinates);
            _selection.RotateActionId = Guid.NewGuid();
        }
    }

    private void SelectionRotationBtn_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var pointerCoordinates = GetPointerPosition(e);
        var hasLastAngle = !double.IsNaN(_selection.SelectionRotationAngle);

        if (e.Properties.IsLeftButtonPressed && hasLastAngle && _viewModel != null &&
            !_selection.SelectionBounds.IsEmpty)
        {
            var angleRad = Math.Atan2(pointerCoordinates.Y - _selection.SelectionCenter.Y,
                pointerCoordinates.X - _selection.SelectionCenter.X);
            var deltaRad = angleRad - _selection.SelectionRotationAngle;

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
                _viewModel.ApplyEvent(new RotateStrokesEvent(_selection.RotateActionId, selection.Key, (float)deltaRad,
                    Utilities.ToSkPoint(_selection.SelectionCenter)));
            }

            _selection.SelectionRotationAngle = angleRad;
        }
    }

    private void SelectionRotationBtn_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && _viewModel != null)
        {
            foreach (var selection in _viewModel.SelectionTargets)
            {
                _viewModel.ApplyEvent(new EndStrokeEvent(_selection.RotateActionId));
            }
        }

        _selection.SelectionRotationAngle = double.NaN;
        _selection.SelectionCenter = new Point(-1, -1);
        VisualizeSelection();
    }

    private void ScaleHandle_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Properties.IsLeftButtonPressed && sender is Control control && _viewModel != null)
        {
            _selection.ActiveScaleHandle = control.Name;
            _selection.ScalePrevCoord = GetPointerPosition(e);
            _selection.ScaleActionId = Guid.NewGuid();
            _selection.RefreshScalePivot();

            e.Handled = true;
        }
    }

    private void ScaleHandle_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Properties.IsLeftButtonPressed && _selection.ActiveScaleHandle != null && _viewModel != null)
        {
            var currentCoord = GetPointerPosition(e);
            var prevVector = _selection.ScalePrevCoord - _selection.ScalePivot;
            var currVector = currentCoord - _selection.ScalePivot;

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
                _viewModel.ApplyEvent(new ScaleStrokesEvent(_selection.ScaleActionId,
                    selection.Key,
                    new SKPoint((float)scaleX, (float)scaleY),
                    Utilities.ToSkPoint(_selection.ScalePivot)));
            }

            _selection.ScalePrevCoord = currentCoord;
            e.Handled = true;
        }
    }

    private void ScaleHandle_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && _viewModel != null && _selection.ActiveScaleHandle != null)
        {
            foreach (var selection in _viewModel.SelectionTargets)
            {
                _viewModel.ApplyEvent(new EndStrokeEvent(_selection.ScaleActionId));
            }

            _selection.ActiveScaleHandle = null;
            _selection.ScalePivot = new Point(-1, -1);
            _selection.ScalePrevCoord = new Point(-1, -1);
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
        CloseMenu();
    }

    private void CloseMenu()
    {
        MenuOptions.IsVisible = false;
        MenuOverlay.IsVisible = false;

        Dispatcher.UIThread.Post(() => CanvasContainer.Focus());
    }

    private void CloseLiveDrawingWindow()
    {
        LiveDrawingWindow.IsVisible = false;
        LiveDrawingWindowOverlay.IsVisible = false;
        Dispatcher.UIThread.Post(() => CanvasContainer.Focus());
    }

    private void CloseAboutScribbleWindow()
    {
        AboutScribbleWindow.IsVisible = false;
        AboutScribbleWindowOverlay.IsVisible = false;
        Dispatcher.UIThread.Post(() => CanvasContainer.Focus());
    }

    private void CanvasBackgroundColorView_OnColorChanged(object? sender, ColorChangedEventArgs e)
    {
        _viewModel?.UiStateViewModel.ChangeBackgroundColor(e.NewColor);
    }

    private void TransparentCanvasButton_OnClick(object? sender, RoutedEventArgs e)
    {
        CanvasBackgroundColorView.Color = Colors.Transparent;
    }

    private void LiveDrawingWindowOverlay_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        CloseLiveDrawingWindow();
    }

    private void LiveDrawingButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        LiveDrawingWindow.IsVisible = true;
        LiveDrawingWindowOverlay.IsVisible = true;
    }

    private async void RoomIdClipboardButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;
            await clipboard.SetTextAsync(RoomIdTextBox.Text);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Failed to copy to clipboard - {exception.Message}");
        }
    }

    private void RoomIdTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Don't allow whitespace
        if (e.Key == Key.Space)
        {
            e.Handled = true;
        }
    }

    private void RoomIdTextBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.Text != null)
        {
            if (textBox.Text.Any(char.IsWhiteSpace))
            {
                var caretIndex = textBox.CaretIndex;
                var cleanText = textBox.Text.Replace(" ", "");
                textBox.Text = cleanText;

                // Restore the cursor position
                textBox.CaretIndex = Math.Min(caretIndex, textBox.Text.Length);
            }
        }
    }

    private void AboutScribbleOverlay_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        CloseAboutScribbleWindow();
    }

    private void AboutOption_OnClick(object? sender, RoutedEventArgs e)
    {
        CloseMenu();
        AboutScribbleWindow.IsVisible = true;
        AboutScribbleWindowOverlay.IsVisible = true;
    }
}