using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

// ---------------------------------------------------------------- parameters shown in the command panel
public abstract partial class Param : ObservableObject
{
    public string Section { get; set; } = "Settings";
    public string Label { get; init; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLabel))]
    public partial string? Caption { get; set; }
    public string DisplayLabel => Caption ?? Label;
    public bool IsAdvanced { get; init; }
    public bool IsResult { get; init; }
    [ObservableProperty] public partial string HelpText { get; set; } = "";
    [ObservableProperty] public partial bool IsVisible { get; set; } = true;
    public Action? Changed { get; set; }
    protected void Notify() => Changed?.Invoke();
}

public sealed partial class NumberParam : Param
{
    [ObservableProperty] public partial string Formula { get; set; } = "";
    [ObservableProperty] public partial double Value { get; set; }
    public double Min { get; init; } = double.MinValue;
    public double Max { get; init; } = double.MaxValue;
    public decimal InputMinimum => Min <= (double)decimal.MinValue ? decimal.MinValue : Min >= (double)decimal.MaxValue ? decimal.MaxValue : (decimal)Min;
    public decimal InputMaximum => Max >= (double)decimal.MaxValue ? decimal.MaxValue : Max <= (double)decimal.MinValue ? decimal.MinValue : (decimal)Max;
    public double Step { get; init; } = 1;
    public string Unit { get; init; } = "mm";
    public string Format => Step < 0.001 ? "0.######" : Step < 0.1 ? "0.###" : "0.##";
    partial void OnValueChanged(double value)
    {
        var c = Math.Clamp(value, Min, Max);
        if (c != value) { Value = c; return; }
        Notify();
    }
}

public sealed partial class IntParam : Param
{
    [ObservableProperty] public partial int Value { get; set; }
    public int Min { get; init; } = 1;
    public int Max { get; init; } = 100;
    partial void OnValueChanged(int value)
    {
        var c = Math.Clamp(value, Min, Max);
        if (c != value) { Value = c; return; }
        Notify();
    }
}

public sealed partial class BoolParam : Param
{
    [ObservableProperty] public partial bool Value { get; set; }
    partial void OnValueChanged(bool value) => Notify();
}

public sealed partial class TextParam : Param
{
    [ObservableProperty] public partial string Value { get; set; } = "";
    public string Hint { get; init; } = "";
    partial void OnValueChanged(string value) => Notify();
}

public sealed partial class ChoiceParam : Param
{
    public bool IsIllustrated { get; init; }
    public bool IsAngular { get; init; }
    public IReadOnlyList<string> Options { get; init; } = [];
    [ObservableProperty] public partial int Index { get; set; }
    bool _silent;
    partial void OnIndexChanged(int value) { if (!_silent) Notify(); }
    /// <summary>Change the choice (the panel shows it) without triggering a rebuild: used when a command picks an option for the user.</summary>
    public void SetSilently(int index) { _silent = true; try { Index = index; } finally { _silent = false; } }
    public string Selected => Options[Math.Clamp(Index, 0, Options.Count - 1)];
}

