using System;
using CommunityToolkit.Mvvm.Input;

namespace Scribble.ViewModels.ToolOptions;

/// <summary>
/// View model for managing layer ordering for the current selection.
/// </summary>
public partial class LayerOrderOptionViewModel : ToolOptionViewModelBase
{
    private readonly Action? _moveUp;
    private readonly Action? _moveDown;
    private readonly Action? _sendToFront;
    private readonly Action? _sendToBack;

    public LayerOrderOptionViewModel(Action? moveUp, Action? moveDown, Action? sendToFront, Action? sendToBack)
        : base("Layer Order")
    {
        _moveUp = moveUp;
        _moveDown = moveDown;
        _sendToFront = sendToFront;
        _sendToBack = sendToBack;
    }

    [RelayCommand]
    private void MoveUp()
    {
        _moveUp?.Invoke();
    }

    [RelayCommand]
    private void MoveDown()
    {
        _moveDown?.Invoke();
    }

    [RelayCommand]
    private void SendToFront()
    {
        _sendToFront?.Invoke();
    }

    [RelayCommand]
    private void SendToBack()
    {
        _sendToBack?.Invoke();
    }
}

