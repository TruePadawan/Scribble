using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Scribble.ViewModels;

namespace Scribble.Tools.PointerTools;

/**
 * Base class for all Pointer Tools
 * It enforces the data that all Pointer Tools should have
 * Name - The name of the Tool
 * Icon - The icon that represents the tool in the UI
 */
public abstract class PointerTool(string name, MainViewModel viewModel, Bitmap icon)
{
    public string Name { get; } = name;
    protected MainViewModel ViewModel { get; } = viewModel;
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

    public virtual void HandlePointerMove(Point prevCoord, Point currentCoord)
    {
    }

    public virtual void HandlePointerClick(Point coord)
    {
    }

    public virtual void HandlePointerRelease(Point prevCoord, Point currentCoord)
    {
    }

    // This is called when the active tool is swapped out
    public virtual void Dispose()
    {
    }
}