// ---------------------------------------------------------------- selection slots
public sealed partial class SelectionSlot : ObservableObject
{
    public string Section { get; set; } = "";
    public bool ProfileSelection { get; init; }
    public string PickerLabel => ProfileSelection ? "Selected geometry" : DisplayLabel;
    public bool HasSelectionRules => Multi && Filter.HasFlag(SelFilter.Edge);
    public string[] SelectionRules => ProfileSelection
        ? ["Whole profile", "Single edges", "Connected chain", "Tangent chain", "Partial loop"]
        : ["Single edges", "Connected chain", "Tangent chain", "Partial loop"];
    [ObservableProperty] public partial int SelectionRuleIndex { get; set; }
    [ObservableProperty] public partial bool OtherPath { get; set; }
    public int EdgeRule => SelectionRuleIndex - (ProfileSelection ? 1 : 0);
    public bool IsPartialLoop => HasSelectionRules && EdgeRule == 3;
    public bool ReplacesSelection => !Multi || ProfileSelection && EdgeRule < 0;
    public SelFilter PickFilter => !ProfileSelection ? Filter : EdgeRule < 0 ? Filter & ~SelFilter.Edge : SelFilter.Edge;
    internal SelItem? ChainStart, ChainEnd;
    internal bool PathNeedsUpdate;
    public void ResetChain() { ChainStart = ChainEnd = null; PathNeedsUpdate = false; }
    partial void OnSelectionRuleIndexChanged(int value)
    {
        ChainStart = ChainEnd = null; Items.Clear();
        OnPropertyChanged(nameof(IsPartialLoop)); OnPropertyChanged(nameof(PickFilter));
        Changed?.Invoke();
    }
    partial void OnOtherPathChanged(bool value)
    {
        if (ChainStart != null && ChainEnd != null) { PathNeedsUpdate = true; Changed?.Invoke(); }
    }
    public string Label { get; init; } = "";
    public string? Caption { get; init; }
    public string DisplayLabel => Caption ?? Label;
    public SelFilter Filter { get; set; } = SelFilter.AnyObject;
    public bool Multi { get; init; }
    /// <summary>Clicking a face selects what it belongs to (a hole, slot, pocket, boss...), by the chosen <see cref="Intent"/>, instead of the one face.</summary>
    public bool SmartPick { get; init; }
    static readonly string[] IntentNames = ["Whole feature", "Single face", "Tangent chain", "Same size", "Pattern"];
    public string[] PickIntents => IntentNames;
    [ObservableProperty] public partial int IntentIndex { get; set; }
    public PickIntent Intent => (PickIntent)Math.Clamp(IntentIndex, 0, IntentNames.Length - 1);
    public bool Optional { get; init; }
    public bool Ordered { get; init; }
    public bool IsResult { get; init; }
    public int MaxCount { get; init; } = int.MaxValue;
    public ObservableCollection<SelItem> Items { get; } = [];
    [ObservableProperty] public partial bool IsExpanded { get; set; } = true;
    [ObservableProperty] public partial bool IsActive { get; set; }
    [ObservableProperty] public partial bool IsVisible { get; set; } = true;
    [ObservableProperty] public partial string Summary { get; set; } = "Click in the viewport";
    public Action? Changed { get; set; }
    public string CountText => MaxCount == int.MaxValue ? Items.Count.ToString() : $"{Items.Count}/{MaxCount}";
    public bool CanAdd => ReplacesSelection || Items.Count < MaxCount;

    public SelectionSlot() => Items.CollectionChanged += (_, _) =>
    {
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(CanAdd));
        Changed?.Invoke();
    };

    public bool TryAdd(SelItem item)
    {
        if (Items.Contains(item) || Items.Count >= (ReplacesSelection ? 1 : MaxCount)) return false;
        Items.Add(item);
        return true;
    }

    partial void OnIsActiveChanged(bool value) { if (value) IsExpanded = true; }

    public void Update(Func<SelItem, string> describe)
    {
        Summary = Items.Count == 0 ? (Optional ? "(optional)" : "Click in the viewport")
                : Items.Count == 1 ? describe(Items[0]) : $"{Items.Count} items selected";
    }
}

/// <summary>One read-out line. Level: 0 plain, 1 good, 2 warning, 3 problem.</summary>
public sealed record ResultLine(string Label, string Value, int Level = 0)
{
    public bool IsGood => Level == 1; public bool IsWarn => Level == 2; public bool IsBad => Level == 3;
}

// ---------------------------------------------------------------- command result
public sealed record NewObject(Shape? Shape, string NamePrefix, ObjKind? Kind = null, uint? Color = null, Plane3? Plane = null, Core.Form.SubdMesh? Form = null, Appearance? Look = null, Meshing.TriMesh? Tri = null, Core.Assembly.AssemblyData? Asm = null, FlatPatternMap? FlatMap = null, bool IsReference = false, bool IsMoldHalf = false);

public sealed class CommandOutput
{
    public List<NewObject> Add { get; } = [];
    public List<(SceneObject Old, Shape New)> Replace { get; } = [];
    public List<(SceneObject Old, Core.Form.SubdMesh New)> ReplaceForms { get; } = [];
    public List<(SceneObject Old, Meshing.TriMesh New)> ReplaceMeshes { get; } = [];
    public List<SceneObject> Remove { get; } = [];
    /// <summary>Shapes shown only in the preview as a faint volume, such as the tool that is cutting.</summary>
    public List<Shape> Ghosts { get; } = [];
    /// <summary>Timeline label/icon for the history step this output creates (defaults to the command title).</summary>
    public string? Label { get; set; }
    public string? Icon { get; set; }
    public Action<Core.CadSession>? After { get; set; }
    /// <summary>Analysis commands: nothing in the document changes, so no history step is recorded.</summary>
    public bool NoOp { get; set; }
    /// <summary>The Replace entries are only a preview of components moving; the After callback sets the real positions.</summary>
    public bool ReplaceIsMove { get; set; }
}

