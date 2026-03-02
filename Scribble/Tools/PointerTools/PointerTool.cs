using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Scribble.ViewModels;

namespace Scribble.Tools.PointerTools;

/// <summary>
/// Base class for all Pointer Tools
/// </summary>
/// <param name="name">The name of the tool</param>
/// <param name="icon">The bitmap representing the tool's icon</param>
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

    /// <summary>
    /// This is called when the tool is switched out
    /// </summary>
    public virtual void Dispose()
    {
    }
}