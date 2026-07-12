using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Scribble.Services.CanvasStateService;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.Events;
using Scribble.State;
using Scribble.Utils;
using SkiaSharp;
using ISelectable = Scribble.Shared.Lib.CanvasElements.ISelectable;

namespace Scribble.Tools.PointerTools.SelectTool;

class SelectTool : PointerTool
{
    private Border? _selectionBorder;
    private readonly Canvas _canvasContainer;
    private SKPoint _startPoint;
    private Guid _boundId = Guid.NewGuid();
    private Guid _actionId = Guid.NewGuid();

    public SelectTool(string name, ICanvasStateService canvasStateService, Canvas canvasContainer) : base(name,
        canvasStateService,
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
        if (CanvasStateService.ActiveSelectionBoundId != null)
        {
            CanvasStateService.ClearSelection();
        }

        // Events use world-space coordinates
        CanvasStateService.ApplyEvent(new CreateSelectionBoundEvent(_actionId, _boundId, coord));
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
        CanvasStateService.ApplyEvent(new IncreaseSelectionBoundEvent(_actionId, _boundId, currentCoord));
    }

    public override void HandlePointerRelease(SKPoint prevCoord, SKPoint currentCoord)
    {
        if (_selectionBorder == null) return;

        _canvasContainer.Children.Remove(_selectionBorder);
        _selectionBorder = null;
        CanvasStateService.ApplyEvent(new EndSelectionEvent(_actionId, _boundId));
    }

    public override void HandleToolSwitchOut()
    {
        CanvasStateService.ClearSelection();
    }

    /// <summary>
    /// Selects all elements in the list by their IDs.
    /// Fires a <see cref="SelectByIdsEvent"/> so the handler sets targets directly
    /// without any spatial containment check.
    /// </summary>
    public void SelectElements(List<ISelectable> elements)
    {
        if (elements.Count == 0) return;

        var allElementIds = CanvasStateService.CanvasElements
            .Select(e => e.Id)
            .ToHashSet();

        var elementIds = elements
            .OfType<CanvasElement>()
            .Select(e => e.Id)
            .Where(id => allElementIds.Contains(id))
            .ToList();

        if (elementIds.Count == 0) return;

        CanvasStateService.ApplyEvent(
            new SelectByIdsEvent(Guid.NewGuid(), Guid.NewGuid(), elementIds));
    }
}