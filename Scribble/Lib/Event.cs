using System;
using SkiaSharp;

namespace Scribble.Lib;

/// <summary>
/// Represents the base class for all events in the application.
/// </summary>
public abstract record Event
{
    public DateTime TimeStamp { get; init; } = DateTime.UtcNow;
}

public record StrokeOperation : Event
{
    public enum StrokeOperationType
    {
        Add,
        Remove,
        Move
    }

    public StrokeOperationType Type { get; init; }
    public Guid StrokeId { get; init; }
    public SKPoint? NewPosition { get; init; }
    public Stroke? NewStrokeData { get; init; } // Only for "Add"
}