namespace Prism.Core;

/// <summary>Conversions between user-facing lengths and Prism's millimeter geometry coordinates.</summary>
public enum LengthUnit { Millimeter, Inch }

public static class LengthUnits
{
    public const double MillimetersPerInch = 25.4;

    public static double ToMillimeters(double value, LengthUnit unit) => unit switch
    {
        LengthUnit.Millimeter => value,
        LengthUnit.Inch => value * MillimetersPerInch,
        _ => throw new System.ArgumentOutOfRangeException(nameof(unit))
    };

    public static double FromMillimeters(double value, LengthUnit unit) => unit switch
    {
        LengthUnit.Millimeter => value,
        LengthUnit.Inch => value / MillimetersPerInch,
        _ => throw new System.ArgumentOutOfRangeException(nameof(unit))
    };
}
