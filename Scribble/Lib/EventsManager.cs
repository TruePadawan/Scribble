using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Scribble.ViewModels;

namespace Scribble.Lib;

public class EventsManager(MainViewModel viewModel)
{
    private List<Event> Events { get; } = [];
    private int _currentEventIndex = -1;

    private WriteableBitmap? _checkpointBitmap;
    private int _checkpointIndex = -1;
    private const int CheckpointInterval = 10;

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

        // Truncate all stale events if we branch off
        if (Events.Count > 0 && _currentEventIndex < Events.Count - 1)
        {
            Events.RemoveRange(_currentEventIndex + 1, Events.Count - _currentEventIndex - 1);

            // If we go back past the checkpoint, the checkpoint is now from an invalid future
            if (_checkpointIndex > _currentEventIndex)
            {
                _checkpointIndex = -1;
            }
        }

        Events.Add(@event);
        _currentEventIndex = Events.Count - 1;

        // Update the checkpoint periodically to keep undo fast
        if (_currentEventIndex > 0 && _currentEventIndex % CheckpointInterval == 0)
        {
            UpdateCheckPoint();
        }
    }

    private void UpdateCheckPoint()
    {
        var size = viewModel.WhiteboardBitmap.PixelSize;
        var dpi = viewModel.WhiteboardBitmap.Dpi;

        // Lazy initialization
        if (_checkpointBitmap == null || _checkpointBitmap.PixelSize != size)
        {
            _checkpointBitmap?.Dispose();
            // Create the backup bitmap
            _checkpointBitmap = new WriteableBitmap(size, dpi, PixelFormat.Bgra8888);
        }

        // copy screen to checkpoint
        using var srcBuffer = viewModel.WhiteboardBitmap.Lock();
        using var checkpointBuffer = _checkpointBitmap.Lock();
        var srcSize = srcBuffer.RowBytes * size.Height;
        var dstSize = checkpointBuffer.RowBytes * size.Height;

        unsafe
        {
            Buffer.MemoryCopy(srcBuffer.Address.ToPointer(), checkpointBuffer.Address.ToPointer(), dstSize, srcSize);
        }

        _checkpointIndex = _currentEventIndex;
    }

    public void Revert()
    {
        if (Events.Count == 0 || _currentEventIndex == -1) return;

        using var frameBuffer = viewModel.WhiteboardBitmap.Lock();
        // Fast Undo: Use checkpoint if the target state is after our saved checkpoint
        if (_checkpointIndex != -1 && _currentEventIndex - 1 >= _checkpointIndex)
        {
            using var checkpointBuffer = _checkpointBitmap!.Lock();
            var size = viewModel.WhiteboardBitmap.PixelSize;
            var srcSize = checkpointBuffer.RowBytes * size.Height;
            var dstSize = frameBuffer.RowBytes * size.Height;

            unsafe
            {
                Buffer.MemoryCopy(checkpointBuffer.Address.ToPointer(), frameBuffer.Address.ToPointer(), dstSize,
                    srcSize);
            }

            // Replay the remaining small events
            for (int i = _checkpointIndex + 1; i <= _currentEventIndex - 1; i++)
            {
                ApplyEvent(Events[i], frameBuffer);
            }
        }
        else
        {
            // Full Replay
            viewModel.ClearBitmap(frameBuffer);
            for (var i = 0; i < _currentEventIndex; i++)
            {
                ApplyEvent(Events[i], frameBuffer);
            }
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