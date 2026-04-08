using SkiaSharp;

namespace Scribble.Utils;

/// <summary>
/// Builds an SKPath from text that may contain special characters like \n and \t.
/// Each \n starts a new line offset downward by the font's line height.
/// Each \t is expanded to 4 spaces.
/// </summary>
public static class TextPathBuilder
{
    private const string TabReplacement = "    "; // 4 spaces

    public static SKPath Build(string text, float xPos, float yPos, float textSize, SKTypeface typeface)
    {
        var combinedPath = new SKPath();
        using var paint = new SKPaint();
        paint.TextSize = textSize;
        paint.Typeface = typeface;

        // Replace tabs with spaces
        text = text.Replace("\t", TabReplacement);

        var lines = text.Split('\n');
        var lineHeight = textSize * 1.2f;

        for (var i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrEmpty(lines[i])) continue;

            var lineY = yPos + i * lineHeight;
            using var linePath = paint.GetTextPath(lines[i], xPos, lineY);
            combinedPath.AddPath(linePath);
        }

        return combinedPath;
    }
}