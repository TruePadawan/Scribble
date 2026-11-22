using System;

namespace Scribble.Dtos;

public record CanvasImageDto
{
    public Guid Id { get; init; }
    public float Width { get; init; }
    public float Height { get; init; }
    public float X { get; init; }
    public float Y { get; init; }

    public int LayerIndex { get; init; }

    // Older files omit these fields and load without rotation or flips.
    public float Rotation { get; init; }
    public bool FlipX { get; init; }
    public bool FlipY { get; init; }
    public Guid FileId { get; init; }
}