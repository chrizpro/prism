using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Core;

namespace Prism.ViewModels;

/// <summary>One row of the material list.</summary>
public sealed class MaterialRow
{
    public required Material Material { get; init; }
    public string Name => Material.DisplayName;
    public string Category => Material.Category;
    public uint Color => Material.Color;
    public string Summary { get; init; } = "";
}

/// <summary>One family chip above the material list: pick a family (Steel …) to see just its grades.</summary>
public sealed partial class MaterialFamilyRow : ObservableObject
{
    public required int Index { get; init; }              // index into MaterialCategories (0 = All)
    public required string Name { get; init; }
    public required int Count { get; init; }
    public required uint Color { get; init; }
    [ObservableProperty] public partial bool IsActive { get; set; }
    public string Tip => Index == 0 ? "Every material in the library" : $"All {Count} {Name.ToLowerInvariant()} grades and types";
}

/// <summary>A property in the detail card; Bar (0..1) is its size relative to the whole library.</summary>
public sealed class MatPropLine
{
    public string Label { get; init; } = "";
    public string Value { get; init; } = "";
    public string Hint { get; init; } = "";
    public double Bar { get; init; }
    public bool HasBar { get; init; }
    public double BarWidth => Bar * 150;
}

public sealed class CompareRow
{
    public string Label { get; init; } = "";
    public string A { get; init; } = "";
    public string B { get; init; } = "";
    public string C { get; init; } = "";
    public bool IsHeader { get; init; }
}

/// <summary>The Materials workspace: browse the library, read engineering data, compare, and apply a material and finish to bodies.</summary>
public sealed partial class MainViewModel
{
    static readonly string[] SortNames =
        ["Name", "Lightest", "Densest", "Strongest", "Stiffest", "Strength-to-weight", "Stiffness-to-weight", "Highest temperature", "Best conductor", "Cheapest"];
    static readonly uint[] FinishColors =
    [
        0xFFB9C6D6, 0xFFD8C3A5, 0xFFA8CBB5, 0xFFCDB4DB, 0xFFE3B7A0, 0xFF9FC5E8, 0xFFE5484D, 0xFFF59E0B,
        0xFF1E9E68, 0xFF2F78E4, 0xFF7B8AA0, 0xFF2F3E52, 0xFFF4F4F0, 0xFF22252A, 0xFFD2A94A, 0xFFC87941,
    ];
    public IReadOnlyList<uint> Swatches => FinishColors;

    public ObservableCollection<MaterialRow> MaterialList { get; } = [];
    public ObservableCollection<MatPropLine> MaterialProps { get; } = [];
    public ObservableCollection<CompareRow> CompareRows { get; } = [];
    public ObservableCollection<MaterialFamilyRow> MaterialFamilies { get; } = [];
    public IReadOnlyList<string> MaterialCategories { get; } = ["All", .. Materials.Categories, "Custom"];
    public IReadOnlyList<string> MaterialSorts => SortNames;
    readonly List<Material> _compare = [];

    [ObservableProperty] public partial bool ShowMaterials { get; set; }
    [ObservableProperty] public partial string MaterialSearch { get; set; } = "";
    [ObservableProperty] public partial int MaterialCategoryIndex { get; set; }
    [ObservableProperty] public partial int MaterialSortIndex { get; set; }
    [ObservableProperty] public partial MaterialRow? SelectedMaterialRow { get; set; }
    [ObservableProperty] public partial bool HasMaterial { get; set; }
    [ObservableProperty] public partial string MatName { get; set; } = "";
    [ObservableProperty] public partial string MatCategory { get; set; } = "";
    [ObservableProperty] public partial uint MatColor { get; set; } = 0xFF8A9099;
    [ObservableProperty] public partial string MatUses { get; set; } = "";
    [ObservableProperty] public partial string MatWatch { get; set; } = "";
    [ObservableProperty] public partial string MatEvidence { get; set; } = "";
    [ObservableProperty] public partial string MatDataStatus { get; set; } = "";
    [ObservableProperty] public partial string AssignedData { get; set; } = "";
    [ObservableProperty] public partial bool HasAssignedData { get; set; }
    [ObservableProperty] public partial string MatMassNote { get; set; } = "";
    [ObservableProperty] public partial string MatTarget { get; set; } = "Select a body, then press Apply.";
    [ObservableProperty] public partial bool HasCompare { get; set; }
    [ObservableProperty] public partial string CompareButtonText { get; set; } = "Add to compare";
    [ObservableProperty] public partial string MatResultCount { get; set; } = "";

