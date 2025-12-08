using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Scribble.ViewModels;

namespace Scribble.Tools.PointerTools;

/**
 * Base class for all Pointer Tools
 * It enforces the data that all Pointer Tools should have
 * Name - The name of the Tool
 * Icon - The icon that represents the tool in the UI
 * Every PointerTool should consume the ViewModel to perform its operation
 */
public abstract class PointerToolsBase(string name, MainViewModel viewModel, IImage icon)
{
    public string Name { get; } = name;
    protected MainViewModel ViewModel { get; } = viewModel;
    public readonly IImage ToolIcon = icon;

    public abstract void HandlePointerMove(Point prevCoord, Point currentCoord);

    public abstract void HandlePointerClick(Point coord);

    // Derived classes should override this and render their options in the panel
    public abstract void RenderOptions(Panel parent);

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