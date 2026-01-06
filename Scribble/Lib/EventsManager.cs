using System.Collections.Generic;
using Avalonia.Platform;
using Scribble.ViewModels;

namespace Scribble.Lib;

public class EventsManager(MainViewModel viewModel)
{
    private List<Event> Events { get; } = [];
    private int _currentEventIndex = -1;

    private void ApplyEvent(Event @event, ILockedFramebuffer frameBuffer)
    {
        var address = frameBuffer.Address;
        var stride = frameBuffer.RowBytes;

        switch (@event)
        {
            case PointsDrawn e:
                for (int i = 0; i < e.Points.Count - 1; i++)
                {
                    var start = e.Points[i];
                    var end = e.Points[i + 1];
                    viewModel.DrawSegmentNoCaps(address, stride, start, end, e.Color, e.StrokeWidth, e.Color.A / 255f);
                    viewModel.DrawSinglePixel(address, stride, end, e.Color, e.StrokeWidth, e.Color.A / 255f);
                }

                break;
            case PointDrawn e:
                viewModel.DrawSinglePixel(address, stride, e.Coord, e.Color, e.StrokeWidth, e.Color.A / 255f);
                break;
            case PointsErased e:
                for (int i = 0; i < e.Points.Count - 1; i++)
                {
                    var start = e.Points[i];
                    var end = e.Points[i + 1];
                    viewModel.EraseSegmentNoCaps(address, stride, start, end, e.Radius);
                    viewModel.EraseSinglePixel(address, stride, end, e.Radius);
                }

                break;
            case PointErased e:
                viewModel.EraseSinglePixel(address, stride, e.Coord, e.Radius);
                break;
        }
    }

    public void Apply(Event @event, bool skipRendering = false)
    {
        if (!skipRendering)
        {
            using var frameBuffer = viewModel.WhiteboardBitmap.Lock();
            ApplyEvent(@event, frameBuffer);
        }

        // Truncate all stale events first
        if (Events.Count > 0 && _currentEventIndex < Events.Count - 1)
        {
            Events.RemoveRange(_currentEventIndex + 1, Events.Count - _currentEventIndex - 1);
        }

        Events.Add(@event);
        _currentEventIndex = Events.Count - 1;
    }

    public void Revert()
    {
        if (Events.Count == 0 || _currentEventIndex == -1) return;

        using var frameBuffer = viewModel.WhiteboardBitmap.Lock();

        viewModel.ClearBitmap(frameBuffer);
        for (var i = 0; i < _currentEventIndex; i++)
        {
            var @event = Events[i];
            ApplyEvent(@event, frameBuffer);
        }

        _currentEventIndex -= 1;
    }

    public void FastForward()
    {
        if (Events.Count == 0 || _currentEventIndex >= Events.Count - 1) return;

        using var frameBuffer = viewModel.WhiteboardBitmap.Lock();

        _currentEventIndex += 1;
        var @eventToApply = Events[_currentEventIndex];
        ApplyEvent(@eventToApply, frameBuffer);
    }
}