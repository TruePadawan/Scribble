using System;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Scribble.Services.CanvasStateService;
using SkiaSharp;

namespace Scribble.Tools.PointerTools;

/// <summary>
/// Base class for all Pointer Tools
/// </summary>
/// <param name="name">The name of the tool</param>
/// <param name="icon">The bitmap representing the tool's icon</param>
public abstract class PointerTool(string name, ICanvasStateService canvasStateService, Bitmap icon)
{
    public string Name { get; } = name;
    protected ICanvasStateService CanvasStateService { get; } = canvasStateService;
    public Bitmap ToolIcon { get; } = icon;
    public Cursor? Cursor { get; protected init; }
    public KeyGesture? HotKey { get; protected init; }
    public string ToolTip { get; protected init; } = name;

    /// <summary>
    /// Loads a bitmap relative to the tool's folder.
    /// Example: If the class is in Scribble.Tools.PanningTool, it looks for Scribble/Tools/PanningTool/filename
    /// </summary>
    protected static Bitmap LoadToolBitmap(Type toolType, string filename)
    {
        // Converts "Scribble.Tools.PointerTools.PanningTool" to "Scribble/Tools/PointerTools/PanningTool"
        var assetPath = toolType.Namespace?.Replace('.', '/') ?? "";
        var uri = new Uri($"avares://{assetPath}/{filename}");

        return new Bitmap(AssetLoader.Open(uri));
    }

    public virtual void HandlePointerMove(SKPoint prevCoord, SKPoint currentCoord)
    {
    }

    public virtual void HandlePointerClick(SKPoint coord)
    {
    }

    public virtual void HandleDoubleClick(SKPoint coord)
    {
    }

    public virtual void HandlePointerRelease(SKPoint prevCoord, SKPoint currentCoord)
    {
    }

    /// <summary>
    /// Handles keyboard input while this tool is active.
    /// Return true if the key was consumed and should not propagate further.
    /// </summary>
    public virtual bool HandleKeyPress(Key key)
    {
        return false;
    }

    /// <summary>
    /// Indicates whether the tool is currently in an active, multi-step drawing operation.
    /// When true, the view will forward pointer-move events even when the pointer button is not pressed.
    /// </summary>
    public virtual bool IsDrawing => false;

    /// <summary>
    /// This is called when the tool is switched in
    /// </summary>
    public virtual void HandleToolSwitchIn()
    {
    }

    /// <summary>
    /// This is called when the tool is switched out
    /// </summary>
    public virtual void HandleToolSwitchOut()
    {
    }
}