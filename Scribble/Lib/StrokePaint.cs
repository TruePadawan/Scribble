using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace Scribble.Lib;

public class StrokePaint
{
    public bool IsAntialias { get; init; }
    public bool IsStroke { get; init; }
    public SKStrokeCap StrokeCap { get; init; }
    public SKStrokeJoin StrokeJoin { get; set; }
    public float StrokeWidth { get; set; } = 1;
    public float TextSize { get; set; }
    public float[]? DashIntervals { get; set; }

    [JsonConverter(typeof(SKColorJsonConverter))]
    public SKColor Color { get; set; } = SKColors.Red;

    [JsonConverter(typeof(SKColorJsonConverter))]
    public SKColor FillColor { get; set; } = SKColors.Transparent;

    [JsonIgnore]
    private SKPathEffect? PathEffect
    {
        get
        {
            if (DashIntervals != null && DashIntervals.Length > 0)
            {
                return SKPathEffect.CreateDash(DashIntervals, 0);
            }

            return null;
        }
    }

    public StrokePaint Clone()
    {
        return (StrokePaint)MemberwiseClone();
    }

    public SKPaint ToSkPaint()
    {
        var paint = new SKPaint
        {
            IsAntialias = this.IsAntialias,
            IsStroke = this.IsStroke,
            StrokeCap = this.StrokeCap,
            StrokeJoin = this.StrokeJoin,
            StrokeWidth = this.StrokeWidth,
            Color = this.Color,
            TextSize = this.TextSize,
            PathEffect = this.PathEffect
        };
        return paint;
    }
}

public class SKColorJsonConverter : JsonConverter<SKColor>
{
    public override SKColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var hexString = reader.GetString();
        return SKColor.Parse(hexString);
    }

    public override void Write(Utf8JsonWriter writer, SKColor value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}