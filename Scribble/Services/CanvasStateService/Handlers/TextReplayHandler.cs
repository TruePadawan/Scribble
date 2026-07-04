using System;
using Scribble.Services.CanvasStateService.Context;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements.Strokes;
using Scribble.Utils;
using SkiaSharp;

namespace Scribble.Services.CanvasStateService.Handlers;

/// <summary>
/// Handles replay for text-related events:
/// AddTextEvent, UpdateTextEvent, UpdateFontSizeEvent, UpdateFontCasingEvent, UpdateFontStyleEvent
/// </summary>
public class TextReplayHandler :
    IEventReplayHandler<AddTextEvent>,
    IEventReplayHandler<UpdateTextEvent>,
    IEventReplayHandler<UpdateFontSizeEvent>,
    IEventReplayHandler<UpdateFontCasingEvent>,
    IEventReplayHandler<UpdateFontStyleEvent>
{
    public void Replay(AddTextEvent ev, ReplayContext ctx)
    {
        var textPath = new SKPath();
        textPath.MoveTo(ev.Position);
        textPath.AddPath(
            TextPathBuilder.Build(ev.Text, ev.Position.X, ev.Position.Y, ev.Paint.TextSize,
                StrokePaint.DefaultTypeFace));
        ctx.PaintableStrokes[ev.StrokeId] = new TextStroke
        {
            Id = ev.StrokeId,
            Paint = ev.Paint.Clone(),
            Path = textPath,
            ToolOptions = ev.ToolOptions,
            Text = ev.Text,
            Position = ev.Position,
            CreatorConnectionId = ev.CreatorConnectionId,
            LayerIndex = ctx.MaxLayerIndex
        };
    }

    public void Replay(UpdateTextEvent ev, ReplayContext ctx)
    {
        if (ctx.PaintableStrokes.TryGetValue(ev.TextStrokeId, out var existingStroke) &&
            existingStroke is TextStroke textStroke)
        {
            textStroke.Text = ev.NewText;
            var updateTextTypeface = SKTypeface.FromFamilyName(
                StrokePaint.DefaultTypeFace.FamilyName, textStroke.SkFontStyle);
            var newTextPath = new SKPath();
            newTextPath.MoveTo(textStroke.Position);
            newTextPath.AddPath(
                TextPathBuilder.Build(ev.NewText, textStroke.Position.X, textStroke.Position.Y,
                    textStroke.Paint.TextSize, updateTextTypeface));

            newTextPath.Transform(textStroke.TransformMatrix);

            textStroke.Path.Reset();
            textStroke.Path.AddPath(newTextPath);
        }
    }

    public void Replay(UpdateFontSizeEvent ev, ReplayContext ctx)
    {
        foreach (var strokeId in ev.StrokeIds)
        {
            if (ctx.PaintableStrokes.TryGetValue(strokeId, out var stroke) && stroke is TextStroke ts)
            {
                var fontSizeTypeface = SKTypeface.FromFamilyName(
                    StrokePaint.DefaultTypeFace.FamilyName, ts.SkFontStyle);
                var noTransformTextPath = new SKPath();
                noTransformTextPath.MoveTo(ts.Position);
                noTransformTextPath.AddPath(
                    TextPathBuilder.Build(ts.Text, ts.Position.X, ts.Position.Y, ev.FontSize,
                        fontSizeTypeface));
                noTransformTextPath.Transform(ts.TransformMatrix);
                ts.Path.Reset();
                ts.Path.AddPath(noTransformTextPath);
            }
        }
    }

    public void Replay(UpdateFontCasingEvent ev, ReplayContext ctx)
    {
        foreach (var strokeId in ev.TextStrokeIds)
        {
            if (ctx.PaintableStrokes.TryGetValue(strokeId, out var stroke) && stroke is TextStroke ts)
            {
                ts.Text = ev.NewCasing == FontCasing.UpperCase ? ts.Text.ToUpper() : ts.Text.ToLower();

                // Recreate stroke paths
                var casingTypeface = SKTypeface.FromFamilyName(
                    StrokePaint.DefaultTypeFace.FamilyName, ts.SkFontStyle);
                var newTextPath = new SKPath();
                newTextPath.MoveTo(ts.Position);
                newTextPath.AddPath(
                    TextPathBuilder.Build(ts.Text, ts.Position.X, ts.Position.Y,
                        ts.Paint.TextSize, casingTypeface));

                newTextPath.Transform(ts.TransformMatrix);

                ts.Path.Reset();
                ts.Path.AddPath(newTextPath);
            }
        }
    }

    public void Replay(UpdateFontStyleEvent ev, ReplayContext ctx)
    {
        foreach (var strokeId in ev.TextStrokeIds)
        {
            if (ctx.PaintableStrokes.TryGetValue(strokeId, out var stroke) && stroke is TextStroke ts)
            {
                if (ev.NewStyle == FontStyle.Normal)
                {
                    // resets both bold and italic
                    ts.IsBold = false;
                    ts.IsItalic = false;
                }
                else if (ev.NewStyle == FontStyle.Bold)
                {
                    // Toggle bold
                    ts.IsBold = !ts.IsBold;
                }
                else if (ev.NewStyle == FontStyle.Italic)
                {
                    // Toggle italic
                    ts.IsItalic = !ts.IsItalic;
                }

                // Derive typeface from the updated bold/italic state
                var newTypeFace = SKTypeface.FromFamilyName(
                    StrokePaint.DefaultTypeFace.FamilyName, ts.SkFontStyle);

                // Recreate stroke paths
                var newTextPath = new SKPath();
                newTextPath.MoveTo(ts.Position);
                newTextPath.AddPath(
                    TextPathBuilder.Build(ts.Text, ts.Position.X, ts.Position.Y,
                        ts.Paint.TextSize, newTypeFace));

                newTextPath.Transform(ts.TransformMatrix);

                ts.Path.Reset();
                ts.Path.AddPath(newTextPath);
            }
        }
    }
}