// ---------------------------------------------------------------- command base
/// <summary>
/// A modeling operation with a parameter panel. Build() is called on every change to produce a live preview;
/// OK commits the same output to the document.
/// </summary>
/// <summary>
/// A grab handle for a distance-like value: an arrow from Origin along Dir whose tip sits at Param/Mul. Dragging it sets the value.
/// With a Ref direction it is a turning handle instead: Dir is the axis, Param is an angle in degrees, and the tip rides an arc of the given Radius starting from Ref.
/// </summary>
public sealed record HandleSpec(Vec3 Origin, Vec3 Dir, NumberParam Param, double Mul = 1, Vec3? Ref = null, double Radius = 0, bool ShowRange = false, uint Colour = 0);

/// <summary>Everything needed to run a command again: how to make it, its picks and its numbers. Stored on the history step the command created.</summary>
public sealed record FeatureRecipe(Func<Command> Make, string Title, List<object?> Values, List<List<SelItem>> Slots, Dictionary<int, string>? Expressions = null);

public abstract partial class Command : ObservableObject
{
    [ObservableProperty] public partial bool PreviewEnabled { get; set; } = true;
    partial void OnPreviewEnabledChanged(bool value) => OnChanged();
    public virtual bool SupportsPreview => Tool == null;
    public string CommitText => HasPanelStep ? "Apply and return" : EditMode ? "Update" : OkText == "OK" ? "Create" : OkText;
    // ---- editing a feature after the fact
    /// <summary>How to make a fresh copy of this command (set by whoever started it). Without it the step cannot be edited later.</summary>
    public Func<Command>? Factory { get; set; }
    /// <summary>True while the user is re-editing an earlier step: the model is rolled back to before that step.</summary>
    public bool EditMode { get; set; }
    public Core.HistoryNode? EditingNode { get; set; }
    public Core.HistoryNode? ReturnNode { get; set; }
    public Core.HistoryNode? RebuildEnd { get; set; }
    public Dictionary<Guid, Guid>? RebuildMap { get; set; }
    public string DisplayTitle => _panelStep?.title ?? (EditMode ? "Edit " + Title : Title);
    public string PanelHint => _panelStep?.hint ?? Hint;
    (string title, string hint, HashSet<Param> parameters, Action accept, Action cancel)? _panelStep;
    Dictionary<Param,bool>? _panelParamVisibility;
    Dictionary<SelectionSlot,bool>? _panelSlotVisibility;
    public bool HasPanelStep => _panelStep != null;
    object?[]? _initialValues;
    List<SelItem>[]? _initialSlots;
    object?[] InputValues()=>Params.Select(p=>p switch
    {
        NumberParam n=>(object)(n.Value,n.Formula),IntParam i=>i.Value,ChoiceParam c=>c.Index,
        BoolParam b=>b.Value,TextParam t=>t.Value,_=>null,
    }).ToArray();
    public virtual bool HasInputChanges=>_initialValues!=null && (!InputValues().SequenceEqual(_initialValues)
        || _initialSlots==null || Slots.Count!=_initialSlots.Length
        || Slots.Where((s,i)=>!s.Items.SequenceEqual(_initialSlots[i])).Any());

    /// <summary>A temporary operation page keeps the current editor and its working state alive.</summary>
    protected bool DeferPanelStep(string title,Action open)
    {
        if(!HasPanelStep)return false;
        if(_panelStep?.title==title){S.KeepPending();return true;}
        S.RequestPanelStep(title,open);return true;
    }

