using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Scribble.Services;
using Scribble.Shared.Lib;
using Scribble.Utils;
using SkiaSharp;

namespace Scribble.Tools.PointerTools.SelectTool;

class SelectTool : PointerTool
{
    private Border? _selectionBorder;
    private readonly Canvas _canvasContainer;
    private Point _startPoint;
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

    public override void HandlePointerClick(Point coord)
    {
        _selectionBorder = new Border
        {
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Gray,
            Width = 0,
            Height = 0
        };
        Canvas.SetLeft(_selectionBorder, coord.X);
        Canvas.SetTop(_selectionBorder, coord.Y);
        _startPoint = coord;
        _canvasContainer.Children.Add(_selectionBorder);
        _boundId = Guid.NewGuid();
        _actionId = Guid.NewGuid();
        if (CanvasState.ActiveSelectionBoundId != null)
        {
            CanvasState.ClearSelection();
        }

        CanvasState.ApplyEvent(new CreateSelectionBoundEvent(_actionId, _boundId,
            new SKPoint((float)coord.X, (float)coord.Y)));
    }

    public override void HandlePointerMove(Point prevCoord, Point currentCoord)
    {
        if (_selectionBorder == null) return;
        _selectionBorder.Width = Math.Abs(currentCoord.X - _startPoint.X);
        _selectionBorder.Height = Math.Abs(currentCoord.Y - _startPoint.Y);

        Canvas.SetLeft(_selectionBorder, Math.Min(_startPoint.X, currentCoord.X));
        Canvas.SetTop(_selectionBorder, Math.Min(_startPoint.Y, currentCoord.Y));
        CanvasState.ApplyEvent(new IncreaseSelectionBoundEvent(_actionId, _boundId, Utilities.ToSkPoint(currentCoord)));
    }

    public override void HandlePointerRelease(Point prevCoord, Point currentCoord)
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