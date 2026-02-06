using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.AspNetCore.SignalR.Client;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using Scribble.Behaviours;
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
    private const double MinZoom = 1f;
    private const double MaxZoom = 3f;
    private PointerToolsBase? _activePointerTool;
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
            _selection.SelectionBounds = combinedBounds;
            bool isRotating = !double.IsNaN(_selection.SelectionRotationAngle);
            bool isScaling = _selection.ActiveScaleHandle != null;
            if (isRotating || isScaling)
            {
                SelectionOverlay.IsVisible = false;
            }
        }
        else
        {
            SelectionOverlay.IsVisible = false;
            _selection.SelectionBounds = SKRect.Empty;
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

    private async void SaveToFileMenuOption_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

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

            CloseMenu();
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception.Message);
        }
    }

    private async void OpenFileMenuOption_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                SuggestedFileName = "Scribble",
                Title = "Restore canvas state from file",
                AllowMultiple = false,
            });
            if (files.Count == 1 && _viewModel != null)
            {
                await _viewModel.RestoreCanvasFromFile(files[0]);
            }

            CloseMenu();
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception.Message);
        }
    }

    private async void ExitOption_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_viewModel?.HasEvents() == true)
            {
                var box = MessageBoxManager
                    .GetMessageBoxStandard("Warning",
                        "All unsaved work will be lost. Are you sure you want to proceed?",
                        ButtonEnum.YesNo,
                        Icon.Warning);

                var result = await box.ShowAsync();
                if (result != ButtonResult.Yes) return;
            }

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception.Message);
        }
    }

    private void ResetCanvasMenuOption_OnClick(object? sender, RoutedEventArgs e)
    {
        _viewModel?.ResetCanvas();
    }

    private void CanvasBackgroundColorView_OnColorChanged(object? sender, ColorChangedEventArgs e)
    {
        _viewModel?.ChangeBackgroundColor(e.NewColor);
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

    private async void EnterRoomButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            EnterRoomButton.IsEnabled = false;
            if (_viewModel == null || RoomIdTextBox.Text == null) return;
            if (_viewModel.GetLiveDrawingServiceConnectionState() == HubConnectionState.Disconnected)
            {
                if (_viewModel.HasEvents())
                {
                    var box = MessageBoxManager
                        .GetMessageBoxStandard("Warning",
                            "This might clear your current canvas. Are you sure you want to proceed?",
                            ButtonEnum.YesNo,
                            Icon.Warning);

                    var result = await box.ShowAsync();
                    if (result != ButtonResult.Yes)
                    {
                        EnterRoomButton.IsEnabled = true;
                        return;
                    }
                }

                await _viewModel.JoinRoom(RoomIdTextBox.Text);
                EnterRoomButton.Content = "Leave Room";
                LiveDrawingButton.Background = new SolidColorBrush(Colors.Green);
            }
            else
            {
                await _viewModel.LeaveRoom();
                EnterRoomButton.Content = "Enter Room";
                LiveDrawingButton.Background = new SolidColorBrush(Colors.Transparent);
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }
        finally
        {
            EnterRoomButton.IsEnabled = true;
        }
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

    private void RoomIdGenerateButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.GetLiveDrawingServiceConnectionState() == HubConnectionState.Disconnected)
        {
            string roomId = Guid.NewGuid().ToString("N");
            RoomIdTextBox.Text = roomId;
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
}