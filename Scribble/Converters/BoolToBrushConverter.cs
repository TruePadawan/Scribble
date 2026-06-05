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
    private static readonly IBrush OwnMessageBrush = new SolidColorBrush(Color.Parse("#00BFFF"));
    private static readonly IBrush OtherMessageBrush = new SolidColorBrush(Color.Parse("#7CFC00"));
    private static readonly IBrush SentMessageBrush = new SolidColorBrush(Color.Parse("#FFFFFF"));
    private static readonly IBrush PendingMessageBrush = new SolidColorBrush(Color.Parse("#808080"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is string status && value is bool statusValue)
        {
            switch (status)
            {
                case "ownershipStatus":
                    return statusValue ? OwnMessageBrush : OtherMessageBrush;
                case "sentStatus":
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