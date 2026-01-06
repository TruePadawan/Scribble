using System.Collections.Generic;
using Scribble.ViewModels;

namespace Scribble.Lib;

public class EventsManager(MainViewModel viewModel)
{
    private List<Event> Events { get; } = [];

    public void Apply(Event @event)
    {
        using var frame = viewModel.WhiteboardBitmap.Lock();
        var address = frame.Address;
        var stride = frame.RowBytes;

        switch (@event)
        {
            case PointsDrawn e:
                // Draw an interior segment without round caps to avoid over-dark joints,
                // then place a single circular dab at the current point to form a smooth join.
                viewModel.DrawSegmentNoCaps(address, stride, e.Start, e.End, e.Color, e.StrokeWidth, e.Color.A / 255f);
                viewModel.DrawSinglePixel(address, stride, e.End, e.Color, e.StrokeWidth, e.Color.A / 255f);
                break;
            case PointDrawn e:
                viewModel.DrawSinglePixel(address, stride, e.Coord, e.Color, e.StrokeWidth, e.Color.A / 255f);
                break;
            case PointsErased e:
                viewModel.EraseSegmentNoCaps(address, stride, e.Start, e.End, e.Radius);
                viewModel.EraseSinglePixel(address, stride, e.End, e.Radius);
                break;
            case PointErased e:
                viewModel.EraseSinglePixel(address, stride, e.Coord, e.Radius);
                break;
        }

        Events.Add(@event);
    }
}