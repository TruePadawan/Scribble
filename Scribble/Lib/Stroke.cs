using System;
using System.Collections.Generic;
using SkiaSharp;

namespace Scribble.Lib;

public abstract class Stroke
{
    // public Guid Id { get; set; } = Guid.NewGuid();
    public SKPath Path { get; init; } = new();
}

public class DrawStroke : Stroke
{
    public bool IsToBeErased = false;
    public bool IsArrow = false;
    public SKPaint Paint { get; init; } = new();
}

public class EraserStroke : Stroke
{
    public HashSet<Guid> Targets = [];
}