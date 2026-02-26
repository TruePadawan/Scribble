using Avalonia.Media;
using Scribble.Shared.Lib;

namespace Scribble.Lib;

public class ToolOptionsValues
{
    public float StrokeThickness { get; set; } = 1f;
    public Color StrokeColor { get; set; } = Colors.White;
    public StrokeStyle StrokeStyle { get; set; } = StrokeStyle.Solid;
    public float[]? DashIntervals { get; set; }
    public Color FillColor { get; set; } = Colors.Transparent;
    public EdgeType EdgeType { get; set; } = EdgeType.Sharp;
    public float FontSize { get; set; } = 10;
}