    // finish of the selected bodies
    [ObservableProperty] public partial double LookMetal { get; set; }
    [ObservableProperty] public partial double LookGloss { get; set; } = 0.35;
    public IReadOnlyList<string> SurfacePatterns { get; } = ["From material","No pattern","Wood grain","Brushed metal","Cast finish","Carbon weave"];
    [ObservableProperty] public partial int LookPattern { get; set; }
    [ObservableProperty] public partial double LookPatternScale { get; set; } = 12;
    [ObservableProperty] public partial double LookPatternAngle { get; set; }
    partial void OnLookPatternChanged(int value) { PushLook(); OnPropertyChanged(nameof(AppearancePattern)); OnPropertyChanged(nameof(ActiveFinishPreset)); }
    partial void OnLookPatternScaleChanged(double value)=>PushLook();
    partial void OnLookPatternAngleChanged(double value)=>PushLook();
    [ObservableProperty] public partial string ColorHex { get; set; } = "";
    [ObservableProperty] public partial string AssignedText { get; set; } = "No material assigned";
    bool _syncingLook;

    Ribbon.RibbonTab? _materialsTab;
    [ObservableProperty] public partial bool ShowMaterialsPanel { get; set; }
    partial void OnShowMaterialsPanelChanged(bool value) { Session.AppearancePreview = value; Session.Invalidate(); }

    public void SetFinish(double metal, double gloss, SurfacePattern? pattern = null) { LookMetal = metal; LookGloss = gloss; if(pattern.HasValue)LookPattern=(int)pattern.Value; }

    void HookMaterials() => RefreshMaterialFamilies();

    void RefreshMaterialFamilies()
    {
        MaterialFamilies.Clear();
        MaterialFamilies.Add(new MaterialFamilyRow { Index = 0, Name = "All", Count = AvailableMaterials.Count(), Color = 0xFF8A9099, IsActive = MaterialCategoryIndex == 0 });
        for (int i = 1; i < MaterialCategories.Count; i++)
        {
            var cat = MaterialCategories[i];
            MaterialFamilies.Add(new MaterialFamilyRow { Index = i, Name = cat, Count = AvailableMaterials.Count(m => m.Category == cat), Color = AvailableMaterials.FirstOrDefault(m => m.Category == cat)?.Color ?? 0xFF8A9099, IsActive = MaterialCategoryIndex == i });
        }
        RefreshMaterialList();
    }

    /// <summary>Pick a family chip (0 = All): the list below shows just that family's grades/types.</summary>
    public void SelectFamily(int index)
    {
        if (index >= 0 && index < MaterialCategories.Count) MaterialCategoryIndex = index;
    }

    partial void OnMaterialSearchChanged(string value) => RefreshMaterialList();
    partial void OnMaterialCategoryIndexChanged(int value)
    {
        foreach (var f in MaterialFamilies) f.IsActive = f.Index == value;
        RefreshMaterialList();
    }
    partial void OnMaterialSortIndexChanged(int value) => RefreshMaterialList();
    partial void OnSelectedMaterialRowChanged(MaterialRow? value) { ShowMaterial(value?.Material); RefreshRibbon(); }
    partial void OnLookMetalChanged(double value) { PushLook(); OnPropertyChanged(nameof(ActiveFinishPreset)); }
    partial void OnLookGlossChanged(double value) { OnPropertyChanged(nameof(LookRoughness)); PushLook(); OnPropertyChanged(nameof(ActiveFinishPreset)); }

