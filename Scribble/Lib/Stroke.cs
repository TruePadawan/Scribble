using System;
using System.Collections.Generic;
using SkiaSharp;

namespace Scribble.Lib;

public abstract class Stroke
{
    // public Guid Id { get; set; } = Guid.NewGuid();
    public SKPath Path { get; init; } = new();
}

public enum StrokeTool
{
    Pencil,
    Line,
    Arrow,
    Ellipse,
    Rectangle,
    Text
}

public class DrawStroke : Stroke
{
    public bool IsToBeErased = false;
    public StrokeTool ToolType;
    public SKPaint Paint { get; init; } = new();
}

public class EraserStroke : Stroke
{
    public HashSet<Guid> Targets = [];
}

public class TextStroke : DrawStroke
{
    public string Text { get; set; } = string.Empty;
}