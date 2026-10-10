using System.Globalization;
using Avalonia.Data.Converters;

namespace GamingHub.App.ViewModels;

public sealed class BoolToOpacityConverter : IValueConverter
{
    public double True { get; set; } = 1.0;
    public double False { get; set; } = 0.45;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? True : False;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class StringNotEmptyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrWhiteSpace(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public static class CommonConverters
{
    public static readonly BoolToOpacityConverter BadgeOpacity = new() { True = 1.0, False = 0.4 };
    public static readonly StringNotEmptyConverter NotEmpty = new();
}
