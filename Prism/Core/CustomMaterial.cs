using System;

namespace Prism.Core;

/// <summary>Creates user-defined snapshots without transferring Prism's source-check claims.</summary>
public static class CustomMaterial
{
    public static Material Create(Material template, string name, string source)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80)
            throw new ArgumentException("Enter a material name of 1–80 characters.");
        if (!double.IsFinite(template.Density) || template.Density <= 0 ||
            !double.IsFinite(template.Youngs) || template.Youngs <= 0 ||
            !double.IsFinite(template.Poisson) || template.Poisson <= -1 || template.Poisson >= .5 ||
            !(double.IsNaN(template.Yield) || double.IsFinite(template.Yield) && template.Yield > 0) ||
            !double.IsFinite(template.Ultimate) || template.Ultimate <= 0 ||
            !double.IsFinite(template.Conductivity) || template.Conductivity <= 0 ||
            !double.IsFinite(template.Expansion) || !double.IsFinite(template.MaxTemp) || template.MaxTemp <= -273.15 ||
            !double.IsFinite(template.SpecificHeat) || template.SpecificHeat <= 0)
            throw new ArgumentException("Use positive density, modulus, strengths, conductivity and heat capacity; −1 < Poisson < 0.5; temperature > −273.15 °C. Yield may be NaN when inapplicable. All other values must be finite.");
        return template with
        {
            Id = "custom:" + Guid.NewGuid().ToString("N"), Name = name.Trim(), Category = "Custom",
            DefaultAppearance = SavedAppearance.Capture(name.Trim(), template.Color,
                template.Look with { Pattern = Rendering.MaterialSurface.Pattern(template.Look) }),
            Evidence = template.Evidence with
            {
                Revision = "User-defined", Source = string.IsNullOrWhiteSpace(source) ? "No user source recorded" : source.Trim(),
                CheckedProperties = "User-defined — not verified by Prism",
                Condition = "User-entered properties. Model limitations and strength basis inherited from " + template.Name + ". " + template.Evidence.Condition
            }
        };
    }
}
