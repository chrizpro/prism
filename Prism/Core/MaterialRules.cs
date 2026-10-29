using System;
namespace Prism.Core;
public static class MaterialRules
{
    public static void Structural(Material material)
    {
        if(!material.Evidence.IsotropicStructural) throw new InvalidOperationException($"{material.Name} requires a directional or nonlinear material model that this structural solver does not support.");
        if(!double.IsFinite(material.Density)||material.Density<=0||!double.IsFinite(material.Youngs)||material.Youngs<=0||!double.IsFinite(material.Poisson)||material.Poisson<=-1||material.Poisson>=.5)
            throw new InvalidOperationException("The material needs positive finite density/modulus and a Poisson ratio between −1 and 0.5.");
    }
    public static void Thermal(Material material)
    {
        if(!material.Evidence.IsotropicThermal)throw new InvalidOperationException($"{material.Name} needs directional thermal properties; the current conduction model is isotropic.");
        if(!double.IsFinite(material.Density)||material.Density<=0||!double.IsFinite(material.Conductivity)||material.Conductivity<=0||!double.IsFinite(material.SpecificHeat)||material.SpecificHeat<=0)
            throw new InvalidOperationException("The material needs positive finite density, conductivity and heat capacity.");
    }
}
