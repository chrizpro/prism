using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Prism.Controls;

/// <summary>bool -> 1.0 / 0.4 opacity.</summary>
public sealed class OpacityFor : IValueConverter
{
    public static readonly OpacityFor Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? 1.0 : 0.4;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>bool -> Bold / Normal.</summary>
public sealed class BoldFor : IValueConverter
{
    public static readonly BoldFor Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? FontWeight.Bold : FontWeight.Normal;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>uint 0xAARRGGBB -> SolidColorBrush.</summary>
public sealed class ArgbToBrush : IValueConverter
{
    public static readonly ArgbToBrush Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is uint c ? new SolidColorBrush(Color.FromUInt32(c)) : Brushes.Transparent;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
