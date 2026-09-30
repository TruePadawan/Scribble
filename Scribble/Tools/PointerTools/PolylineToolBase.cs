using System;
using System.Collections.Generic;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Scribble.Services.CanvasStateService;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.Events;
using SkiaSharp;

namespace Scribble.Tools.PointerTools;

/// <summary>
/// Shared base class for Line and Arrow tools that supports two drawing modes:
/// 1. Single click starts multi-click polyline mode
/// 2. Press-and-drag draws a single segment
/// </summary>
public abstract class PolylineToolBase : StrokeTool
{
    private const float ClickThresholdWorldUnits = 4f;

    private PolylineDrawState _drawState = PolylineDrawState.Idle;
    private Guid _strokeId;
    private Guid _startActionId;
    private SKPoint _pressPoint;
    private bool _dragThresholdExceeded;
    private readonly List<SKPoint> _confirmedNodes = [];

    protected abstract ToolType GetToolType();

    public override bool IsDrawing => _drawState == PolylineDrawState.MultiClick;

    protected PolylineToolBase(string name, ICanvasStateService canvasState, Bitmap icon)
        : base(name, canvasState, icon)
    {
    }

    public override void HandlePointerClick(SKPoint startPoint)
    {
        switch (_drawState)
        {
            case PolylineDrawState.Idle:
                // Start a new stroke
                _strokeId = Guid.NewGuid();
                _startActionId = Guid.NewGuid();
                _pressPoint = startPoint;
                _dragThresholdExceeded = false;
                _confirmedNodes.Clear();
                _confirmedNodes.Add(startPoint);

                CanvasStateService.ApplyEvent(new StartStrokeEvent(
                    _startActionId, _strokeId, startPoint,
                    StrokePaint.Clone(), GetToolType(), ToolOptions));

                _drawState = PolylineDrawState.PendingDecision;
                break;

            case PolylineDrawState.MultiClick:
                // Subsequent click in multi-click mode: record press point
                _pressPoint = startPoint;
                break;
        }
    }

    public override void HandleDoubleClick(SKPoint coord)
    {
        if (_drawState == PolylineDrawState.MultiClick)
        {
            FinalizeMultiClickStroke();
        }
    }

    public override void HandlePointerMove(SKPoint prevCoord, SKPoint currentCoord)
    {
        switch (_drawState)
        {
            case PolylineDrawState.PendingDecision:
                var distance = SKPoint.Distance(_pressPoint, currentCoord);
                if (distance > ClickThresholdWorldUnits)
                {
                    _dragThresholdExceeded = true;
                    _drawState = PolylineDrawState.DragDrawing;
                }

                // Rubberband preview (shows line from start to current mouse position)
                CanvasStateService.ApplyEvent(
                    new LineStrokeLineToEvent(_startActionId, _strokeId, currentCoord));
                break;

            case PolylineDrawState.DragDrawing:
                // Legacy drag mode: update the endpoint
                CanvasStateService.ApplyEvent(
                    new LineStrokeLineToEvent(_startActionId, _strokeId, currentCoord));
                break;

            case PolylineDrawState.MultiClick:
                // Rubberband preview from last confirmed node to cursor
                CanvasStateService.ApplyEvent(
                    new LineStrokeLineToEvent(_startActionId, _strokeId, currentCoord));
                break;
        }
    }

    public override void HandlePointerRelease(SKPoint prevCoord, SKPoint currentCoord)
    {
        switch (_drawState)
        {
            case PolylineDrawState.PendingDecision:
                if (_dragThresholdExceeded)
                {
                    // If dragged past threshold but release came before the move handler
                    // transitioned to DragDrawing? treat as drag drawing
                    CanvasStateService.ApplyEvent(new EndStrokeEvent(_startActionId));
                    ResetState();
                }
                else
                {
                    // Quick click: enter multi-click mode
                    // The first node is already committed via StartStrokeEvent
                    _drawState = PolylineDrawState.MultiClick;
                }

                break;

            case PolylineDrawState.DragDrawing:
                // Legacy drag end: finalize the stroke
                CanvasStateService.ApplyEvent(new EndStrokeEvent(_startActionId));
                ResetState();
                break;

            case PolylineDrawState.MultiClick:
                // Click release in multi-click mode: add a new node
                _confirmedNodes.Add(currentCoord);
                CanvasStateService.ApplyEvent(
                    new AddPolylineNodeEvent(_startActionId, _strokeId, currentCoord));
                break;
        }
    }

    public override bool HandleKeyPress(Key key)
    {
        if (_drawState != PolylineDrawState.MultiClick) return false;

        if (key is Key.Escape or Key.Enter)
        {
            FinalizeMultiClickStroke();
            return true;
        }

        return false;
    }

    public override void HandleToolSwitchOut()
    {
        base.HandleToolSwitchOut();
        if (_drawState == PolylineDrawState.MultiClick)
        {
            FinalizeMultiClickStroke();
        }
    }

    /// <summary>
    /// Ends the multi-click drawing session, cancelling the uncommitted rubberband segment.
    /// </summary>
    private void FinalizeMultiClickStroke()
    {
        // Snap the rubberband back to the last confirmed node to cancel the uncommitted segment
        if (_confirmedNodes.Count > 0)
        {
            var lastNode = _confirmedNodes[^1];
            CanvasStateService.ApplyEvent(
                new LineStrokeLineToEvent(_startActionId, _strokeId, lastNode));
        }

        CanvasStateService.ApplyEvent(new EndStrokeEvent(_startActionId));
        ResetState();
    }

    private void ResetState()
    {
        _drawState = PolylineDrawState.Idle;
        _confirmedNodes.Clear();
        _dragThresholdExceeded = false;
    }

    private enum PolylineDrawState
    {
        Idle,
        PendingDecision,
        DragDrawing,
        MultiClick
    }
}