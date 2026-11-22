using System.Collections.Generic;
using System.Linq;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.CanvasElements.Strokes;

namespace Scribble.Services.CanvasStateService;

/// <summary>
/// Compare document fields after replay, ignoring transient selection, caches and clone timestamps.
/// </summary>
internal static class CanvasContent
{
    /// <summary>
    /// Compare elements in their materialized drawing order.
    /// </summary>
    internal static bool Equals(IReadOnlyList<CanvasElement> before, IReadOnlyList<CanvasElement> after)
    {
        if (before.Count != after.Count) return false;
        for (var index = 0; index < before.Count; index++)
        {
            if (!SameElement(before[index], after[index])) return false;
        }

        return true;
    }

    /// <summary>
    /// Match image placement or stroke geometry and styling as saved by DocumentService.
    /// </summary>
    private static bool SameElement(CanvasElement a, CanvasElement b)
    {
        if (a.Id != b.Id || a.LayerIndex != b.LayerIndex) return false;
        if (a is CanvasImage i && b is CanvasImage j)
            return i.Bounds == j.Bounds && i.Rotation == j.Rotation && i.FlipX == j.FlipX &&
                   i.FlipY == j.FlipY && i.ImageBase64String == j.ImageBase64String;
        if (a is not PaintableStroke x || b is not PaintableStroke y) return false;
        if (x.Rotation != y.Rotation || !x.TransformMatrix.Equals(y.TransformMatrix) ||
            x.CreatorConnectionId != y.CreatorConnectionId || !x.ToolOptions.SetEquals(y.ToolOptions) ||
            !SamePaint(x.Paint, y.Paint) || x.Path.ToSvgPathData() != y.Path.ToSvgPathData()) return false;

        return (x, y) switch
        {
            (DrawStroke d, DrawStroke e) => d.ToolType == e.ToolType &&
                                            (d.SerializedRawPoints ?? []).SequenceEqual(e.SerializedRawPoints ?? []),
            (TextStroke t, TextStroke u) => t.Text == u.Text && t.Position == u.Position &&
                                            t.IsBold == u.IsBold && t.IsItalic == u.IsItalic,
            _ => false
        };
    }

    /// <summary>
    /// Compare persistent paint values independently of cached Skia objects.
    /// </summary>
    private static bool SamePaint(StrokePaint a, StrokePaint b) =>
        a.Color == b.Color && a.FillColor == b.FillColor && a.StrokeWidth == b.StrokeWidth &&
        a.TextSize == b.TextSize && a.StrokeJoin == b.StrokeJoin && a.StrokeCap == b.StrokeCap &&
        a.IsStroke == b.IsStroke && a.IsAntialias == b.IsAntialias &&
        (a.DashIntervals ?? []).SequenceEqual(b.DashIntervals ?? []);
}