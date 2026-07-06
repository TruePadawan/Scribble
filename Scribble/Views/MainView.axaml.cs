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
using Avalonia.Skia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Scribble.Services.CanvasStateService;
using Scribble.Services.DialogService;
using Scribble.Services.FileService;
using Scribble.Services.MultiUserDrawing;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using Scribble.State;
using Scribble.Tools.PointerTools;
using Scribble.Tools.PointerTools.ArrowTool;
using Scribble.Tools.PointerTools.EllipseTool;
using Scribble.Tools.PointerTools.EraseTool;
using Scribble.Tools.PointerTools.ImageTool;
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
    private SKPoint _prevCoord;
    private SKPoint _lastWorldPointerPos;
    private PointerTool? _activePointerTool;
    private MainViewModel? _viewModel;
    private readonly ICanvasStateService _canvasStateService;
    private readonly IMultiUserDrawingService _multiUserDrawingService;
    private readonly IDialogService _dialogService;
    private readonly IFileService _fileService;
    private readonly Selection _selection;

    public MainView()
    {
        InitializeComponent();
        _prevCoord = SKPoint.Empty;
        _lastWorldPointerPos = SKPoint.Empty;
        _selection = new Selection();

        var moveIconBitmap = Bitmap.DecodeToWidth(AssetLoader.Open(new Uri("avares://Scribble/Assets/move.png")), 36);
        var rotateIconBitmap =
            Bitmap.DecodeToWidth(AssetLoader.Open(new Uri("avares://Scribble/Assets/rotate.png")), 24);
        SelectionBorder.Cursor = new Cursor(moveIconBitmap, new PixelPoint(18, 18));
        SelectionRotationBtn.Cursor = new Cursor(rotateIconBitmap, new PixelPoint(12, 12));

        var services = ((App)Application.Current!).Services;
        _canvasStateService = services.GetRequiredService<ICanvasStateService>();
        _dialogService = services.GetRequiredService<IDialogService>();
        _fileService = services.GetRequiredService<IFileService>();
        _multiUserDrawingService = services.GetRequiredService<IMultiUserDrawingService>();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.RequestRefreshSelection -= VisualizeSelection;
            _viewModel.RequestInvalidateSkiaCanvas -= MainCanvas.InvalidateVisual;
            _viewModel.RequestInvalidateSkiaCanvas -= MarkTextStrokesForEditing;
            _viewModel.UiStateViewModel.CenterZoomRequested -= OnCenterZoomRequested;
            _viewModel.UiStateViewModel.ActiveToolChanged -= OnActiveToolChanged;
        }

        base.OnDataContextChanged(e);

        if (DataContext is MainViewModel viewModel)
        {
            _viewModel = viewModel;
            _viewModel.RequestRefreshSelection += VisualizeSelection;
            _viewModel.RequestInvalidateSkiaCanvas += MainCanvas.InvalidateVisual;
            _viewModel.RequestInvalidateSkiaCanvas += MarkTextStrokesForEditing;
            _viewModel.UiStateViewModel.CenterZoomRequested += OnCenterZoomRequested;
            _viewModel.UiStateViewModel.ActiveToolChanged += OnActiveToolChanged;

            OnAppStartUp(viewModel);
        }
    }

    private bool CanExecuteKeyBinding()
    {
        var focusedElement = Utilities.GetTopLevel()?.FocusManager?.GetFocusedElement();
        return focusedElement is not TextBox;
    }

    private void OnAppStartUp(MainViewModel viewModel)
    {
        LoadAllTools(viewModel);
        // Ensure the canvas container gets focus so keybindings work immediately
        Dispatcher.UIThread.Post(() => CanvasContainer.Focus());

        // Add keybindings for click event handlers
        RootPanel.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.D1, KeyModifiers.Shift),
            Command = new RelayCommand(() => ZoomToFitBtn_OnClick(null, null), CanExecuteKeyBinding)
        });

        RootPanel.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.E, KeyModifiers.Control | KeyModifiers.Shift),
            Command = new RelayCommand(() => ExportMenuOption_OnClick(null, null), CanExecuteKeyBinding)
        });

        RootPanel.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.C, KeyModifiers.Control),
            Command = new RelayCommand(() => viewModel.CopyCommand.Execute(null), CanExecuteKeyBinding)
        });

        RootPanel.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.V, KeyModifiers.Control),
            Command = new RelayCommand(() =>
            {
                var pastePos = _lastWorldPointerPos;
                if (pastePos.IsEmpty)
                {
                    var viewportCenter = new SKPoint(
                        (float)(MainCanvas.Bounds.Width / 2),
                        (float)(MainCanvas.Bounds.Height / 2)
                    );
                    pastePos = CameraState.ScreenToWorld(viewportCenter);
                }
                viewModel.PasteCommand.Execute(pastePos);
            }, CanExecuteKeyBinding)
        });
    }

    private void LoadAllTools(MainViewModel viewModel)
    {
        viewModel.UiStateViewModel.AvailableTools.Clear();
        var tools = new List<PointerTool>
        {
            new PencilTool("PencilTool", _canvasStateService),
            new EraseTool("EraseTool", _canvasStateService),
            new PanningTool("PanningTool", _canvasStateService, MainCanvas.InvalidateVisual),
            new LineTool("LineTool", _canvasStateService),
            new ArrowTool("ArrowTool", _canvasStateService),
            new EllipseTool("EllipseTool", _canvasStateService),
            new RectangleTool("RectangleTool", _canvasStateService),
            new TextTool("TextTool", _canvasStateService, CanvasContainer),
            new SelectTool("SelectTool", _canvasStateService, CanvasContainer),
            new ImageTool("ImageTool", _canvasStateService, _fileService, _dialogService),
        };
        foreach (var tool in tools)
        {
            viewModel.UiStateViewModel.AvailableTools.Add(tool);

            // Map the tool's HotKey to trigger the SwitchToolCommand
            if (tool.HotKey != null)
            {
                KeyBindings.Add(new KeyBinding
                {
                    Gesture = tool.HotKey,
                    Command = new RelayCommand(() => viewModel.UiStateViewModel.SwitchToolCommand.Execute(tool),
                        CanExecuteKeyBinding)
                });
            }
        }

        viewModel.UiStateViewModel.ActivePointerTool = tools.FirstOrDefault();
    }

    /// <summary>
    /// Identify all text strokes and put an invisible Border control on them that triggers
    /// a pop-up for editing the text
    /// </summary>
    private void MarkTextStrokesForEditing()
    {
        TextStrokeEditBorders.Children.Clear();

        // Don't show edit border when there is an active selection so they don't conflict
        if (_canvasStateService.SelectedElementIds.Count > 0) return;

        var textStrokes = _canvasStateService.CanvasElements
            .Where(canvasEl => canvasEl is TextStroke)
            // Only allow editing for text strokes created by the current user
            .Where(canvasEl => canvasEl.CreatorConnectionId == null ||
                               canvasEl.CreatorConnectionId == _multiUserDrawingService.Room?.Me.ConnectionId)
            .Cast<TextStroke>()
            .ToList();
        var borders = new List<Border>();
        foreach (var textStroke in textStrokes)
        {
            var strokeBounds = textStroke.Path.Bounds;

            // Convert world-space bounds to screen-space for overlay positioning
            var topLeftScreen = CameraState.WorldToScreen(new SKPoint(strokeBounds.Left, strokeBounds.Top));
            var bottomRightScreen =
                CameraState.WorldToScreen(new SKPoint(strokeBounds.Right, strokeBounds.Bottom));

            var border = new Border
            {
                Background = Brushes.Transparent,
                Width = bottomRightScreen.X - topLeftScreen.X,
                Height = bottomRightScreen.Y - topLeftScreen.Y,
                Cursor = new Cursor(StandardCursorType.Ibeam),
                Tag = textStroke
            };

            border.PointerPressed += TextStrokeBorder_OnPointerPressed;

            Canvas.SetLeft(border, topLeftScreen.X);
            Canvas.SetTop(border, topLeftScreen.Y);
            borders.Add(border);
        }

        TextStrokeEditBorders.Children.AddRange(borders);
    }

    private void TextStrokeBorder_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Properties.IsLeftButtonPressed && sender is Border { Tag: TextStroke textStroke })
        {
            var textTool = _viewModel?.UiStateViewModel.AvailableTools.OfType<TextTool>().FirstOrDefault();
            textTool?.StartEditing(textStroke);
        }
    }

    // If the active tool is a stroke tool, show its options else clear and hide the tool options UI
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
        var viewportCenter = new SKPoint(
            (float)(MainCanvas.Bounds.Width / 2),
            (float)(MainCanvas.Bounds.Height / 2)
        );

        PerformZoom(zoomFactor, viewportCenter);
    }

    /// <summary>
    /// Zoom-to-point: adjusts CameraState.Zoom while keeping the world point
    /// under the pointer fixed on screen.
    /// </summary>
    private void PerformZoom(double zoomFactor, SKPoint screenPivot)
    {
        if (_viewModel == null) return;

        var worldPosBeforeZoom = CameraState.ScreenToWorld(screenPivot);
        var oldZoom = CameraState.Zoom;
        var newZoom = (float)(oldZoom * zoomFactor);
        CameraState.SetZoom(newZoom);
        newZoom = CameraState.Zoom; // re-read in case it was clamped

        if (Math.Abs(newZoom - oldZoom) < 0.0001f) return;

        var worldPosAfterZoom = CameraState.ScreenToWorld(screenPivot);

        // Adjust the world offset so the world point under the pointer stays fixed.
        CameraState.WorldOffSetX -= worldPosAfterZoom.X - worldPosBeforeZoom.X;
        CameraState.WorldOffSetY -= worldPosAfterZoom.Y - worldPosBeforeZoom.Y;

        MainCanvas.InvalidateVisual();

        _viewModel.UiStateViewModel.UpdateZoomLevel(newZoom);
        // Re-visualize selection if there was any before zooming, this is needed to correct the size of the selection
        // border post-zoom
        VisualizeSelection();
        // Re-apply text edit overlay so it stays in sync with the zoom level
        MarkTextStrokesForEditing();
    }

    private void ZoomToFitBtn_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null || _viewModel.CanvasElements.Count == 0) return;

        SKRect globalBounds = SKRect.Empty;

        foreach (var element in _viewModel.CanvasElements)
        {
            SKRect elementBounds = element switch
            {
                PaintableStroke stroke => stroke.Path.Bounds,
                CanvasImage image => image.Bounds,
                _ => SKRect.Empty
            };

            if (globalBounds == SKRect.Empty)
            {
                globalBounds = elementBounds;
            }
            else if (!elementBounds.IsEmpty)
            {
                globalBounds.Union(elementBounds);
            }
        }

        if (globalBounds.IsEmpty) return;

        // Viewport dimensions (screen space)
        float viewportWidth = (float)MainCanvas.Bounds.Width;
        float viewportHeight = (float)MainCanvas.Bounds.Height;

        // Calculate the scale required to fit width and height, then take the smaller one
        float scaleX = viewportWidth / globalBounds.Width;
        float scaleY = viewportHeight / globalBounds.Height;
        float targetZoom = Math.Min(scaleX, scaleY);

        // Add 10% padding so it looks nice
        targetZoom *= 0.9f;

        CameraState.SetZoom(targetZoom);
        float finalZoom = CameraState.Zoom;

        // Center the viewport
        CameraState.WorldOffSetX = globalBounds.MidX - (viewportWidth / 2f) / finalZoom;
        CameraState.WorldOffSetY = globalBounds.MidY - (viewportHeight / 2f) / finalZoom;

        _viewModel.UiStateViewModel.UpdateZoomLevel(finalZoom);
        MainCanvas.InvalidateVisual();

        VisualizeSelection();
    }

    private void VisualizeSelection()
    {
        if (_viewModel == null) return;

        var hasEvents = _canvasStateService.CanvasEvents.Count > 0;
        // Am I triggering a selection?
        var triggeringSelectionAction = hasEvents &&
                                        _canvasStateService.CanvasEvents.Last() is EndSelectionEvent es &&
                                        _canvasStateService.IsLocalSelection(es.BoundId);
        var allSelectedIds = _canvasStateService.SelectedElementIds;

        if (allSelectedIds.Count > 0)
        {
            var selectedStrokes = _viewModel.CanvasElements
                .Where(element => allSelectedIds.Contains(element.Id) && element is PaintableStroke)
                .Cast<PaintableStroke>()
                .ToList();
            var selectedImages = _viewModel.CanvasElements
                .Where(element => allSelectedIds.Contains(element.Id) && element is CanvasImage)
                .Cast<CanvasImage>()
                .ToList();
            if (selectedStrokes.Count == 0 && selectedImages.Count == 0)
            {
                SelectionOverlay.IsVisible = false;
                return;
            }

            SKRect combinedBounds = SKRect.Empty;

            foreach (var strokeBounds in selectedStrokes.Select(stroke => stroke.Path.Bounds))
            {
                if (combinedBounds == SKRect.Empty)
                {
                    combinedBounds = strokeBounds;
                }
                else
                {
                    combinedBounds.Union(strokeBounds);
                }
            }

            foreach (var imageBounds in selectedImages.Select(canvasImage => canvasImage.Bounds))
            {
                if (combinedBounds == SKRect.Empty)
                {
                    combinedBounds = imageBounds;
                }
                else
                {
                    combinedBounds.Union(imageBounds);
                }
            }

            // Align the selection overlay with what it has selected
            // Convert the world-space bounding rect corners to screen-space
            var topLeftScreen = CameraState.WorldToScreen(new SKPoint(combinedBounds.Left, combinedBounds.Top));
            var bottomRightScreen = CameraState.WorldToScreen(new SKPoint(combinedBounds.Right, combinedBounds.Bottom));
            var screenWidth = bottomRightScreen.X - topLeftScreen.X;
            var screenHeight = bottomRightScreen.Y - topLeftScreen.Y;

            Canvas.SetLeft(SelectionOverlay, topLeftScreen.X);
            Canvas.SetTop(SelectionOverlay, topLeftScreen.Y - 15 - 6); // rotation handle offset
            SelectionBoxContainer.Width = screenWidth;
            SelectionBoxContainer.Height = screenHeight;

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
                _viewModel.UiStateViewModel.ShowSelectedCanvasElementOptions([..selectedStrokes, ..selectedImages]);
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

    private SKPoint GetPointerPosition(PointerEventArgs e)
    {
        // For panning, we need screen-space coordinates (raw viewport pixels)
        // so the panning delta can be correctly converted to world-space offset changes
        if (_activePointerTool is PanningTool)
        {
            return e.GetPosition(MainCanvas).ToSKPoint();
        }

        // For all other tools, convert screen pixels to world coordinates
        var screenPos = Utilities.ToSkPoint(e.GetPosition(MainCanvas));
        return CameraState.ScreenToWorld(screenPos);
    }

    private void CloseExportImageWindow()
    {
        ExportImageWindow.IsVisible = false;
        ExportImageWindowOverlay.IsVisible = false;

        Dispatcher.UIThread.Post(() => CanvasContainer.Focus());
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

    private void MainCanvas_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var screenPos = Utilities.ToSkPoint(e.GetPosition(MainCanvas));
        _lastWorldPointerPos = CameraState.ScreenToWorld(screenPos);

        var pointerCoordinates = GetPointerPosition(e);

        // Skip if position hasn't changed (tablet pens fire PointerMoved
        // due to pressure/tilt jitter even when stationary)
        if (Utilities.AreSamePosition(pointerCoordinates, _prevCoord) || _prevCoord.IsEmpty) return;

        var hasLastCoordinates = !_prevCoord.IsEmpty;

        if (e.Properties.IsLeftButtonPressed && hasLastCoordinates)
        {
            _activePointerTool?.HandlePointerMove(_prevCoord, pointerCoordinates);
        }

        _prevCoord = pointerCoordinates;

        // Re-apply text edit overlay if canvas is being panned around
        if (_activePointerTool is PanningTool)
        {
            MarkTextStrokesForEditing();
        }
    }

    private void MainCanvas_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var screenPos = Utilities.ToSkPoint(e.GetPosition(MainCanvas));
        _lastWorldPointerPos = CameraState.ScreenToWorld(screenPos);

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
        _prevCoord = SKPoint.Empty;
    }

    private void MainCanvas_OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        bool ctrlKeyIsPressed = (e.KeyModifiers & KeyModifiers.Control) != 0;
        bool shiftKeyIsPressed = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        if (_viewModel == null) throw new Exception("View Model not initialized");

        var screenPos = Utilities.ToSkPoint(e.GetPosition(MainCanvas));

        if (ctrlKeyIsPressed)
        {
            double zoomFactor = e.Delta.Y > 0 ? 1.1f : 0.9f;
            PerformZoom(zoomFactor, screenPos);
        }
        else
        {
            // Pan: trackpads natively provide both Delta.X and Delta.Y for omnidirectional panning.
            // Mouse wheels only provide Delta.Y, so use Shift+Wheel for horizontal panning.
            const float panStep = 50f;

            float deltaX = (float)e.Delta.X;
            float deltaY = (float)e.Delta.Y;

            // If Shift is pressed and the pointing device only sent vertical scroll, convert it to horizontal.
            if (shiftKeyIsPressed && Math.Abs(deltaX) < 0.0001f)
            {
                deltaX = deltaY;
                deltaY = 0;
            }

            CameraState.WorldOffSetX -= (deltaX * panStep) / CameraState.Zoom;
            CameraState.WorldOffSetY -= (deltaY * panStep) / CameraState.Zoom;

            MainCanvas.InvalidateVisual();
        }

        VisualizeSelection();
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

        // Skip if position hasn't changed (tablet pen jitter)
        if (Utilities.AreSamePosition(pointerCoordinates, _selection.SelectionMoveCoord)) return;

        var hasLastCoordinates = !_selection.SelectionMoveCoord.IsEmpty;
        if (e.Properties.IsLeftButtonPressed && hasLastCoordinates && _viewModel != null)
        {
            // Move selected elements
            var delta = pointerCoordinates - _selection.SelectionMoveCoord;
            if (_canvasStateService.ActiveSelectionBoundId is { } moveBoundId)
            {
                _canvasStateService.ApplyEvent(new MoveCanvasElementsEvent(_selection.MoveActionId, moveBoundId,
                    delta));
            }
        }

        _selection.SelectionMoveCoord = pointerCoordinates;
    }

    private void SelectionBorder_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && _viewModel != null)
        {
            if (_canvasStateService.ActiveSelectionBoundId != null)
            {
                _canvasStateService.ApplyEvent(new EndStrokeEvent(_selection.MoveActionId));
            }
        }

        _selection.SelectionMoveCoord = SKPoint.Empty;
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

            // Skip if angle hasn't changed (tablet pen jitter)
            if (Math.Abs(deltaRad) < double.Epsilon) return;

            // Keep delta in [-pi, pi] to avoid jumps across the wrap boundary.
            if (deltaRad > Math.PI)
            {
                deltaRad -= Math.PI * 2;
            }
            else if (deltaRad < -Math.PI)
            {
                deltaRad += Math.PI * 2;
            }

            if (_canvasStateService.ActiveSelectionBoundId is { } rotateBoundId)
            {
                _canvasStateService.ApplyEvent(new RotateCanvasElementsEvent(_selection.RotateActionId, rotateBoundId,
                    (float)deltaRad, _selection.SelectionCenter));
            }

            _selection.SelectionRotationAngle = angleRad;
        }
    }

    private void SelectionRotationBtn_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && _viewModel != null)
        {
            if (_canvasStateService.ActiveSelectionBoundId != null)
            {
                _canvasStateService.ApplyEvent(new EndStrokeEvent(_selection.RotateActionId));
            }
        }

        _selection.SelectionRotationAngle = double.NaN;
        _selection.SelectionCenter = SKPoint.Empty;
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

            // Skip if position hasn't changed (tablet pen jitter)
            if (Utilities.AreSamePosition(currentCoord, _selection.ScalePrevCoord)) return;

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

            if (_canvasStateService.ActiveSelectionBoundId is { } scaleBoundId)
            {
                _canvasStateService.ApplyEvent(new ScaleCanvasElementsEvent(_selection.ScaleActionId,
                    scaleBoundId,
                    new SKPoint(scaleX, scaleY),
                    _selection.ScalePivot));
            }

            _selection.ScalePrevCoord = currentCoord;
            e.Handled = true;
        }
    }

    private void ScaleHandle_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && _viewModel != null && _selection.ActiveScaleHandle != null)
        {
            if (_canvasStateService.ActiveSelectionBoundId != null)
            {
                _canvasStateService.ApplyEvent(new EndStrokeEvent(_selection.ScaleActionId));
            }

            _selection.ActiveScaleHandle = null;
            _selection.ScalePivot = SKPoint.Empty;
            _selection.ScalePrevCoord = SKPoint.Empty;
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

    private void TransparentCanvasButton_OnClick(object? sender, RoutedEventArgs e)
    {
        CanvasBackgroundColorPicker.SelectedColor = Color.Parse("#a2000000");
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

    private void RoomIdClipboardButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var clipboard = Utilities.GetTopLevel()?.Clipboard;
        clipboard?.SetTextAsync(RoomIdTextBox.Text);
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

    private void ExportImageWindowOverlay_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        CloseExportImageWindow();
    }

    private void ExportMenuOption_OnClick(object? sender, RoutedEventArgs e)
    {
        CloseMenu();
        _viewModel?.CanvasExportViewModel.UpdateCanvasPreview();
        ExportImageWindow.IsVisible = true;
        ExportImageWindowOverlay.IsVisible = true;
    }

    private void MessageTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        var enterKeyIsPressed = e.Key == Key.Enter && (e.KeyModifiers & KeyModifiers.Shift) == 0;
        if (!enterKeyIsPressed) return;

        if (_viewModel != null && _viewModel.MultiUserDrawingViewModel.SendMessageCommand.CanExecute(null))
        {
            _viewModel.MultiUserDrawingViewModel.SendMessageCommand.Execute(null);
        }

        e.Handled = true;
    }
}