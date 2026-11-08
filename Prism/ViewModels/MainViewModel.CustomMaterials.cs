using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Core;

namespace Prism.ViewModels;

public sealed partial class MaterialPropertyInput : ObservableObject
{
    public string Label { get; }
    [ObservableProperty] public partial string Text { get; set; }
    public MaterialPropertyInput(string label, double value) { Label = label; Text = value.ToString("G", CultureInfo.InvariantCulture); }
}

public sealed partial class MainViewModel
{
    System.Collections.Generic.IEnumerable<Material> AvailableMaterials => Materials.All.Concat(AppSettings.Current.CustomMaterials);
    Material _customMaterialTemplate = Materials.Default;
    public ObservableCollection<MaterialPropertyInput> CustomMaterialInputs { get; } = [];
    [ObservableProperty] public partial string CustomMaterialName { get; set; } = "";
    [ObservableProperty] public partial string CustomMaterialSource { get; set; } = "";
    [ObservableProperty] public partial string CustomMaterialStatus { get; set; } = "Choose a library material, then duplicate it to customize its properties.";
    [ObservableProperty] public partial bool EditingCustomMaterial { get; set; }

    public void BeginCustomMaterial()
    {
        _customMaterialTemplate = SelectedMaterialRow?.Material ?? TargetsForLook().FirstOrDefault()?.Material ?? Materials.Default;
        var m = _customMaterialTemplate;
        CustomMaterialName = m.Name + " copy"; CustomMaterialSource = "";
        CustomMaterialInputs.Clear();
        foreach (var (label, value) in new[] { ("Density (g/cm³)", m.Density), ("Young's modulus (GPa)", m.Youngs),
            ("Yield strength (MPa; NaN if inapplicable)", m.Yield), (m.Evidence.StrengthBasis + " (MPa)", m.Ultimate),
            ("Poisson's ratio", m.Poisson), ("Conductivity (W/(m·K))", m.Conductivity),
            ("Expansion (µm/(m·K))", m.Expansion), ("Max service temperature (°C)", m.MaxTemp), ("Specific heat (J/(kg·K))", m.SpecificHeat) })
            CustomMaterialInputs.Add(new(label, value));
        EditingCustomMaterial = true;
        CustomMaterialStatus = "Based on " + m.Name + ". Strength basis and solver model limitations are retained. Enter numbers with a decimal point.";
    }

    public void SaveCustomMaterial()
    {
        if (!EditingCustomMaterial) return;
        try
        {
            var v = CustomMaterialInputs.Select(p => double.Parse(p.Text, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
            var m = CustomMaterial.Create(_customMaterialTemplate with { Density = v[0], Youngs = v[1], Yield = v[2], Ultimate = v[3],
                Poisson = v[4], Conductivity = v[5], Expansion = v[6], MaxTemp = v[7], SpecificHeat = v[8] }, CustomMaterialName, CustomMaterialSource);
            if (AppSettings.Current.CustomMaterials.Any(x => string.Equals(x.Name, m.Name, StringComparison.OrdinalIgnoreCase)))
            { CustomMaterialStatus = "That custom name is already saved. Choose another name."; return; }
            AppSettings.Current.CustomMaterials.Add(m);
            if (!AppSettings.TrySave(false))
            { AppSettings.Current.CustomMaterials.Remove(m); CustomMaterialStatus = "Could not save custom materials to this computer. Check disk space and permissions."; return; }
            MaterialSearch = ""; MaterialCategoryIndex = MaterialCategories.Count - 1;
            RefreshMaterialFamilies();
            SelectedMaterialRow = MaterialList.First(r => r.Material.Id == m.Id);
            EditingCustomMaterial = false;
            CustomMaterialStatus = "Saved on this computer · User-defined — not verified by Prism. Select Apply to assign it.";
        }
        catch (Exception e) when (e is ArgumentException or FormatException or OverflowException)
        { CustomMaterialStatus = e is FormatException or OverflowException ? "Enter valid numbers with a decimal point; only yield may be NaN." : e.Message; }
    }

    public void RemoveCustomMaterial()
    {
        if (SelectedMaterialRow?.Material is not { IsCustom: true } m) return;
        var previous = AppSettings.Current.CustomMaterials.ToList();
        AppSettings.Current.CustomMaterials.RemoveAll(x => x.Id == m.Id);
        if (!AppSettings.TrySave(false))
        { AppSettings.Current.CustomMaterials = previous; CustomMaterialStatus = "Could not remove the saved custom material from this computer."; return; }
        RefreshMaterialFamilies();
        CustomMaterialStatus = "Removed from this computer. Assigned material snapshots stay in their documents.";
    }
}
