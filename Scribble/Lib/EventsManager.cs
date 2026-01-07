using System.Collections.Generic;
using Avalonia.Platform;
using Scribble.ViewModels;

namespace Scribble.Lib;

public class EventsManager(MainViewModel viewModel)
{
    public List<Event> Events { get; } = [];
    public int CurrentEventIndex = -1;

    public void ApplyEvent(Event @event, ILockedFramebuffer frameBuffer)
    {
        switch (@event)
        {
            case PointsDrawn e:
                for (int i = 0; i < e.Points.Count - 1; i++)
                {
                    var start = e.Points[i];
                    var end = e.Points[i + 1];
                    if (i == 0)
                    {
                        viewModel.BitmapRenderer.DrawSinglePoint(frameBuffer, start, e.Color, e.StrokeWidth);
                    }

                    viewModel.BitmapRenderer.DrawStroke(frameBuffer, start, end, e.Color, e.StrokeWidth);
                }

                break;
            case PointDrawn e:
                viewModel.BitmapRenderer.DrawSinglePoint(frameBuffer, e.Coord, e.Color, e.StrokeWidth);
                break;
            case PointsErased e:
                for (int i = 0; i < e.Points.Count - 1; i++)
                {
                    var start = e.Points[i];
                    var end = e.Points[i + 1];
                    viewModel.BitmapRenderer.EraseStroke(frameBuffer, start, end, viewModel.BackgroundColor, e.Radius);
                }

                break;
            case PointErased e:
                viewModel.BitmapRenderer.EraseSinglePoint(frameBuffer, e.Coord, viewModel.BackgroundColor, e.Radius);
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
        if (Events.Count > 0 && CurrentEventIndex < Events.Count - 1)
        {
            Events.RemoveRange(CurrentEventIndex + 1, Events.Count - CurrentEventIndex - 1);

            // If we go back past the checkpoint, the checkpoint is now from an invalid future
            if (viewModel.CheckpointIndex > CurrentEventIndex)
            {
                viewModel.InvalidateCanvasSnapshot();
            }
        }

        Events.Add(@event);
        CurrentEventIndex = Events.Count - 1;

        // Update the checkpoint periodically to keep undo fast
        if (CurrentEventIndex > 0 && CurrentEventIndex % MainViewModel.CanvasSnapshotInterval == 0)
        {
            viewModel.UpdateCanvasSnapshot();
        }
    }
}