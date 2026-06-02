using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Scribble.Converters;

/// <summary>
/// Converts a boolean to a brush, this is used to differentiate own messages (cyan) from others (green)
/// </summary>
public class BoolToBrushConverter : IValueConverter
{
    private static readonly IBrush OwnMessageBrush = new SolidColorBrush(Color.Parse("#00BFFF"));
    private static readonly IBrush OtherMessageBrush = new SolidColorBrush(Color.Parse("#7CFC00"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? OwnMessageBrush : OtherMessageBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}