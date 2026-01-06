using System;
using Avalonia;
using Avalonia.Media;

namespace Scribble.Lib;

/// <summary>
/// Represents the base class for all events in the application.
/// </summary>
public abstract record Event
{
    public DateTime TimeStamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Represents an event that captures the drawing of a line segment
/// between two points using a specified color and stroke width.
/// </summary>
public record PointsDrawn(Point Start, Point End, Color Color, int StrokeWidth) : Event;

/// <summary>
/// Represents an event that captures the drawing of a single point
/// using a specified color and stroke width
/// </summary>
public record PointDrawn(Point Coord, Color Color, int StrokeWidth) : Event;

/// <summary>
/// Represents an event that captures the erasing of a line segment
/// between two points using a specified radius
/// </summary>
public record PointsErased(Point Start, Point End, int Radius) : Event;

/// <summary>
/// Represents an event that captures the erasing of a single point
/// using a specified radius
/// </summary>
public record PointErased(Point Coord, int Radius) : Event;