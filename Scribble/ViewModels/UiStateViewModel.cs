using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Scribble.Lib;
using Scribble.Messages;
using Scribble.Shared.Lib;
using Scribble.Tools.PointerTools;
using Scribble.Utils;
using Scribble.ViewModels.ToolOptions;
using SkiaSharp;

namespace Scribble.ViewModels;

public partial class UiStateViewModel : ViewModelBase
{
    public const double MinZoom = 1.0f;
    public const double MaxZoom = 3.0f;
    public ScaleTransform ScaleTransform { get; } = new ScaleTransform(1, 1);

    private bool CanZoomIn => ZoomLevel < MaxZoom;
    private bool CanZoomOut => ZoomLevel > MinZoom;

    public event Action<PointerTool?>? ActiveToolChanged;
    public event Action<double>? CenterZoomRequested;

    [ObservableProperty] private Color _backgroundColor = Color.Parse("#a2000000");
    [ObservableProperty] private PointerTool? _activePointerTool;
    [ObservableProperty] private bool _toolOptionsVisible;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ScaleFactorText))]
    private double _zoomLevel = 1.0f;

    public string ScaleFactorText => $"{Math.Floor(ZoomLevel / MinZoom * 100)}%";
    public ObservableCollection<PointerTool> AvailableTools { get; } = [];
    public ObservableCollection<ToolOptionViewModelBase> ActiveToolOptions { get; } = [];

    private readonly ToolOptionsValues _toolOptionsValues = new();

    public UiStateViewModel()
    {
        // Automatically listen for document loads to change the background color
        WeakReferenceMessenger.Default.Register<LoadCanvasDataMessage>(this, (r, message) =>
        {
            if (message.BackgroundColorHex != null)
            {
                BackgroundColor = Color.Parse(message.BackgroundColorHex);
            }
        });
    }

    public void ChangeBackgroundColor(Color color)
    {
        BackgroundColor = color;
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

    // runs when _activePointerTool changes
    partial void OnActivePointerToolChanged(PointerTool? oldValue, PointerTool? newValue)
    {
        oldValue?.Dispose();
        ActiveToolChanged?.Invoke(newValue);
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

    public void BuildSelectionEditOptions(Dictionary<ToolOption, List<Guid>> categorizedStrokeIds,
        Action<Event> applyEvent)
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
                            applyEvent(new UpdateStrokeThicknessEvent(Guid.NewGuid(), strokeIds, v));
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
                            applyEvent(new UpdateStrokeColorEvent(Guid.NewGuid(), strokeIds, Utilities.ToSkColor(c)));
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
                            applyEvent(new UpdateStrokeStyleEvent(Guid.NewGuid(), strokeIds, intervals));
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
                            applyEvent(
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
                            applyEvent(new UpdateStrokeEdgeTypeEvent(Guid.NewGuid(), strokeIds, join));
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
                            applyEvent(new UpdateStrokeFontSizeEvent(Guid.NewGuid(), strokeIds, fs));
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