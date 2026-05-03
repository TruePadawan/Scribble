using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Scribble.Services;
using Scribble.Shared.Lib;
using Scribble.State;
using SkiaSharp;

namespace Scribble.Tools.PointerTools.SelectTool;

class SelectTool : PointerTool
{
    private Border? _selectionBorder;
    private readonly Canvas _canvasContainer;
    private SKPoint _startPoint;
    private Guid _boundId = Guid.NewGuid();
    private Guid _actionId = Guid.NewGuid();

    public SelectTool(string name, CanvasStateService canvasState, Canvas canvasContainer) : base(name, canvasState,
        LoadToolBitmap(typeof(SelectTool), "cursor.png"))
    {
        Cursor = Cursor.Default;
        _canvasContainer = canvasContainer;
        HotKey = new KeyGesture(Key.D9);
        ToolTip = "Select Tool - 9";
    }

    public override void HandlePointerClick(SKPoint coord)
    {
        // coord is in world-space, convert to screen-space for positioning the visual border
        _startPoint = CameraState.WorldToScreen(coord);

        _selectionBorder = new Border
        {
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Gray,
            Width = 0,
            Height = 0
        };
        Canvas.SetLeft(_selectionBorder, _startPoint.X);
        Canvas.SetTop(_selectionBorder, _startPoint.Y);
        _canvasContainer.Children.Add(_selectionBorder);
        _boundId = Guid.NewGuid();
        _actionId = Guid.NewGuid();
        if (CanvasState.ActiveSelectionBoundId != null)
        {
            CanvasState.ClearSelection();
        }

        // Events use world-space coordinates
        CanvasState.ApplyEvent(new CreateSelectionBoundEvent(_actionId, _boundId, coord));
    }

    public override void HandlePointerMove(SKPoint prevCoord, SKPoint currentCoord)
    {
        if (_selectionBorder == null) return;

        // currentCoord is in world-space, convert to screen-space for visual border
        var screenPos = CameraState.WorldToScreen(currentCoord);

        _selectionBorder.Width = Math.Abs(screenPos.X - _startPoint.X);
        _selectionBorder.Height = Math.Abs(screenPos.Y - _startPoint.Y);

        Canvas.SetLeft(_selectionBorder, Math.Min(_startPoint.X, screenPos.X));
        Canvas.SetTop(_selectionBorder, Math.Min(_startPoint.Y, screenPos.Y));

        // Events use world-space coordinates
        CanvasState.ApplyEvent(new IncreaseSelectionBoundEvent(_actionId, _boundId, currentCoord));
    }

    public override void HandlePointerRelease(SKPoint prevCoord, SKPoint currentCoord)
    {
        if (_selectionBorder == null) return;

        _canvasContainer.Children.Remove(_selectionBorder);
        _selectionBorder = null;
        CanvasState.ApplyEvent(new EndSelectionEvent(_actionId, _boundId));
    }

    public override void HandleToolSwitchOut()
    {
        CanvasState.ClearSelection();
    }
}