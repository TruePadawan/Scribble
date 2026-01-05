using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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
public abstract class PointerToolsBase(string name, MainViewModel viewModel, IImage icon)
{
    public string Name { get; } = name;
    protected MainViewModel ViewModel { get; } = viewModel;
    public readonly IImage ToolIcon = icon;
    public IImage? CursorIcon;

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

    // Derived classes should override this and render their options in the panel
    // Should return true if the tool has options to render
    public virtual bool RenderOptions(Panel parent)
    {
        return false;
    }

    protected StackPanel CreateOptionControl(Control actualControl, string optionLabel)
    {
        var stackPanel = new StackPanel
        {
            Margin = new Thickness(8),
            Spacing = 2
        };
        stackPanel.Children.Add(new Label { Content = optionLabel });
        stackPanel.Children.Add(actualControl);
        return stackPanel;
    }
}