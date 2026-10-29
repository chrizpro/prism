using Prism.Core;

static void Check(double actual, double expected)
{
    if (Math.Abs(actual - expected) > 1e-10)
        throw new Exception($"Expected {expected}, got {actual}.");
}

Check(LengthUnits.ToMillimeters(2, LengthUnit.Inch), 50.8);
Check(LengthUnits.FromMillimeters(50.8, LengthUnit.Inch), 2);
Check(LengthUnits.ToMillimeters(-12.5, LengthUnit.Millimeter), -12.5);
Check(LengthUnits.FromMillimeters(-12.5, LengthUnit.Millimeter), -12.5);
try
{
    LengthUnits.ToMillimeters(1, (LengthUnit)99);
    throw new Exception("An unknown unit must be rejected.");
}
catch (ArgumentOutOfRangeException) { }
Console.WriteLine("Length conversion checks passed.");
