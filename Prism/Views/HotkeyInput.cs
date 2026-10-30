using Avalonia.Input;

namespace Prism.Views;

/// <summary>Turns a key press into the text form the shortcut catalogue uses ("Ctrl+Shift+S").</summary>
public static class HotkeyInput
{
    /// <summary>Null while only a modifier (Ctrl, Shift, Alt) is held: there is no shortcut to speak of yet.</summary>
    public static string? Gesture(KeyEventArgs e) => Gesture(e.Key, e.KeyModifiers);

    public static string? Gesture(Key key, KeyModifiers modifiers)
    {
        if (key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System) return null;
        string name = key switch
        {
            Key.Add => "OemPlus", Key.Subtract => "OemMinus", Key.Decimal => "OemPeriod",
            >= Key.NumPad0 and <= Key.NumPad9 => "D" + (key - Key.NumPad0),
            _ => key.ToString(),
        };
        string prefix = "";
        if (modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta)) prefix += "Ctrl+";
        if (modifiers.HasFlag(KeyModifiers.Alt)) prefix += "Alt+";
        if (modifiers.HasFlag(KeyModifiers.Shift)) prefix += "Shift+";
        return prefix + name;
    }
}
