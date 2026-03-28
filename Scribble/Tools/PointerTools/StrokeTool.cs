using System.Collections.Generic;
using Avalonia.Media.Imaging;
using Scribble.Services;
using Scribble.Shared.Lib;

namespace Scribble.Tools.PointerTools;

/// <summary>
/// Represents a Pointer Tool that draws visible strokes on the canvas
/// </summary>
/// <param name="name">The name of the tool</param>
/// <param name="icon">The bitmap representing the tool's icon</param>
public abstract class StrokeTool(string name, CanvasStateService canvasState, Bitmap icon)
    : PointerTool(name, canvasState, icon)
{
    public StrokePaint StrokePaint { get; protected init; } = new();
    public HashSet<ToolOption> ToolOptions { get; protected init; } = [];
}