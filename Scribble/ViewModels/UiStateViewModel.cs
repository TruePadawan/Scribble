using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Scribble.Services;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using Scribble.State;
using Scribble.Tools.PointerTools;
using Scribble.Utils;
using Scribble.ViewModels.ToolOptions;
using SkiaSharp;

namespace Scribble.ViewModels;

/// <summary>
/// View model for managing the UI state of the application
/// Handles zooming, tool switching, and tool options
/// </summary>
public partial class UiStateViewModel : ViewModelBase
{
    public const double MinZoom = 1.0f;
    public const double MaxZoom = 3.0f;
    public ScaleTransform ScaleTransform { get; } = new ScaleTransform(1, 1);

    private bool CanZoomIn => ZoomLevel < MaxZoom;
    private bool CanZoomOut => ZoomLevel > MinZoom;

    public event Action<PointerTool?>? ActiveToolChanged;
    public event Action<double>? CenterZoomRequested;

    [ObservableProperty] private Color _backgroundColor;
    [ObservableProperty] private PointerTool? _activePointerTool;
    [ObservableProperty] private bool _toolOptionsVisible;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ScaleFactorText))]
    private double _zoomLevel = 1.0f;

    public string ScaleFactorText => $"{Math.Floor(ZoomLevel / MinZoom * 100)}%";
    public ObservableCollection<PointerTool> AvailableTools { get; } = [];
    public ObservableCollection<ToolOptionViewModelBase> ActiveToolOptions { get; } = [];

    private readonly CanvasStateService _canvasStateService;
    private readonly ToolOptionsValues _toolOptionsValues = new();

    public UiStateViewModel(CanvasStateService canvasStateService)
    {
        _canvasStateService = canvasStateService;
        _backgroundColor = Utilities.FromSkColor(canvasStateService.BackgroundColor);

        canvasStateService.BackgroundColorChanged += () =>
        {
            BackgroundColor = Utilities.FromSkColor(_canvasStateService.BackgroundColor);
        };
    }

    partial void OnBackgroundColorChanged(Color value)
    {
        _canvasStateService.SetBackgroundColor(Utilities.ToSkColor(value));
    }

    [RelayCommand(CanExecute = nameof(CanZoomIn))]
    private void ZoomIn() => CenterZoomRequested?.Invoke(1.1);

    [RelayCommand(CanExecute = nameof(CanZoomOut))]
    private void ZoomOut() => CenterZoomRequested?.Invoke(0.9);

    public void ApplyZoom(double newScale)
    {
        // Clamp the zoom level between the min and max zoom
        ZoomLevel = Math.Max(MinZoom, Math.Min(MaxZoom, newScale));

        ScaleTransform.ScaleX = ZoomLevel;
        ScaleTransform.ScaleY = ZoomLevel;

        // Tell the UI that it should refresh controls that are bound to CanZoomIn and CanZoomOut
        ZoomInCommand.NotifyCanExecuteChanged();
        ZoomOutCommand.NotifyCanExecuteChanged();
    }

    partial void OnActivePointerToolChanged(PointerTool? oldValue, PointerTool? newValue)
    {
        oldValue?.HandleToolSwitchOut();
        ActiveToolChanged?.Invoke(newValue);
        newValue?.HandleToolSwitchIn();
    }

    [RelayCommand]
    private void SwitchTool(PointerTool tool)
    {
        ActivePointerTool = tool;
    }

    public void BuildToolOptions(StrokeTool strokeTool)
    {
        ActiveToolOptions.Clear();

        foreach (var option in strokeTool.ToolOptions)
        {
            switch (option)
            {
                case ToolOption.StrokeThickness:
                    strokeTool.StrokePaint.StrokeWidth = _toolOptionsValues.StrokeThickness;
                    var thicknessVm = new StrokeThicknessOptionViewModel(_toolOptionsValues.StrokeThickness)
                    {
                        ThicknessChanged = v =>
                        {
                            _toolOptionsValues.StrokeThickness = v;
                            strokeTool.StrokePaint.StrokeWidth = v;
                        }
                    };
                    ActiveToolOptions.Add(thicknessVm);
                    break;

                case ToolOption.StrokeColor:
                    strokeTool.StrokePaint.Color = Utilities.ToSkColor(_toolOptionsValues.StrokeColor);
                    var colorVm = new StrokeColorOptionViewModel(_toolOptionsValues.StrokeColor)
                    {
                        ColorChanged = c =>
                        {
                            _toolOptionsValues.StrokeColor = c;
                            strokeTool.StrokePaint.Color = Utilities.ToSkColor(c);
                        }
                    };
                    ActiveToolOptions.Add(colorVm);
                    break;

                case ToolOption.StrokeStyle:
                    strokeTool.StrokePaint.DashIntervals = _toolOptionsValues.DashIntervals;
                    var styleVm = new StrokeStyleOptionViewModel(_toolOptionsValues.StrokeStyle)
                    {
                        StyleChanged = (style, intervals) =>
                        {
                            _toolOptionsValues.StrokeStyle = style;
                            _toolOptionsValues.DashIntervals = intervals;
                            strokeTool.StrokePaint.DashIntervals = intervals;
                        }
                    };
                    ActiveToolOptions.Add(styleVm);
                    break;

                case ToolOption.FillColor:
                    strokeTool.StrokePaint.FillColor = Utilities.ToSkColor(_toolOptionsValues.FillColor);
                    var fillVm = new FillColorOptionViewModel(_toolOptionsValues.FillColor)
                    {
                        FillColorChanged = c =>
                        {
                            _toolOptionsValues.FillColor = c;
                            strokeTool.StrokePaint.FillColor = Utilities.ToSkColor(c);
                        }
                    };
                    ActiveToolOptions.Add(fillVm);
                    break;

                case ToolOption.EdgeType:
                    strokeTool.StrokePaint.StrokeJoin = _toolOptionsValues.EdgeType == EdgeType.Rounded
                        ? SKStrokeJoin.Round
                        : SKStrokeJoin.Miter;
                    var edgeVm = new EdgeTypeOptionViewModel(_toolOptionsValues.EdgeType)
                    {
                        EdgeTypeChanged = et =>
                        {
                            _toolOptionsValues.EdgeType = et;
                            strokeTool.StrokePaint.StrokeJoin = et == EdgeType.Rounded
                                ? SKStrokeJoin.Round
                                : SKStrokeJoin.Miter;
                        }
                    };
                    ActiveToolOptions.Add(edgeVm);
                    break;

                case ToolOption.FontSize:
                    strokeTool.StrokePaint.TextSize = _toolOptionsValues.FontSize;
                    var fontVm = new FontSizeOptionViewModel(_toolOptionsValues.FontSize)
                    {
                        FontSizeChanged = fs =>
                        {
                            _toolOptionsValues.FontSize = fs;
                            strokeTool.StrokePaint.TextSize = fs;
                        }
                    };
                    ActiveToolOptions.Add(fontVm);
                    break;
            }
        }

        ToolOptionsVisible = ActiveToolOptions.Count > 0;
    }

    public void ShowSelectedCanvasElementOptions(List<CanvasElement> selectedElements)
    {
        var filteredStrokeIds = new Dictionary<ToolOption, List<Guid>>();
        foreach (var element in selectedElements)
        {
            if (element is not DrawStroke selectedStroke)
            {
                continue;
            }

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

        BuildSelectionEditOptions(filteredStrokeIds);

        if (_canvasStateService.SelectedElementIds.Count > 0)
        {
            var targetElementIds = _canvasStateService.SelectedElementIds.ToArray();
            var currentElements = _canvasStateService.CanvasElements;
            var currentMaxLayer = 0;
            var currentMinLayer = 0;
            foreach (var element in currentElements)
            {
                currentMinLayer = Math.Min(currentMinLayer, element.LayerIndex);
                currentMaxLayer = Math.Max(currentMaxLayer, element.LayerIndex);
            }

            var layerOptionsVm = new LayerOrderOptionViewModel(
                moveUp: () =>
                {
                    _canvasStateService.ApplyEvent(
                        new NudgeElementLayerEvent(Guid.NewGuid(), targetElementIds, 1));
                },
                moveDown: () =>
                {
                    _canvasStateService.ApplyEvent(
                        new NudgeElementLayerEvent(Guid.NewGuid(), targetElementIds, -1));
                },
                sendToFront: () =>
                {
                    // Move selection to a new top-most layer
                    var newTopLayer = currentMaxLayer + 1;
                    _canvasStateService.ApplyEvent(
                        new SetElementLayerEvent(Guid.NewGuid(), targetElementIds, newTopLayer));
                },
                sendToBack: () =>
                {
                    // Move selection to the lowest layer
                    var newMinLayer = currentMinLayer - 1;
                    _canvasStateService.ApplyEvent(
                        new SetElementLayerEvent(Guid.NewGuid(), targetElementIds, newMinLayer));
                });

            ActiveToolOptions.Add(layerOptionsVm);
            ToolOptionsVisible = ActiveToolOptions.Count > 0;
        }
    }

    private void BuildSelectionEditOptions(Dictionary<ToolOption, List<Guid>> categorizedStrokeIds)
    {
        ActiveToolOptions.Clear();

        foreach (var (option, strokeIds) in categorizedStrokeIds)
        {
            switch (option)
            {
                case ToolOption.StrokeThickness:
                    var thicknessVm = new StrokeThicknessOptionViewModel(_toolOptionsValues.StrokeThickness)
                    {
                        ThicknessChanged = v =>
                        {
                            _toolOptionsValues.StrokeThickness = v;
                            _canvasStateService.ApplyEvent(
                                new UpdateStrokeThicknessEvent(Guid.NewGuid(), strokeIds, v));
                        }
                    };
                    ActiveToolOptions.Add(thicknessVm);
                    break;

                case ToolOption.StrokeColor:
                    var colorVm = new StrokeColorOptionViewModel(_toolOptionsValues.StrokeColor)
                    {
                        ColorChanged = c =>
                        {
                            _toolOptionsValues.StrokeColor = c;
                            _canvasStateService.ApplyEvent(new UpdateStrokeColorEvent(Guid.NewGuid(), strokeIds,
                                Utilities.ToSkColor(c)));
                        }
                    };
                    ActiveToolOptions.Add(colorVm);
                    break;

                case ToolOption.StrokeStyle:
                    var styleVm = new StrokeStyleOptionViewModel(_toolOptionsValues.StrokeStyle)
                    {
                        StyleChanged = (style, intervals) =>
                        {
                            _toolOptionsValues.StrokeStyle = style;
                            _toolOptionsValues.DashIntervals = intervals;
                            _canvasStateService.ApplyEvent(new UpdateStrokeStyleEvent(Guid.NewGuid(), strokeIds,
                                intervals));
                        }
                    };
                    ActiveToolOptions.Add(styleVm);
                    break;

                case ToolOption.FillColor:
                    var fillVm = new FillColorOptionViewModel(_toolOptionsValues.FillColor)
                    {
                        FillColorChanged = c =>
                        {
                            _toolOptionsValues.FillColor = c;
                            _canvasStateService.ApplyEvent(
                                new UpdateStrokeFillColorEvent(Guid.NewGuid(), strokeIds, Utilities.ToSkColor(c)));
                        }
                    };
                    ActiveToolOptions.Add(fillVm);
                    break;

                case ToolOption.EdgeType:
                    var edgeVm = new EdgeTypeOptionViewModel(_toolOptionsValues.EdgeType)
                    {
                        EdgeTypeChanged = et =>
                        {
                            _toolOptionsValues.EdgeType = et;
                            var join = et == EdgeType.Rounded ? SKStrokeJoin.Round : SKStrokeJoin.Miter;
                            _canvasStateService.ApplyEvent(
                                new UpdateStrokeEdgeTypeEvent(Guid.NewGuid(), strokeIds, join));
                        }
                    };
                    ActiveToolOptions.Add(edgeVm);
                    break;

                case ToolOption.FontSize:
                    var fontVm = new FontSizeOptionViewModel(_toolOptionsValues.FontSize)
                    {
                        FontSizeChanged = fs =>
                        {
                            _toolOptionsValues.FontSize = fs;
                            _canvasStateService.ApplyEvent(
                                new UpdateStrokeFontSizeEvent(Guid.NewGuid(), strokeIds, fs));
                        }
                    };
                    ActiveToolOptions.Add(fontVm);
                    break;
            }
        }

        ToolOptionsVisible = ActiveToolOptions.Count > 0;
    }

    public void ClearToolOptions()
    {
        ActiveToolOptions.Clear();
        ToolOptionsVisible = false;
    }
}