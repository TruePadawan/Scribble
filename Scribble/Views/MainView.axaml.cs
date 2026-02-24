using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
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
    private PointerTool? _activePointerTool;
    private MainViewModel? _viewModel;
    private readonly Selection _selection;
    private readonly ToolOptionsValues _toolOptionsValues;
    private Action? _zoomed;

    public MainView()
    {
        InitializeComponent();
        _prevCoord = new Point(-1, -1);
        _selection = new Selection();
        _toolOptionsValues = new ToolOptionsValues();

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

        // Update the scale factor text and zoom button's enabled state when zoom happens
        _zoomed += UpdateScaleFactorText;
        _zoomed += UpdateZoomButtons;

        // Add hotkeys for zoom buttons
        ZoomInBtn.HotKey = new KeyGesture(Key.OemPlus, KeyModifiers.Control);
        ZoomOutBtn.HotKey = new KeyGesture(Key.OemMinus, KeyModifiers.Control);
    }

    private void RegisterPointerTool(PointerTool tool)
    {
        var toggleButton = new ToggleButton
        {
            Name = tool.Name,
        };
        ToggleButtonGroup.SetGroupName(toggleButton, "PointerTools");

        ToolTip.SetTip(toggleButton, tool.ToolTip);
        // Connect the tool's hotkey
        if (tool.HotKey != null)
        {
            toggleButton.HotKey = tool.HotKey;
        }


        toggleButton.IsCheckedChanged += (object? sender, RoutedEventArgs e) =>
        {
            if (toggleButton.IsChecked == true)
            {
                _activePointerTool?.Dispose();
                _activePointerTool = tool;

                // Render tool options
                if (_activePointerTool is StrokeTool strokeTool)
                {
                    ToolOptionsBorder.IsVisible = true;
                    ToolOptionsBorder.Opacity = 1;
                    RenderToolOptions(strokeTool);
                }
                else
                {
                    ToolOptionsBorder.IsVisible = false;
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

    private StackPanel CreateOptionControl(Control actualControl, string optionLabel)
    {
        var stackPanel = new StackPanel
        {
            Margin = new Thickness(8),
            Spacing = 2
        };
        stackPanel.Children.Add(new Label { Content = optionLabel });
        stackPanel.Children.Add(actualControl);
        return stackPanel;
    }

    private void RenderToolOptions(StrokeTool strokeTool)
    {
        ToolOptionsPanel.Children.Clear();

        foreach (var toolOption in strokeTool.ToolOptions)
        {
            switch (toolOption)
            {
                case ToolOption.StrokeThickness:
                    var thicknessSlider = ToolOptionsControlFactory.GetStrokeThicknessOption();
                    thicknessSlider.Value = _toolOptionsValues.StrokeThickness;
                    strokeTool.StrokePaint.StrokeWidth = _toolOptionsValues.StrokeThickness;

                    thicknessSlider.ValueChanged += (sender, args) =>
                    {
                        var newThickness = (float)args.NewValue;
                        _toolOptionsValues.StrokeThickness = newThickness;
                        strokeTool.StrokePaint.StrokeWidth = newThickness;
                    };
                    ToolOptionsPanel.Children.Add(CreateOptionControl(thicknessSlider, "Stroke Thickness"));
                    break;
                case ToolOption.StrokeColor:
                    var colorPicker = ToolOptionsControlFactory.GetStrokeColorOption();
                    colorPicker.Color = _toolOptionsValues.StrokeColor;
                    strokeTool.StrokePaint.Color = Utilities.ToSkColor(_toolOptionsValues.StrokeColor);

                    colorPicker.ColorChanged += (sender, args) =>
                    {
                        strokeTool.StrokePaint.Color = Utilities.ToSkColor(args.NewColor);
                        _toolOptionsValues.StrokeColor = args.NewColor;
                    };
                    ToolOptionsPanel.Children.Add(CreateOptionControl(colorPicker, "Stroke Color"));
                    break;
                case ToolOption.StrokeStyle:
                    var strokeStylePanel =
                        ToolOptionsControlFactory.GetStrokeStyleOption(_toolOptionsValues.StrokeStyle);
                    strokeTool.StrokePaint.DashIntervals = _toolOptionsValues.DashIntervals;

                    foreach (var child in strokeStylePanel.Children)
                    {
                        if (child is ToggleButton toggleButton)
                        {
                            toggleButton.IsCheckedChanged += (sender, args) =>
                            {
                                if (toggleButton.IsChecked == false) return;
                                switch (toggleButton.Name)
                                {
                                    case "Solid":
                                        strokeTool.StrokePaint.DashIntervals = null;
                                        _toolOptionsValues.DashIntervals = null;
                                        _toolOptionsValues.StrokeStyle = StrokeStyle.Solid;
                                        break;
                                    case "Dashed":
                                        strokeTool.StrokePaint.DashIntervals = [8f, 14f];
                                        _toolOptionsValues.DashIntervals = [8f, 14f];
                                        _toolOptionsValues.StrokeStyle = StrokeStyle.Dash;
                                        break;
                                    case "Dotted":
                                        strokeTool.StrokePaint.DashIntervals = [0f, 16f];
                                        _toolOptionsValues.DashIntervals = [0f, 16f];
                                        _toolOptionsValues.StrokeStyle = StrokeStyle.Dotted;
                                        break;
                                }
                            };
                        }
                    }

                    ToolOptionsPanel.Children.Add(CreateOptionControl(strokeStylePanel, "Stroke style"));
                    break;
                case ToolOption.FillColor:
                    var fillColorPanel = ToolOptionsControlFactory.GetFillColorOption();
                    strokeTool.StrokePaint.FillColor = Utilities.ToSkColor(_toolOptionsValues.FillColor);

                    var firstChild = fillColorPanel.Children[0];
                    var secondChild = fillColorPanel.Children[1];
                    if (firstChild is Button transparentBtn && secondChild is ColorPicker fillColorPicker)
                    {
                        fillColorPicker.ColorChanged += (sender, args) =>
                        {
                            var newColor = args.NewColor;
                            _toolOptionsValues.FillColor = newColor;
                            strokeTool.StrokePaint.FillColor = Utilities.ToSkColor(newColor);
                        };

                        var picker = fillColorPicker;
                        transparentBtn.Click += (sender, args) =>
                        {
                            strokeTool.StrokePaint.FillColor = SKColors.Transparent;
                            _toolOptionsValues.FillColor = Colors.Transparent;
                            picker.Color = Utilities.FromSkColor(SKColors.Transparent);
                        };
                    }

                    ToolOptionsPanel.Children.Add(CreateOptionControl(fillColorPanel, "Fill Color"));
                    break;
                case ToolOption.EdgeType:
                    var edgesPanel = ToolOptionsControlFactory.GetEdgesOption(_toolOptionsValues.EdgeType);
                    strokeTool.StrokePaint.StrokeJoin = _toolOptionsValues.EdgeType == EdgeType.Rounded
                        ? SKStrokeJoin.Round
                        : SKStrokeJoin.Miter;
                    foreach (var child in edgesPanel.Children)
                    {
                        if (child is ToggleButton toggleButton)
                        {
                            toggleButton.IsCheckedChanged += (sender, args) =>
                            {
                                if (toggleButton.IsChecked == false) return;
                                switch (toggleButton.Name)
                                {
                                    case "Sharp":
                                        _toolOptionsValues.EdgeType = EdgeType.Sharp;
                                        strokeTool.StrokePaint.StrokeJoin = SKStrokeJoin.Miter;
                                        break;
                                    case "Rounded":
                                        _toolOptionsValues.EdgeType = EdgeType.Rounded;
                                        strokeTool.StrokePaint.StrokeJoin = SKStrokeJoin.Round;
                                        break;
                                }
                            };
                        }
                    }

                    ToolOptionsPanel.Children.Add(CreateOptionControl(edgesPanel, "Edges"));
                    break;
                case ToolOption.FontSize:
                    var fontSizeSlider = ToolOptionsControlFactory.GetFontSizeOption();
                    strokeTool.StrokePaint.TextSize = _toolOptionsValues.FontSize;
                    fontSizeSlider.Value = _toolOptionsValues.FontSize;

                    fontSizeSlider.ValueChanged += (sender, args) =>
                    {
                        var newFontSize = (float)args.NewValue;
                        _toolOptionsValues.FontSize = newFontSize;
                        strokeTool.StrokePaint.TextSize = newFontSize;
                    };
                    ToolOptionsPanel.Children.Add(CreateOptionControl(fontSizeSlider, "Font Size"));
                    break;
            }
        }
    }

    private void RenderStrokeEditOptions(Dictionary<ToolOption, List<Guid>> categorizedStrokeIds)
    {
        if (_viewModel == null) return;

        ToolOptionsPanel.Children.Clear();

        foreach (var toolOption in categorizedStrokeIds.Keys)
        {
            var strokeIds = categorizedStrokeIds[toolOption];
            switch (toolOption)
            {
                case ToolOption.StrokeThickness:
                    var thicknessSlider = ToolOptionsControlFactory.GetStrokeThicknessOption();
                    thicknessSlider.Value = _toolOptionsValues.StrokeThickness;

                    thicknessSlider.ValueChanged += (sender, args) =>
                    {
                        var newThickness = (float)args.NewValue;
                        _toolOptionsValues.StrokeThickness = newThickness;
                        _viewModel.ApplyEvent(new UpdateStrokeThicknessEvent(Guid.NewGuid(), strokeIds, newThickness));
                    };
                    ToolOptionsPanel.Children.Add(CreateOptionControl(thicknessSlider, "Stroke Thickness"));
                    break;
                case ToolOption.StrokeColor:
                    var colorPicker = ToolOptionsControlFactory.GetStrokeColorOption();
                    colorPicker.Color = _toolOptionsValues.StrokeColor;

                    colorPicker.ColorChanged += (sender, args) =>
                    {
                        _toolOptionsValues.StrokeColor = args.NewColor;
                        _viewModel.ApplyEvent(new UpdateStrokeColorEvent(Guid.NewGuid(), strokeIds,
                            Utilities.ToSkColor(args.NewColor)));
                    };
                    ToolOptionsPanel.Children.Add(CreateOptionControl(colorPicker, "Stroke Color"));
                    break;
                case ToolOption.StrokeStyle:
                    var strokeStylePanel =
                        ToolOptionsControlFactory.GetStrokeStyleOption(_toolOptionsValues.StrokeStyle);

                    foreach (var child in strokeStylePanel.Children)
                    {
                        if (child is ToggleButton toggleButton)
                        {
                            toggleButton.IsCheckedChanged += (sender, args) =>
                            {
                                if (toggleButton.IsChecked == false) return;
                                float[]? newDashIntervals = null;
                                switch (toggleButton.Name)
                                {
                                    case "Solid":
                                        newDashIntervals = null;
                                        _toolOptionsValues.DashIntervals = null;
                                        _toolOptionsValues.StrokeStyle = StrokeStyle.Solid;
                                        break;
                                    case "Dashed":
                                        newDashIntervals = [8f, 14f];
                                        _toolOptionsValues.DashIntervals = [8f, 14f];
                                        _toolOptionsValues.StrokeStyle = StrokeStyle.Dash;
                                        break;
                                    case "Dotted":
                                        newDashIntervals = [0f, 16f];
                                        _toolOptionsValues.DashIntervals = [0f, 16f];
                                        _toolOptionsValues.StrokeStyle = StrokeStyle.Dotted;
                                        break;
                                }

                                _viewModel.ApplyEvent(new UpdateStrokeStyleEvent(Guid.NewGuid(), strokeIds,
                                    newDashIntervals));
                            };
                        }
                    }

                    ToolOptionsPanel.Children.Add(CreateOptionControl(strokeStylePanel, "Stroke style"));
                    break;
                case ToolOption.FillColor:
                    var fillColorPanel = ToolOptionsControlFactory.GetFillColorOption();

                    var firstChild = fillColorPanel.Children[0];
                    var secondChild = fillColorPanel.Children[1];
                    if (firstChild is Button transparentBtn && secondChild is ColorPicker fillColorPicker)
                    {
                        fillColorPicker.ColorChanged += (sender, args) =>
                        {
                            var newColor = args.NewColor;
                            _toolOptionsValues.FillColor = newColor;
                            _viewModel.ApplyEvent(new UpdateStrokeFillColorEvent(Guid.NewGuid(), strokeIds,
                                Utilities.ToSkColor(newColor)));
                        };

                        var picker = fillColorPicker;
                        transparentBtn.Click += (sender, args) =>
                        {
                            _toolOptionsValues.FillColor = Colors.Transparent;
                            picker.Color = Utilities.FromSkColor(SKColors.Transparent);
                            _viewModel.ApplyEvent(new UpdateStrokeFillColorEvent(Guid.NewGuid(), strokeIds,
                                SKColors.Transparent));
                        };
                    }

                    ToolOptionsPanel.Children.Add(CreateOptionControl(fillColorPanel, "Fill Color"));
                    break;
                case ToolOption.EdgeType:
                    var edgesPanel = ToolOptionsControlFactory.GetEdgesOption(_toolOptionsValues.EdgeType);
                    foreach (var child in edgesPanel.Children)
                    {
                        if (child is ToggleButton toggleButton)
                        {
                            toggleButton.IsCheckedChanged += (sender, args) =>
                            {
                                if (toggleButton.IsChecked == false) return;
                                SKStrokeJoin newStrokeJoin = SKStrokeJoin.Miter;
                                switch (toggleButton.Name)
                                {
                                    case "Sharp":
                                        _toolOptionsValues.EdgeType = EdgeType.Sharp;
                                        newStrokeJoin = SKStrokeJoin.Miter;
                                        break;
                                    case "Rounded":
                                        _toolOptionsValues.EdgeType = EdgeType.Rounded;
                                        newStrokeJoin = SKStrokeJoin.Round;
                                        break;
                                }

                                _viewModel.ApplyEvent(new UpdateStrokeEdgeTypeEvent(Guid.NewGuid(), strokeIds,
                                    newStrokeJoin));
                            };
                        }
                    }

                    ToolOptionsPanel.Children.Add(CreateOptionControl(edgesPanel, "Edges"));
                    break;
                case ToolOption.FontSize:
                    var fontSizeSlider = ToolOptionsControlFactory.GetFontSizeOption();
                    fontSizeSlider.Value = _toolOptionsValues.FontSize;

                    fontSizeSlider.ValueChanged += (sender, args) =>
                    {
                        var newFontSize = (float)args.NewValue;
                        _toolOptionsValues.FontSize = newFontSize;
                        _viewModel.ApplyEvent(new UpdateStrokeFontSizeEvent(Guid.NewGuid(), strokeIds, newFontSize));
                    };
                    ToolOptionsPanel.Children.Add(CreateOptionControl(fontSizeSlider, "Font Size"));
                    break;
            }
        }

        ToolOptionsBorder.IsVisible = true;
        ToolOptionsBorder.Opacity = 1;
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
                ToolOptionsBorder.IsVisible = false;
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

        RenderStrokeEditOptions(filteredStrokeIds);
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
        Zoom(zoomFactor, mousePosOnViewPort, mousePosOnCanvas);
        // Stop the scroll viewer from applying its own scrolling logic
        e.Handled = true;
    }

    private void Zoom(double zoomFactor, Point pointerViewPortPos, Point pointerCanvasPos)
    {
        if (_viewModel == null) throw new Exception("View Model not initialized");

        double currentScale = _viewModel.GetCurrentScale();
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
        var newOffset = (pointerCanvasPos * newScale) - pointerViewPortPos;
        CanvasScrollViewer.Offset = new Vector(newOffset.X, newOffset.Y);

        _zoomed?.Invoke();
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

    private void ZoomOutBtn_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        // Zoom out as if the pointer was in the middle of the viewport
        Point centerOnViewport = new Point(
            CanvasScrollViewer.Viewport.Width / 2,
            CanvasScrollViewer.Viewport.Height / 2
        );

        double currentScale = _viewModel.GetCurrentScale();
        Vector currentOffset = CanvasScrollViewer.Offset;

        Point centerOnCanvas = new Point(
            (currentOffset.X + centerOnViewport.X) / currentScale,
            (currentOffset.Y + centerOnViewport.Y) / currentScale
        );

        Zoom(0.9f, centerOnViewport, centerOnCanvas);
    }

    private void ZoomInBtn_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        // Zoom in as if the pointer was in the middle of the viewport
        Point centerOnViewport = new Point(
            CanvasScrollViewer.Viewport.Width / 2,
            CanvasScrollViewer.Viewport.Height / 2
        );

        double currentScale = _viewModel.GetCurrentScale();
        Vector currentOffset = CanvasScrollViewer.Offset;

        Point centerOnCanvas = new Point(
            (currentOffset.X + centerOnViewport.X) / currentScale,
            (currentOffset.Y + centerOnViewport.Y) / currentScale
        );

        Zoom(1.1f, centerOnViewport, centerOnCanvas);
    }

    private void UpdateScaleFactorText()
    {
        if (_viewModel == null) return;

        var scaleFactor = Math.Floor(_viewModel.GetCurrentScale() / MinZoom * 100);
        ScaleFactorText.Text = $"{scaleFactor}%";
    }

    private void UpdateZoomButtons()
    {
        if (_viewModel == null) return;

        var currentScale = _viewModel.GetCurrentScale();
        bool atMinZoom = Math.Abs(currentScale - MinZoom) < 0.0001f;
        bool atMaxZoom = Math.Abs(currentScale - MaxZoom) < 0.0001f;

        if (atMinZoom)
        {
            ZoomOutBtn.IsEnabled = false;
            ZoomOutBtn.Content = new Image
            {
                Source = new Bitmap(AssetLoader.Open(new Uri("avares://Scribble/Assets/minus-disabled.png"))),
                Width = 15,
                Height = 15,
                Margin = new Thickness(4)
            };
        }
        else
        {
            ZoomOutBtn.IsEnabled = true;
            ZoomOutBtn.Content = new Image
            {
                Source = new Bitmap(AssetLoader.Open(new Uri("avares://Scribble/Assets/minus.png"))),
                Width = 15,
                Height = 15,
                Margin = new Thickness(4)
            };
        }

        if (atMaxZoom)
        {
            ZoomInBtn.IsEnabled = false;
            ZoomInBtn.Content = new Image
            {
                Source = new Bitmap(AssetLoader.Open(new Uri("avares://Scribble/Assets/plus-disabled.png"))),
                Width = 15,
                Height = 15,
                Margin = new Thickness(4)
            };
        }
        else
        {
            ZoomInBtn.IsEnabled = true;
            ZoomInBtn.Content = new Image
            {
                Source = new Bitmap(AssetLoader.Open(new Uri("avares://Scribble/Assets/plus.png"))),
                Width = 15,
                Height = 15,
                Margin = new Thickness(4)
            };
        }
    }
}