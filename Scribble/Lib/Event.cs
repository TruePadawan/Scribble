using System;
using System.Collections.Generic;
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
/// </summary>
public record PointsDrawn(List<Point> Points, Color Color, int StrokeWidth) : Event;

/// <summary>
/// Represents an event that captures the drawing of a single point
/// using a specified color and stroke width
/// </summary>
public record PointDrawn(Point Coord, Color Color, int StrokeWidth) : Event;

/// <summary>
/// Represents an event that captures the erasing of a line segment
/// between two points using a specified radius
/// </summary>
public record PointsErased(List<Point> Points, int Radius) : Event;

/// <summary>
/// Represents an event that captures the erasing of a single point
/// using a specified radius
/// </summary>
public record PointErased(Point Coord, int Radius) : Event;