    protected void OpenPanelStep(string title, string hint, IEnumerable<Param> parameters, Action accept, Action cancel)
    {
        if(HasPanelStep)throw new InvalidOperationException("Request the panel switch before initializing its preview and return state.");
        _panelParamVisibility=Params.ToDictionary(p=>p,p=>p.IsVisible);
        _panelSlotVisibility=Slots.ToDictionary(p=>p,p=>p.IsVisible);
        _panelStep = (title, hint, parameters.ToHashSet(), accept, cancel);
        OnChanged(); S.RaiseState();
    }
    public bool FinishPanelStep(bool accept)
    {
        if (_panelStep is not { } step) return false;
        _panelStep = null;
        foreach(var p in _panelParamVisibility!)p.Key.IsVisible=p.Value;
        foreach(var p in _panelSlotVisibility!)p.Key.IsVisible=p.Value;
        _panelParamVisibility=null;_panelSlotVisibility=null;
        if (accept) step.accept(); else step.cancel();
        OnChanged(); S.RaiseState(); return true;
    }
    /// <summary>Commands driven by an interactive viewport tool keep state outside their parameters, so they cannot be replayed.</summary>
    public virtual bool CanEdit => Tool == null;

    public FeatureRecipe? Capture()
    {
        if (Factory == null || !CanEdit) return null;
        var values = Params.Select(p => p switch
        {
            NumberParam n => (object?)n.Value, IntParam i => i.Value, BoolParam b => b.Value, TextParam x => x.Value, ChoiceParam c => c.Index, _ => null,
        }).ToList();
        var expressions = Params.Select((p, i) => (p, i)).Where(x => x.p is NumberParam { Formula.Length: > 0 })
            .ToDictionary(x => x.i, x => ((NumberParam)x.p).Formula);
        return new FeatureRecipe(Factory, Title, values, [.. Slots.Select(s => s.Items.ToList())], expressions);
    }

    /// <summary>Fill the picks and numbers from a recipe. Picks of objects that were remade since are translated through <paramref name="map"/>.</summary>
    public void Restore(FeatureRecipe r, IReadOnlyDictionary<Guid, Guid>? map = null)
    {
        r = UpgradeRecipe(r);
        ConfigureInputs(() =>
        {
            for (int i = 0; i < Slots.Count && i < r.Slots.Count; i++)
            {
                if (Slots[i].ProfileSelection && r.Slots[i].Any(it => it.Kind == SelKind.Edge)) Slots[i].SelectionRuleIndex = 1;
                Slots[i].Items.Clear();
                foreach (var it in r.Slots[i]) Slots[i].TryAdd(map != null && map.TryGetValue(it.Obj, out var n) ? it with { Obj = n } : it);
            }
            for (int i = 0; i < Params.Count && i < r.Values.Count; i++)
            {
                switch (Params[i], r.Values[i])
                {
                    case (NumberParam n, double d): n.Value = d; break;
                    case (IntParam ip, int v): ip.Value = v; break;
                    case (BoolParam b, bool v): b.Value = v; break;
                    case (TextParam x, string v): x.Value = v; break;
                    case (ChoiceParam c, int v): c.Index = v; break;
                }
            }
            foreach (var (index, formula) in r.Expressions ?? [])
                if (index < Params.Count && Params[index] is NumberParam number)
                {
                    double value = S.ParameterValue(formula);
                    if (value < number.Min || value > number.Max) throw new FormatException($"{number.Label} is outside its allowed range.");
                    number.Formula = formula; number.Value = value;
                }
        });
        OnChanged();
    }

    protected virtual FeatureRecipe UpgradeRecipe(FeatureRecipe recipe) => recipe;

    /// <summary>The arrow the user can pull in the viewport (null when there is nothing to pull yet).</summary>
    public virtual HandleSpec? Handle => null;
    /// <summary>All grab handles (a command with two-sided input shows one per side).</summary>
    public virtual IReadOnlyList<HandleSpec> Handles { get { var h = Handle; return h == null ? Array.Empty<HandleSpec>() : [h]; } }
    /// <summary>Set once the view has been tilted for pulling, so it only happens the first time.</summary>
    public bool TiltDone { get; set; }

