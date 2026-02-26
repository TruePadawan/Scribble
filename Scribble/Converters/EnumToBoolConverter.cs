using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Scribble.Converters;

public class EnumToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.Equals(parameter) ?? false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true && parameter != null ? parameter : BindingOperations.DoNothing;
    }
}
