namespace Prism.Core;

/// <summary>A reusable visual recipe, deliberately independent of physical material identity.</summary>
public sealed record SavedAppearance(string Name, uint Color, float Metal, float Gloss,
    SurfacePattern Pattern, float PatternScale, float PatternAngle, SurfaceStyle Surface)
{
    public string DisplayName => Name + " · Custom";
    public static SavedAppearance Capture(string name, uint color, Appearance look) =>
        new(name.Trim(), color, look.Metal, look.Gloss, look.Pattern, look.PatternScale,
            look.PatternAngle, (look.Surface ?? new()) with { Override = true });

    public Appearance ApplyTo(Appearance target) => target with
    {
        Metal = Metal, Gloss = Gloss, Pattern = Pattern, PatternScale = PatternScale,
        PatternAngle = PatternAngle, Surface = Surface.Validated() with { Override = true }
    };
}