    /// <summary>Centre of a shape's bounding box.</summary>
    protected static Vec3 CenterOf(Shape sh)
    {
        var m = sh.Tessellate(); var p = m.Positions;
        if (p.Length < 3) return Vec3.Zero;
        double x0 = double.MaxValue, y0 = x0, z0 = x0, x1 = double.MinValue, y1 = x1, z1 = x1;
        for (int i = 0; i + 2 < p.Length; i += 3) { x0 = Math.Min(x0, p[i]); x1 = Math.Max(x1, p[i]); y0 = Math.Min(y0, p[i + 1]); y1 = Math.Max(y1, p[i + 1]); z0 = Math.Min(z0, p[i + 2]); z1 = Math.Max(z1, p[i + 2]); }
        return new Vec3((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2);
    }

    /// <summary>A handle standing on a picked face or surface and pointing along its normal.</summary>
    protected HandleSpec? SurfaceHandle(SelItem it, NumberParam p, double mul = 1)
    {
        try
        {
            if (it.Kind == SelKind.Face && S.FaceFrame(it) is { } f) return new HandleSpec(f.point, f.normal, p, mul);
            var obj = S.Find(it.Obj); if (obj == null) return null;
            var n = ProfileUtil.NormalOf(obj.Shape) ?? Vec3.UnitZ;
            return new HandleSpec(CenterOf(obj.Shape), n, p, mul);
        }
        catch (Exception e) when (e is not OutOfMemoryException) { return null; }
    }

    /// <summary>Sampled polyline of a picked edge or curve object, used to anchor handles. Empty when there is nothing to read.</summary>
    protected List<Vec3> CurvePolyline(SelItem it)
    {
        var pts = S.EdgePoints(it);
        if (pts.Count >= 2) return pts;
        try
        {
            var o = S.Find(it.Obj); if (o == null) return pts;
            var m = o.Mesh; var E = m.EdgePoints;
            for (int sg = 0; sg < m.SegEdge.Length; sg++)
            {
                var a = new Vec3(E[6 * sg], E[6 * sg + 1], E[6 * sg + 2]); var b = new Vec3(E[6 * sg + 3], E[6 * sg + 4], E[6 * sg + 5]);
                if (pts.Count == 0 || pts[^1].DistanceTo(a) > 1e-6) pts.Add(a);
                pts.Add(b);
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException) { }
        return pts;
    }

    /// <summary>A handle standing at the middle of a picked edge and pointing away from the body, so pulling it sets a radius or distance.</summary>
    protected HandleSpec? EdgeHandle(SelItem it, NumberParam p, double mul = 1)
    {
        try
        {
            var pts = S.EdgePoints(it); var obj = S.Find(it.Obj);
            if (pts.Count < 2 || obj == null) return null;
            var mid = pts[pts.Count / 2];
            var lo = obj.Mesh.Min; var hi = obj.Mesh.Max;
            var dir = mid - (lo + hi) * 0.5;
            var tan = pts[^1] - pts[0];
            if (tan.Length > 1e-6) { tan = tan.Normalized(); dir -= tan * Vec3.Dot(dir, tan); }
            if (dir.Length < 1e-9) dir = Vec3.UnitZ;
            return new HandleSpec(mid, dir.Normalized(), p, mul);
        }
        catch (Exception e) when (e is not OutOfMemoryException) { return null; }
    }

    public abstract string Title { get; }
    public virtual string Hint => "";
    public virtual string OkText => "OK";
    /// <summary>Icon shown on the history timeline.</summary>
    public virtual string Icon => Title switch
    {
        "Box" => "box", "Cylinder" => "cylinder", "Sphere" => "sphere", "Cone" => "cone", "Torus" => "torus",
        "Extrude" => "extrude", "Emboss" => "emboss", "Rib" => "rib", "Extrude Surface" => "extrude-surface", "Revolve" => "revolve", "Revolve Surface" => "revolve-surface",
        "Sweep" => "sweep", "Sweep Surface" => "sweep-surface", "Loft" => "loft", "Loft Surface" => "loft-surface",
        "Fillet" => "fillet", "Chamfer" => "chamfer", "Shell" => "shell", "Union" => "union", "Subtract" => "subtract", "Intersect" => "intersect",
        "Move" => "move", "Rotate" => "rotate", "Scale" => "scale", "Mirror" => "mirror",
        "Planar Surface" => "planar", "Ruled Surface" => "ruled", "Patch" => "patch", "Offset Surface" => "offset", "Thicken" => "thicken", "Knit" => "knit",
        "NURBS Patch" => "nurbs-patch", "Control-Point Curve" => "curve-cp", "Curve Through Points" => "curve-through",
        "Edit Control Points" => "edit-points", "Sculpt" => "sculpt", "Create Sketch" => "sketch",
        _ => Title.StartsWith("Freeform") || Title.StartsWith("Edit Freeform") || Title.StartsWith("Create Face") ? "form-box" : "box",
    };
    /// <summary>Undo one step inside the command (e.g. the last form edit). Return true if handled.</summary>
    public virtual bool TryUndo() => false;
    public ObservableCollection<Param> Params { get; } = [];
    public ObservableCollection<SelectionSlot> Slots { get; } = [];
    public ObservableCollection<Param> PanelSettings { get; } = [];
    public ObservableCollection<Param> AdvancedSettings { get; } = [];
    public bool HasAdvancedSettings => AdvancedSettings.Any(p => p.IsVisible);
    public ObservableCollection<Param> ResultSettings { get; } = [];
    public ObservableCollection<SelectionSlot> InputSlots { get; } = [];
    public ObservableCollection<SelectionSlot> ResultSlots { get; } = [];
    public ObservableCollection<CommandSection> WorkflowSections { get; } = [];
    /// <summary>Read-outs shown under the parameters (measurements, mass properties, check results...).</summary>
    public ObservableCollection<ResultLine> Results { get; } = [];
    /// <summary>Commands can draw markers/curves over the viewport while they are open.</summary>
    public virtual void BuildOverlay(Core.CadSession s, Core.Overlay o) { }
    public IViewportTool? Tool { get; protected set; }
    public bool HasGizmoOptions => !HasPanelStep && (Tool is FormEditTool or EditPointsTool or ImageGizmoTool || S != null && Handles.Count > 0);
    /// <summary>Plane the grid and point placement use while this command runs.</summary>
    public virtual Plane3? WorkPlane => null;
    public CadSession S { get; private set; } = null!;
    int _inputUpdateDepth;

    /// <summary>Apply a set of inputs without building previews for incomplete intermediate states.</summary>
    public void ConfigureInputs(Action configure)
    {
        _inputUpdateDepth++;
        try { configure(); }
        finally { _inputUpdateDepth--; }
    }
    /// <summary>True while the user is applying the command (slow commands compute their result then, even without a preview).</summary>
    public bool Committing { get; set; }
    public string? Error { get; protected set; }

    protected T Add<T>(T p) where T : Param
    {
        p.Changed = OnChanged; Params.Add(p);
        (p.IsResult ? ResultSettings : p.IsAdvanced ? AdvancedSettings : PanelSettings).Add(p);
        return p;
    }
    protected SelectionSlot Slot(SelectionSlot s)
    {
        s.Changed = () => { UpdateSlots(); OnChanged(); }; Slots.Add(s);
        (s.IsResult ? ResultSlots : InputSlots).Add(s);
        return s;
    }

    void RefreshWorkflow()
    {
        var groups = new List<(string title, List<object> items)>();
        void Place(string title, object item)
        {
            int i = groups.FindIndex(g => g.title == title);
            if (i < 0) { groups.Add((title, [])); i = groups.Count - 1; }
            groups[i].items.Add(item);
        }
        foreach (var slot in InputSlots) Place(slot.Section.Length > 0 ? slot.Section : slot.DisplayLabel, slot);
        foreach (var p in PanelSettings) Place(p.Section, p);
        foreach (var p in AdvancedSettings) Place("Advanced", p);
        foreach (var p in ResultSettings) Place("Result", p);
        foreach (var slot in ResultSlots) Place("Result", slot);
        int number = 0;
        foreach (var group in groups)
        {
            var visible = group.items.Where(x => x is Param p ? p.IsVisible : ((SelectionSlot)x).IsVisible).ToList();
            if (visible.Count == 0) continue;
            // Options precede their dependent pickers (e.g. axis type, then axis line).
            visible = visible.OrderBy(x => x is SelectionSlot ? 1 : 0).ToList();
            var section = WorkflowSections.FirstOrDefault(s => s.Title == group.title);
            if (section == null) { section = new CommandSection(group.title); WorkflowSections.Add(section); }
            section.Number = (++number).ToString(); section.IsVisible = true;
            int index = WorkflowSections.IndexOf(section);
            if (index != number - 1) WorkflowSections.Move(index, number - 1);
            if (!section.Items.SequenceEqual(visible)) { section.Items.Clear(); foreach (var item in visible) section.Items.Add(item); }
            section.IsActive = visible.OfType<SelectionSlot>().Any(s => s.IsActive);
        }
        foreach (var section in WorkflowSections)
            if (!groups.Any(g => g.title == section.Title && g.items.Any(x => x is Param p ? p.IsVisible : ((SelectionSlot)x).IsVisible))) section.IsVisible = false;
    }

    protected NumberParam Num(string label, double v, double min = double.MinValue, double max = double.MaxValue, double step = 1, string unit = "mm")
        => Add(new NumberParam { Label = label, Value = v, Min = min, Max = max, Step = step, Unit = unit });
    protected ChoiceParam Choice(string label, int index, params string[] options) => Add(new ChoiceParam { Label = label, Options = options, Index = index });
    protected TextParam Text(string label, string v = "", string hint = "") => Add(new TextParam { Label = label, Value = v, Hint = hint });
    protected BoolParam Check(string label, bool v) => Add(new BoolParam { Label = label, Value = v });

    public void Begin(CadSession s)
    {
        S = s;
        ConfigureInputs(() =>
        {
            OnBegin();
            UpdateVisibility();
            UpdateSlots();
            // A preselection describes one input group. Reusing it for every matching
            // slot can turn a loft profile into its own rail or a Boolean body into its own tool.
            var selected = s.Selection.ToList();
            var inputs = Slots.Where(slot => slot.IsVisible && !slot.IsResult).ToList();
            var seed = inputs.FirstOrDefault(slot => !slot.Optional && selected.Any(item => s.Matches(item, slot.Filter)))
                ?? inputs.FirstOrDefault(slot => slot.Optional && selected.Any(item => s.Matches(item, slot.Filter)));
            if (seed != null)
            {
                if (seed.ProfileSelection && selected.Any(it => it.Kind == SelKind.Edge)) seed.SelectionRuleIndex = 1;
                foreach (var item in selected)
                    if (s.Matches(item, seed.Filter)) seed.TryAdd(item);
            }
            ActivateFirstOpenSlot();
        });
        OnChanged();
        _initialValues=InputValues();_initialSlots=Slots.Select(s=>s.Items.ToList()).ToArray();
    }

    protected virtual void OnBegin() { }
    /// <summary>True for tools whose picks need a good look at the model: starting one from an orthographic or straight-on standard view moves to the home view in perspective. Not applied when a feature is replayed from history.</summary>
    public virtual bool WantsHomeView => false;
    public virtual void OnEnd() { }
    /// <summary>Called instead of nothing extra when the user cancels (OnEnd runs for both).</summary>
    public virtual void OnCancel() { }

    public void ActivateFirstOpenSlot()
    {
        var open = Slots.FirstOrDefault(x => x.IsVisible && !x.Optional && x.Items.Count == 0) ?? Slots.FirstOrDefault(x => x.IsVisible);
        foreach (var x in Slots) x.IsActive = x == open;
    }

    /// <summary>Commit as soon as a pick completes the command (no need to press OK).</summary>
    public virtual bool CommitOnPick => false;

    /// <summary>A viewport click with its 3D hit point, offered before the normal slot pick. Return true to consume it.</summary>
    public virtual bool PickPoint(PickResult hit, bool additive) => false;
    /// <summary>Pointer pressed: return true to start dragging something of this command's (a placed point, say).</summary>
    public virtual bool GrabAt(CadSession s, double px, double py, bool ctrl) => false;
    /// <summary>The pointer moved while a grab started by GrabAt is held.</summary>
    public virtual void GrabMove(CadSession s, double px, double py, bool alt) { }

    public SelectionSlot? ActiveSlot => Slots.FirstOrDefault(x => x.IsActive);

    /// <summary>Feeds a viewport pick into the active slot. Returns true if it was consumed.</summary>
    public bool Pick(SelItem item, bool additive)
    {
        var slot = ActiveSlot;
        if (slot == null || !S.Matches(item, slot.PickFilter)) return false;
        if (slot.HasSelectionRules && item.Kind == SelKind.Edge && slot.EdgeRule > 0)
        {
            try { ConfigureInputs(() => SelectionChains.Pick(S, slot, item)); UpdateSlots(); OnChanged(); }
            catch (KernelException e) { S.Say(e.Message, true); }
            return true;
        }
        if (slot.ProfileSelection && slot.EdgeRule < 0)
        {
            slot.Items.Clear(); slot.Items.Add(item); AdvanceSlot(slot); return true;
        }
        if (slot.Multi && slot.SmartPick && item.Kind == SelKind.Face && PickFeature(slot, item)) return true;
        if (slot.Multi)
        {
            if (!slot.Items.Remove(item) && !slot.TryAdd(item))
                S.Say($"{slot.Label}: maximum {slot.MaxCount} selections. Remove an item to replace it.", true);
        }
        else
        {
            slot.Items.Clear(); slot.Items.Add(item);
            // advance to the next slot that still needs input
            AdvanceSlot(slot);
        }
        return true;
    }

    /// <summary>A click on a face of a solid picks the whole feature it belongs to (or the tangent chain, the same-size features, the pattern); clicking it again deselects it.</summary>
    bool PickFeature(SelectionSlot slot, SelItem item)
    {
        if (S.Find(item.Obj) is not { Kind: ObjKind.Solid, HasBrep: true } body) return false;
        try
        {
            var feat = FeatureFinder.FeatureOf(body, item.Index);
            var faces = FeatureFinder.Expand(body, item.Index, slot.Intent).Select(i => new SelItem(item.Obj, SelKind.Face, i)).ToList();
            ConfigureInputs(() =>
            {
                if (faces.All(slot.Items.Contains)) foreach (var f in faces) slot.Items.Remove(f);
                else foreach (var f in faces) slot.TryAdd(f);
            });
            UpdateSlots(); OnChanged();
            if (feat != null && slot.Intent == PickIntent.Feature && feat.Kind != FeatureKind.Face)
            {
                int pattern = FeatureFinder.PatternSize(body, feat);
                S.Say($"{feat.Name}: {faces.Count} face{(faces.Count == 1 ? "" : "s")} selected." + (pattern > 0 ? $" It is one of a pattern of {pattern + 1}: choose Pattern in Select by to take them all." : ""));
            }
            else if (slot.Intent != PickIntent.Face) S.Say($"{faces.Count} face{(faces.Count == 1 ? "" : "s")} selected.");
            return true;
        }
        catch (KernelException) { return false; }
    }

    void UpdateSlots() { foreach (var s in Slots) s.Update(S.Describe); }
    void AdvanceSlot(SelectionSlot slot)
    {
        var next = Slots.SkipWhile(x => x != slot).Skip(1).FirstOrDefault(x => x.IsVisible && !x.Optional && x.Items.Count == 0);
        if (next != null) { slot.IsActive = false; next.IsActive = true; }
    }

    /// <summary>Re-evaluate visibility rules then rebuild the preview.</summary>
    public void OnChanged()
    {
        if (S == null || _inputUpdateDepth > 0) return;
        // Typing a different literal into the normal feature editor detaches its formula.
        foreach (var number in Params.OfType<NumberParam>().Where(p => p.Formula.Length > 0))
            if (Math.Abs(number.Value - S.ParameterValue(number.Formula)) > 1e-10) number.Formula = "";
        UpdateVisibility();
        if (_panelStep is { } step)
        {
            foreach (var parameter in Params) parameter.IsVisible = step.parameters.Contains(parameter);
            foreach (var slot in Slots) slot.IsVisible = false;
        }
        ConfigureInputs(() =>
        {
            foreach (var slot in Slots.Where(s => s.IsPartialLoop && s.PathNeedsUpdate))
            { slot.PathNeedsUpdate = false; SelectionChains.UpdatePartial(S, slot); }
        });
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(CommitText));
        OnPropertyChanged(nameof(PanelHint));
        OnPropertyChanged(nameof(HasPanelStep));
        OnPropertyChanged(nameof(HasAdvancedSettings));
        S.RefreshPreview(this);
        RefreshWorkflow();
        OnPropertyChanged(nameof(HasGizmoOptions));
    }

    protected virtual void UpdateVisibility() { }

    /// <summary>Builds the result or returns null when inputs are incomplete. Throw KernelException for failures.</summary>
    public CommandOutput? TryBuild()
    {
        Error = null;
        try { return Build(); }
        catch (KernelException e) { Error = e.Message; }
        catch (Exception e) when (e is not OutOfMemoryException) { Error = e.Message; }
        return null;
    }

    protected abstract CommandOutput? Build();

    // ---- helpers for subclasses
    protected Shape? ShapeOf(SelectionSlot slot, int i = 0) => i < slot.Items.Count ? S.ShapeOf(slot.Items[i]) : null;
    protected IEnumerable<SceneObject> ObjectsOf(SelectionSlot slot) => slot.Items.Select(i => S.Find(i.Obj)).Where(o => o != null)!;
    protected bool Complete => Slots.Where(s => s.IsVisible && !s.Optional).All(s => s.Items.Count > 0);
}
