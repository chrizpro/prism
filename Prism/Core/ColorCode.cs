using System;
using System.Globalization;
using System.Linq;

namespace Prism.Core;

/// <summary>Opaque sRGB color entry shared by the picker and exact-code fields.</summary>
public static class ColorCode
{
    public static uint FromHsv(double hue, double saturation, double value)
    {
        hue = (hue % 360 + 360) % 360; saturation = Math.Clamp(saturation, 0, 1); value = Math.Clamp(value, 0, 1);
        double c = value * saturation, x = c * (1 - Math.Abs(hue / 60 % 2 - 1)), m = value - c;
        var (r, g, b) = hue switch { < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x), < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x) };
        return Pack(r + m, g + m, b + m);
    }
    public static (double H, double S, double V) Hsv(uint color)
    {
        var (r, g, b) = Palette.Unpack(color); double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d < 1e-8 ? 0 : max == r ? 60 * ((g - b) / d % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        return ((h + 360) % 360, max == 0 ? 0 : d / max, max);
    }
    public static uint Pack(double r, double g, double b) => 0xFF000000 | (uint)Math.Round(Math.Clamp(r, 0, 1) * 255) << 16 | (uint)Math.Round(Math.Clamp(g, 0, 1) * 255) << 8 | (uint)Math.Round(Math.Clamp(b, 0, 1) * 255);
    public static bool TryParse(string? text, out uint color)
    {
        color = 0; if (string.IsNullOrWhiteSpace(text)) return false;
        string t = text.Trim().ToLowerInvariant();
        string hex = t.TrimStart('#');
        if (hex.Length == 3 && t.StartsWith('#')) hex = string.Concat(hex.Select(c => new string(c, 2)));
        if (hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb)) { color = 0xFF000000 | rgb; return true; }
        int paren = t.IndexOf('('); if (paren < 0 || !t.EndsWith(')')) return false;
        string mode = t[..paren];
        var parts = t[(paren + 1)..^1].Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 3) return false;
        var v = new double[3];
        for (int i = 0; i < 3; i++)
            if (!double.TryParse(parts[i].TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]) || !double.IsFinite(v[i])) return false;
        if (mode == "rgb")
        {
            for (int i = 0; i < 3; i++) { double max = parts[i].EndsWith('%') ? 100 : 255; if (v[i] < 0 || v[i] > max) return false; v[i] /= max; }
            color = Pack(v[0], v[1], v[2]); return true;
        }
        if (mode is not ("hsl" or "hsv") || v[1] < 0 || v[1] > 100 || v[2] < 0 || v[2] > 100) return false;
        double s = v[1] / 100, l = v[2] / 100;
        if (mode == "hsv") color = FromHsv(v[0], s, l);
        else { double value = l + s * Math.Min(l, 1 - l); color = FromHsv(v[0], value == 0 ? 0 : 2 * (1 - l / value), value); }
        return true;
    }
}
