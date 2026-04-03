namespace Scribble.Shared.Lib;

public enum ToolType
{
    Pencil,
    Line,
    Arrow,
    Ellipse,
    Rectangle,
    [Obsolete("Use TextStroke instead of DrawStroke with ToolType.Text")]
    Text
}