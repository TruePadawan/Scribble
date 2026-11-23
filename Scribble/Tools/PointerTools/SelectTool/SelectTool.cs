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
using SkiaSharp;
using ISelectable = Scribble.Shared.Lib.CanvasElements.ISelectable;

namespace Scribble.Tools.PointerTools.SelectTool;

class SelectTool : PointerTool
{
    private Border? _selectionBorder;
    private readonly Canvas _canvasContainer;
    private readonly CameraState _camera;
    private SKPoint _startPoint;
    private readonly Action<IReadOnlyList<Guid>> _showSelection;

    public SelectTool(string name, ICanvasStateService canvasStateService, Canvas canvasContainer,
        CameraState camera, Action<IReadOnlyList<Guid>> showSelection) : base(name,
        canvasStateService,
        LoadToolBitmap(typeof(SelectTool), "cursor.png"))
    {
        Cursor = Cursor.Default;
        _canvasContainer = canvasContainer;
        _camera = camera;
        _showSelection = showSelection;
        HotKey = new KeyGesture(Key.V);
        ToolTip = "Select Tool (V)";
    }

    public override void HandlePointerClick(SKPoint coord)
    {
        _startPoint = coord;
        var screenStart = _camera.WorldToScreen(coord);

        _selectionBorder = new Border
        {
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Gray,
            Width = 0,
            Height = 0
        };
        Canvas.SetLeft(_selectionBorder, screenStart.X);
        Canvas.SetTop(_selectionBorder, screenStart.Y);
        _canvasContainer.Children.Add(_selectionBorder);
        // Preview the gesture locally. Only its final selection belongs in undo history.
        _showSelection([]);
    }

    public override void HandlePointerMove(SKPoint prevCoord, SKPoint currentCoord)
    {
        if (_selectionBorder == null) return;

        // currentCoord is in world-space, convert to screen-space for visual border
        var screenStart = _camera.WorldToScreen(_startPoint);
        var screenPos = _camera.WorldToScreen(currentCoord);

        _selectionBorder.Width = Math.Abs(screenPos.X - screenStart.X);
        _selectionBorder.Height = Math.Abs(screenPos.Y - screenStart.Y);

        Canvas.SetLeft(_selectionBorder, Math.Min(screenStart.X, screenPos.X));
        Canvas.SetTop(_selectionBorder, Math.Min(screenStart.Y, screenPos.Y));

        _showSelection(GetTargets(currentCoord));
    }

    /// <summary>Resolves the drag rectangle in either direction using canvas ownership rules.</summary>
    private List<Guid> GetTargets(SKPoint end) =>
        end == _startPoint
            ? []
            : CanvasStateService.GetEnclosedElementIds(
                new SKRect(_startPoint.X, _startPoint.Y, end.X, end.Y).Standardized);

    public override void HandlePointerRelease(SKPoint prevCoord, SKPoint currentCoord)
    {
        if (_selectionBorder == null)
        {
            return;
        }

        _canvasContainer.Children.Remove(_selectionBorder);
        _selectionBorder = null;

        var targets = GetTargets(currentCoord);
        if (targets.Count > 0)
        {
            CanvasStateService.ApplyEvent(new SelectByIdsEvent(Guid.NewGuid(), Guid.NewGuid(), targets));
        }
        else
        {
            _showSelection([]);
            CanvasStateService.ClearSelection();
        }
    }

    public override void HandleToolSwitchOut()
    {
        if (_selectionBorder != null)
        {
            _canvasContainer.Children.Remove(_selectionBorder);
            _selectionBorder = null;
            _showSelection([]);
        }

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
            .Where(allElementIds.Contains)
            .ToList();

        if (elementIds.Count == 0) return;

        CanvasStateService.ApplyEvent(
            new SelectByIdsEvent(Guid.NewGuid(), Guid.NewGuid(), elementIds));
    }
}