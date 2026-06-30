using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Scribble.Converters;

/// <summary>
/// Converts a boolean to a brush
/// </summary>
public class BoolToBrushConverter : IValueConverter
{
    public static readonly SolidColorBrush OwnMessageBrush = new(Color.Parse("#00BFFF"));
    public static readonly SolidColorBrush OtherMessageBrush = new(Color.Parse("#7CFC00"));
    public static readonly SolidColorBrush SentMessageBrush = new(Color.Parse("#FFFFFF"));
    public static readonly SolidColorBrush PendingMessageBrush = new(Color.Parse("#808080"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is string status && value is bool statusValue)
        {
            switch (status.ToLowerInvariant())
            {
                case "ownershipstatus":
                    return statusValue ? OwnMessageBrush : OtherMessageBrush;
                case "sentstatus":
                    return statusValue ? SentMessageBrush : PendingMessageBrush;
            }
        }

        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}