    static string Rating(int n) => n == 0 ? "Not specified" : new string('●', Math.Clamp(n, 0, 5)) + new string('○', 5 - Math.Clamp(n, 0, 5));
    static string F(double v, string fmt = "#,0.##") => double.IsNaN(v) ? "-" : v.ToString(fmt, CultureInfo.InvariantCulture);

    void RefreshMaterialList()
    {
        IEnumerable<Material> q = AvailableMaterials;
        var cat = MaterialCategoryIndex > 0 && MaterialCategoryIndex < MaterialCategories.Count ? MaterialCategories[MaterialCategoryIndex] : null;
        if (cat != null) q = q.Where(m => m.Category == cat);
        var terms = MaterialSearch.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var t in terms) q = q.Where(m => $"{m.Name} {m.Category} {m.Uses}".Contains(t, StringComparison.OrdinalIgnoreCase));
        q = MaterialSortIndex switch
        {
            1 => q.OrderBy(m => m.Density),
            2 => q.OrderByDescending(m => m.Density),
            3 => q.OrderByDescending(m => m.Ultimate),
            4 => q.OrderByDescending(m => m.Youngs),
            5 => q.OrderByDescending(m => m.SpecificStrength),
            6 => q.OrderByDescending(m => m.SpecificStiffness),
            7 => q.OrderByDescending(m => m.MaxTemp),
            8 => q.OrderByDescending(m => m.Conductivity),
            9 => q.OrderBy(m => m.Cost).ThenBy(m => m.Name),
            _ => q.OrderBy(m => m.Name),
        };
        string Sum(Material m) => MaterialSortIndex switch
        {
            1 or 2 => $"{m.Density:0.##} g/cm³",
            3 => $"{m.Ultimate:0.#} MPa tensile",
            4 => $"{m.Youngs:0.##} GPa",
            5 => $"{m.SpecificStrength:0.#} kN·m/kg",
            6 => $"{m.SpecificStiffness:0.#} MN·m/kg",
            7 => $"{m.MaxTemp:0} °C",
            8 => $"{m.Conductivity:0.##} W/m·K",
            9 => $"cost {Rating(m.Cost)}",
            _ => $"{m.Density:0.##} g/cm³  ·  {m.Ultimate:0.#} MPa",
        };
        var keep = SelectedMaterialRow?.Material.Id;
        MaterialList.Clear();
        foreach (var m in q) MaterialList.Add(new MaterialRow { Material = m, Summary = Sum(m) });
        MatResultCount = $"{MaterialList.Count} material{(MaterialList.Count == 1 ? "" : "s")}";
        SelectedMaterialRow = MaterialList.FirstOrDefault(r => r.Material.Id == keep);
    }

    MatPropLine Line(string label, double v, string unit, Func<Material, double> f, string hint, string fmt = "#,0.##")
    {
        double max = Materials.Max(f);
        return new MatPropLine { Label = label, Value = double.IsNaN(v) ? "Not defined" : $"{F(v, fmt)} {unit}", Hint = hint, HasBar = !double.IsNaN(v) && max > 0, Bar = double.IsNaN(v) || max <= 0 ? 0 : Math.Clamp(v / max, 0.02, 1) };
    }

    void ShowMaterial(Material? m)
    {
        MaterialProps.Clear();
        HasMaterial = m != null;
        if (m == null) { MatMassNote = ""; CompareButtonText = "Add to compare"; return; }
        MatName = m.Name; MatCategory = m.Category; MatColor = m.Color; MatUses = m.Uses; MatWatch = m.Watch;
        MatDataStatus = m.IsCustom ? "User-defined — not verified by Prism" : m.Id is "6061" or "304" or "bronze" ? "Partially source checked — see conditions below" : "Reference estimates — see conditions below";
        MaterialProps.Add(Line("Density", m.Density, "g/cm³", x => x.Density, "Mass per volume"));
        MaterialProps.Add(Line("Young's modulus", m.Youngs, "GPa", x => x.Youngs, "Stiffness: how little it flexes under load", "#,0.###"));
        MaterialProps.Add(Line("Yield strength", m.Yield, "MPa", x => x.Yield, "Stress where permanent deformation starts"));
        MaterialProps.Add(Line(m.Evidence.StrengthBasis, m.Ultimate, "MPa", x => x.Ultimate, "Bending, tensile and yield values are not interchangeable"));
        MaterialProps.Add(new MatPropLine { Label = "Poisson's ratio", Value = F(m.Poisson, "0.00"), Hint = "Sideways shrink when stretched" });
        MaterialProps.Add(Line("Strength-to-weight", m.SpecificStrength, "kN·m/kg", x => x.SpecificStrength, "Reported strength ÷ density; compare only matching test bases"));
        MaterialProps.Add(Line("Stiffness-to-weight", m.SpecificStiffness, "MN·m/kg", x => x.SpecificStiffness, "Modulus ÷ density: how stiff for its weight"));
        MaterialProps.Add(Line("Thermal conductivity", m.Conductivity, "W/m·K", x => x.Conductivity, "Heat flow: high for heat sinks, low for insulation"));
        MaterialProps.Add(Line("Thermal expansion", m.Expansion, "µm/m·K", x => x.Expansion, "Growth per °C: matters when joining different materials"));
        MaterialProps.Add(Line("Max service temp.", m.MaxTemp, "°C", x => x.MaxTemp, "Approximate continuous limit"));
        MaterialProps.Add(new MatPropLine { Label = "Specific heat", Value = $"{m.SpecificHeat:0} J/kg·K", Hint = "Energy to warm 1 kg by 1 °C" });
        MaterialProps.Add(new MatPropLine {Label="Data revision",Value=m.Evidence.Revision,Hint=m.Evidence.Condition});
        MatEvidence=$"Conditions: {m.Evidence.Condition}\nChecked: {m.Evidence.CheckedProperties}\nSource: {m.Evidence.Source}";
        MaterialProps.Add(new MatPropLine { Label = "Relative cost", Value = Rating(m.Cost), Hint = "● cheap … ●●●●● very expensive" });
        MaterialProps.Add(new MatPropLine { Label = "Corrosion resistance", Value = Rating(m.Corrosion), Hint = "● rusts … ●●●●● immune" });
        MaterialProps.Add(new MatPropLine { Label = "Machinability", Value = Rating(m.Machining), Hint = "● difficult … ●●●●● easy" });
        CompareButtonText = _compare.Any(c => c.Id == m.Id) ? "Remove from compare" : "Add to compare";
        UpdateMassNote(m);
    }

    /// <summary>Mass of the selected body if it were made of the shown material: the number an engineer usually wants first.</summary>
    void UpdateMassNote(Material? m)
    {
        MatMassNote = "";
        if (m == null) return;
        var solids = Session.SelectedObjects.Where(o => o.Kind is ObjKind.Solid or ObjKind.Form && o.Visible).ToList();
        if (solids.Count == 0) return;
        try
        {
            double vol = solids.Sum(o => Kernel.Occ.Mass(o.Shape) is { Solid: true } mp ? mp.Volume : 0);
            if (vol <= 0) return;
            double g = vol * m.Density / 1000.0;
            string who = solids.Count == 1 ? solids[0].Name : $"{solids.Count} bodies";
            MatMassNote = $"{who} would weigh {(g >= 1000 ? $"{g / 1000:0.###} kg" : $"{g:0.#} g")} in {m.Name}  ({vol / 1000:#,0.##} cm³)";
        }
        catch (Kernel.KernelException) { }
    }

    // ------------------------------------------------------------ open / close
    public void OpenMaterials()
    {
        if (_materialsTab == null) return;
        if (!_materialsTab.IsVisible)
        {
            _materialsTab.IsVisible = true;
            if (SelectedTab != _materialsTab && SelectedTab != _sketchTab) _lastNormalTab = SelectedTab ?? _lastNormalTab;
        }
        ShowMaterials = true;
        SelectedTab = _materialsTab;
        RibbonCollapsed = false;
        OpenRightPanel("appearance"); SyncLook();
        Session.Say("Appearance studio: select a body and choose a finish, or open Materials to assign engineering properties.");
    }

    public void CloseMaterials()
    {
        if (_materialsTab == null) return;
        ShowMaterials = false;
        _materialsTab.IsVisible = false;
        if (SelectedTab == _materialsTab) SelectedTab = IsSketching ? _sketchTab : _lastNormalTab;
        CloseRightPanel("appearance");
    }

    // ------------------------------------------------------------ apply
    List<SceneObject> TargetsForLook() => Session.SelectedObjects.Where(o => o.Kind is ObjKind.Solid or ObjKind.Surface or ObjKind.Form).ToList();

    public void ApplyMaterial(Material? m = null)
    {
        m ??= SelectedMaterialRow?.Material;
        if (m == null) { Session.Say("Pick a material in the list first.", true); return; }
        var targets = TargetsForLook();
        if (targets.Count == 0) { Session.Say("Select a body or surface first (click it in the viewport or the Model list).", true); return; }
        EditAppearance(x => x.WithMaterial(m), $"Material: {m.Name}");
        Session.Say($"{m.Name} applied to {(targets.Count == 1 ? targets[0].Name : targets.Count + " objects")}.");
        SyncLook();
    }

    public void ClearMaterial()
    {
        var targets = TargetsForLook().Where(o => o.Look.MaterialId != null).ToList();
        if (targets.Count == 0) { Session.Say("The selection has no material.", true); return; }
        foreach (var o in targets) Session.Update(o.Id, x => x.WithLook(x.Look with { MaterialId = null, PhysicalSnapshot = null }), label: "Remove material");
        SyncLook();
    }

    public void ApplyColor(uint color)
    {
        EditAppearance(x => x.WithColor(color | 0xFF000000).WithLook(x.Look with { Surface = (x.Look.Surface ?? new()) with { Override = true } }), "Color");
        SyncLook(); RememberAppearanceColor();
    }

    public void ApplyHex(string text)
    {
        if (ColorCode.TryParse(text, out var color)) ApplyColor(color);
        else Session.Say("Use #RRGGBB, #RGB, rgb(255,128,0), hsl(30,100%,50%) or hsv(30,100%,100%).", true);
    }

    void PushLook()
    {
        if (_syncingLook || _editingAppearance) return;
        float metal = (float)Math.Clamp(LookMetal, 0, 1), gloss = (float)Math.Clamp(LookGloss, 0, 1);
        var pattern = (SurfacePattern)Math.Clamp(LookPattern, 0, 5);
        float scale = (float)Math.Clamp(LookPatternScale, .1, 10000), angle = (float)LookPatternAngle;
        EditAppearance(x => x.WithLook(x.Look with { Metal = metal, Gloss = gloss, Pattern = pattern, PatternScale = scale, PatternAngle = angle,
            Surface = (x.Look.Surface ?? new()) with { Override = true } }), "Surface finish");
    }

    /// <summary>Show the finish and material of the current selection in the panel.</summary>
    void SyncLook()
    {
        if (_editingAppearance) return;
        CanApplyAppearance = TargetsForLook().Count > 0;
        AppearanceColor = TargetsForLook().FirstOrDefault()?.Color ?? 0xFFB9C6D6;
        var objs = TargetsForLook();
        _syncingLook = true;
        if (objs.Count > 0) { LookMetal = objs[0].Look.Metal; LookGloss = objs[0].Look.Gloss; LookPattern=(int)objs[0].Look.Pattern;LookPatternScale=objs[0].Look.PatternScale;LookPatternAngle=objs[0].Look.PatternAngle; ColorHex = (objs[0].Color & 0xFFFFFF).ToString("X6"); }
        SelectedSurface = objs.FirstOrDefault()?.Look.Surface ?? new();
        OnPropertyChanged(nameof(AppearanceStatus));
        _syncingLook = false;
        MatTarget = objs.Count == 0 ? "Nothing selected. Click a body, then press Apply." : objs.Count == 1 ? $"Applies to: {objs[0].Name}" : $"Applies to {objs.Count} objects";
        var names = objs.Select(o => o.Material?.DisplayName).Distinct().ToList();
        AssignedText = objs.Count == 0 ? "No selection" : names.Count == 1 && names[0] != null ? $"Assigned: {names[0]}" : names.Count > 1 ? "Assigned: mixed materials" : "No material assigned";
        var actual=objs.FirstOrDefault()?.Material;
        HasAssignedData=actual!=null;
        AssignedData=actual==null?"":$"{(objs.Count>1?"First selected body: ":"")}{objs[0].Name}\n{actual.DisplayName}\n{(actual.IsCustom ? "User-defined — not verified by Prism\n" : "")}Density: {F(actual.Density)} g/cm³\nE: {F(actual.Youngs)} GPa; ν: {F(actual.Poisson)}\nYield: {F(actual.Yield)} MPa\n{actual.Evidence.StrengthBasis}: {F(actual.Ultimate)} MPa\nk: {F(actual.Conductivity)} W/(m·K)\nExpansion: {F(actual.Expansion)} µm/(m·K)\nHeat capacity: {F(actual.SpecificHeat)} J/(kg·K)\nRevision: {actual.Evidence.Revision}\n{actual.Evidence.Condition}\nChecked: {actual.Evidence.CheckedProperties}\nSource: {actual.Evidence.Source}";
        UpdateMassNote(SelectedMaterialRow?.Material);
    }

    /// <summary>Jump the list to the material assigned to the selection.</summary>
    public void ShowAssignedMaterial()
    {
        var id = TargetsForLook().FirstOrDefault()?.Look.MaterialId;
        if (id == null) return;
        MaterialSearch = ""; MaterialCategoryIndex = 0;
        SelectedMaterialRow = MaterialList.FirstOrDefault(r => r.Material.Id == id);
    }

    // ------------------------------------------------------------ compare
    public void ToggleCompare()
    {
        var m = SelectedMaterialRow?.Material;
        if (m == null) return;
        var ex = _compare.FindIndex(c => c.Id == m.Id);
        if (ex >= 0) _compare.RemoveAt(ex);
        else { if (_compare.Count >= 3) _compare.RemoveAt(0); _compare.Add(m); }
        CompareButtonText = ex >= 0 ? "Add to compare" : "Remove from compare";
        RebuildCompare();
    }

    public void ClearCompare() { _compare.Clear(); CompareButtonText = "Add to compare"; RebuildCompare(); }

    void RebuildCompare()
    {
        CompareRows.Clear();
        HasCompare = _compare.Count > 0;
        if (_compare.Count == 0) return;
        string V(int i, Func<Material, string> f) => i < _compare.Count ? f(_compare[i]) : "";
        void Row(string label, Func<Material, string> f) => CompareRows.Add(new CompareRow { Label = label, A = V(0, f), B = V(1, f), C = V(2, f) });
        CompareRows.Add(new CompareRow { Label = "", A = V(0, m => m.Name), B = V(1, m => m.Name), C = V(2, m => m.Name), IsHeader = true });
        Row("Density g/cm³", m => F(m.Density));
        Row("Modulus GPa", m => F(m.Youngs, "#,0.###"));
        Row("Yield MPa", m => F(m.Yield));
        Row("Strength basis", m => m.Evidence.StrengthBasis);
        Row("Strength MPa", m => F(m.Ultimate));
        Row("Strength/weight", m => F(m.SpecificStrength, "0.#"));
        Row("Stiffness/weight", m => F(m.SpecificStiffness, "0.#"));
        Row("Conductivity", m => F(m.Conductivity));
        Row("Expansion", m => F(m.Expansion));
        Row("Max temp °C", m => F(m.MaxTemp, "0"));
        Row("Cost", m => Rating(m.Cost));
        Row("Corrosion", m => Rating(m.Corrosion));
        Row("Machinability", m => Rating(m.Machining));
    }
}
