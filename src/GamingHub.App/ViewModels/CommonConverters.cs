using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using GamingHub.Core.Services;

namespace GamingHub.App.ViewModels;

public sealed class BoolToAlignmentConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BoolToCommandConverter : IValueConverter
{
    public object TrueCommand { get; set; } = null!;
    public object FalseCommand { get; set; } = null!;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? TrueCommand : FalseCommand;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BoolToBrushConverter : IValueConverter
{
    public string True { get; set; } = "#2563EB";
    public string False { get; set; } = "#1B1F3A";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => new SolidColorBrush(Color.Parse(value is true ? True : False));

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BoolToTextConverter : IValueConverter
{
    public string True { get; set; } = "True";
    public string False { get; set; } = "False";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is string param && param.Contains(':'))
        {
            var parts = param.Split(':');
            True = parts[0];
            False = parts[1];
        }
        return value is true ? True : False;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BoolToColorConverter : IValueConverter
{
    public string True { get; set; } = "#4ADE80";
    public string False { get; set; } = "#F87171";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is string param && param.Contains(':'))
        {
            var parts = param.Split(':');
            True = parts[0];
            False = parts[1];
        }
        return Color.Parse(value is true ? True : False);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class ColorToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is RgbColor rgb)
            return new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B));
        if (value is Color c)
            return new SolidColorBrush(c);
        return new SolidColorBrush(Colors.Transparent);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class RgbTypeToGlyphConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is RgbDeviceType type)
            return type switch
            {
                RgbDeviceType.Mouse => "🖱",
                RgbDeviceType.Keyboard => "⌨",
                RgbDeviceType.Fan => "🌀",
                RgbDeviceType.Gpu => "🎮",
                RgbDeviceType.Motherboard => "🖥",
                RgbDeviceType.Ram => "🧠",
                RgbDeviceType.LedStrip => "💡",
                _ => "💡"
            };
        return "💡";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

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

/// <summary>Wandelt eine Reihe von Prozentwerten (0–100) in die Points eines
/// Polyline-Diagramms um (x = Index, y = 100 − Wert).</summary>
public sealed class SeriesToPointsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var points = new Points();
        if (value is not IEnumerable<double> series)
            return points;

        var values = series.ToList();
        if (values.Count == 0)
            return points;

        for (int i = 0; i < values.Count; i++)
        {
            double y = Math.Clamp(values[i], 0, 100);
            points.Add(new Point(i, 100 - y));
        }
        return points;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public static class CommonConverters
{
    public static readonly BoolToOpacityConverter BadgeOpacity = new() { True = 1.0, False = 0.4 };
    public static readonly StringNotEmptyConverter NotEmpty = new();
    public static readonly BoolToAlignmentConverter MessageAlign = new();
    public static readonly BoolToBrushConverter MessageBrush = new() { True = "#2563EB", False = "#1B1F3A" };
    public static readonly BoolToColorConverter BoolToColor = new();
    public static readonly BoolToCommandConverter BoolToCommand = new();
    public static readonly SeriesToPointsConverter SeriesToPoints = new();
    public static readonly ColorToBrushConverter ColorToBrush = new();
    public static readonly RgbTypeToGlyphConverter RgbTypeToGlyph = new();
    public static readonly BoolToTextConverter BoolToText = new();
}
