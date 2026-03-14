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
    public Guid FileId { get; init; }
}