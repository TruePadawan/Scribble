using System;
using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Scribble.Messages;
using Scribble.Tools.PointerTools;

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

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ScaleFactorText))]
    private double _zoomLevel = 1.0f;

    public string ScaleFactorText => $"{Math.Floor(ZoomLevel / MinZoom * 100)}%";
    public ObservableCollection<PointerTool> AvailableTools { get; } = [];

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
}