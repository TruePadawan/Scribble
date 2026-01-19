using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Scribble.Lib;
using Scribble.Utils;
using Scribble.ViewModels;
using SkiaSharp;

namespace Scribble.Tools.PointerTools.SelectTool;

class SelectTool : PointerToolsBase
{
    private Border? _selectionBorder;
    private readonly Canvas _canvasContainer;
    private Point _startPoint;
    private Guid _boundId = Guid.NewGuid();

    public SelectTool(string name, MainViewModel viewModel, Canvas canvasContainer) : base(name, viewModel,
        LoadToolBitmap(typeof(SelectTool), "cursor.png"))
    {
        Cursor = Cursor.Default;
        _canvasContainer = canvasContainer;
    }

    public override void HandlePointerClick(Point coord)
    {
        ClearSelectionVisualization();

        _selectionBorder = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Aquamarine,
            Width = 0,
            Height = 0
        };
        Canvas.SetLeft(_selectionBorder, coord.X);
        Canvas.SetTop(_selectionBorder, coord.Y);
        _startPoint = coord;
        _canvasContainer.Children.Add(_selectionBorder);
        _boundId = Guid.NewGuid();
        ViewModel.ApplyStrokeEvent(new CreateSelectionBoundEvent(_boundId,
            new SKPoint((float)coord.X, (float)coord.Y)));
    }

    public override void HandlePointerMove(Point prevCoord, Point currentCoord)
    {
        if (_selectionBorder == null) return;
        _selectionBorder.Width = Math.Abs(currentCoord.X - _startPoint.X);
        _selectionBorder.Height = Math.Abs(currentCoord.Y - _startPoint.Y);

        Canvas.SetLeft(_selectionBorder, Math.Min(_startPoint.X, currentCoord.X));
        Canvas.SetTop(_selectionBorder, Math.Min(_startPoint.Y, currentCoord.Y));
        ViewModel.ApplyStrokeEvent(new IncreaseSelectionBoundEvent(_boundId, Utilities.ToSkPoint(currentCoord)));
        VisualizeSelection();
    }

    public override void HandlePointerRelease(Point prevCoord, Point currentCoord)
    {
        if (_selectionBorder == null) return;

        _canvasContainer.Children.Remove(_selectionBorder);
        _selectionBorder = null;
        ViewModel.ApplyStrokeEvent(new EndSelectionEvent(_boundId));
        VisualizeSelection();
    }

    private void VisualizeSelection()
    {
        if (_canvasContainer.Children.FirstOrDefault(child => child is StackPanel { Name: "SelectionOverlay" }) is not
            StackPanel overlay) return;
        if (overlay.Children.FirstOrDefault(child => child is Border { Name: "SelectionBorder" }) is not Border border)
            return;

        // If we are not currently dragging a selection, sync with the latest selection in the view model
        if (_selectionBorder == null && ViewModel.SelectionTargets.Count > 0)
        {
            _boundId = ViewModel.SelectionTargets.Keys.Last();
        }

        if (ViewModel.SelectionTargets.TryGetValue(_boundId, out var selectedIds) && selectedIds.Count > 0)
        {
            var selectedStrokes = ViewModel.CanvasStrokes
                .Where(stroke => selectedIds.Contains(stroke.Id) && stroke is DrawStroke)
                .Cast<DrawStroke>()
                .ToList();
            if (selectedStrokes.Count == 0)
            {
                overlay.IsVisible = false;
                return;
            }

            SKRect combinedBounds = SKRect.Empty;
            // bool first = true;

            foreach (var stroke in selectedStrokes)
            {
                SKRect strokeBounds;
                if (stroke is TextStroke textStroke && stroke.Path.PointCount > 0)
                {
                    var pos = textStroke.Path[0];
                    var bounds = new SKRect();
                    textStroke.Paint.MeasureText(textStroke.Text, ref bounds);
                    bounds.Offset(pos);
                    strokeBounds = bounds;
                }
                else
                {
                    strokeBounds = stroke.Path.Bounds;
                }

                if (combinedBounds == SKRect.Empty)
                {
                    combinedBounds = strokeBounds;
                }
                else
                {
                    combinedBounds.Union(strokeBounds);
                }
            }

            Canvas.SetLeft(overlay, combinedBounds.Left);
            Canvas.SetTop(overlay, combinedBounds.Top - 15 - 6);

            border.Width = combinedBounds.Width;
            border.Height = combinedBounds.Height;
            overlay.IsVisible = true;
        }
        else
        {
            overlay.IsVisible = false;
        }
    }

    private void ClearSelectionVisualization()
    {
        if (_canvasContainer.Children.FirstOrDefault(c => c is StackPanel sp && sp.Name == "SelectionOverlay") is
            StackPanel overlay)
        {
            overlay.IsVisible = false;
        }
    }

    public override void Dispose()
    {
        ClearSelectionVisualization();
        // ViewModel.RequestInvalidateSelection -= OnSelectionChanged;
    }

    public void SubscribeToSelectionChanges()
    {
        ViewModel.RequestInvalidateSelection += OnSelectionChanged;
        // OnSelectionChanged();
    }

    private void OnSelectionChanged()
    {
        VisualizeSelection();
    }
}