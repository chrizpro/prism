using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Prism.Core;
using Prism.Kernel;
using Prism.Meshing;

namespace Prism.Commands;

/// <summary>Base for tools that work on triangle meshes (scans, imports, converted solids).</summary>
public abstract class MeshCommand : Command
{
    protected readonly SelectionSlot Src;
    bool _force;
    string? _cacheKey; object? _cache;

    protected MeshCommand(string slotLabel = "Mesh", bool multi = false)
    {
        Src = Slot(new SelectionSlot { Label = slotLabel, Filter = SelFilter.MeshObj, Multi = multi });
    }

    public override string Icon => "mesh";

    protected override void OnBegin()
    {
        // convenience: a scene with a single mesh needs no picking
        if (Src.Items.Count == 0)
        {
            var meshes = S.Objects.Where(o => o.Tri != null && o.Visible).ToList();
            if (meshes.Count == 1) Src.Items.Add(SelItem.Of(meshes[0]));
        }
    }

    protected void Row(string label, string value, int level = 0) => Results.Add(new ResultLine(label, value, level));
    protected static string N(double v, string unit = "") => $"{v:#,0.###}{(unit.Length > 0 ? " " + unit : "")}";
    protected static string Count(int v) => v.ToString("#,0", CultureInfo.InvariantCulture);

    protected List<SceneObject> Meshes() => ObjectsOf(Src).Where(o => o.Tri != null).Distinct().ToList();

    /// <summary>"Preview" button for slow operations: results are computed only when asked for (or when the command is applied).</summary>
    protected ActionParam PreviewButton() => Add(new ActionParam { Label = "Preview result", Run = () => { _force = true; try { OnChanged(); } finally { _force = false; } } });

    /// <summary>Compute (or reuse) a slow result. Returns null while waiting for the user to press Preview.</summary>
    protected T? Cached<T>(string key, Func<T> compute) where T : class
    {
        if (_cache is T c && _cacheKey == key) return c;
        if (!Committing && !_force) { Row("Not computed yet", "Press 'Preview result' to see it, or apply directly", 2); return null; }
        var r = compute(); _cache = r; _cacheKey = key; return r;
    }

    protected static string Key(params object[] parts) => string.Join("|", parts.Select(p => p is double d ? d.ToString("R", CultureInfo.InvariantCulture) : p?.ToString() ?? ""));

    protected CommandOutput NoOp() { Results.Clear(); return new CommandOutput { NoOp = true }; }
}

// ================================================================= Repair
public sealed class MeshRepairCommand : MeshCommand
{
    readonly BoolParam _weld, _clean, _shells, _orient, _holes;
    readonly NumberParam _weldTol;
    readonly IntParam _minShell, _maxHole;

    public MeshRepairCommand(bool holesOnly = false) : base("Meshes", true)
    {
        _weld = Check("Merge duplicate vertices", !holesOnly);
        _weldTol = Num("Merge distance", 0.001, 0, 10, 0.001);
        _clean = Check("Remove degenerate and duplicate triangles", !holesOnly);
        _shells = Check("Remove tiny floating shells", false);
        _minShell = Add(new IntParam { Label = "Shells smaller than (triangles)", Value = 100, Min = 1, Max = 1000000 });
        _orient = Check("Fix flipped and inconsistent normals", !holesOnly);
        _holes = Check("Fill holes", true);
        _maxHole = Add(new IntParam { Label = "Largest hole to fill (edges)", Value = 250, Min = 3, Max = 100000 });
        _title = holesOnly ? "Fill Holes" : "Repair Mesh";
    }

    readonly string _title;
    public override string Title => _title;
    public override string Hint => "Fixes the usual scan and STL problems so the mesh can be printed or converted: merges split vertices, removes junk triangles, makes normals face outward and closes holes.";
    public override string Icon => "mesh-repair";
    public override string OkText => "Repair";

    protected override void UpdateVisibility() { _weldTol.IsVisible = _weld.Value; _minShell.IsVisible = _shells.Value; _maxHole.IsVisible = _holes.Value; }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        var objs = Meshes();
        if (objs.Count == 0) return null;
        var o = new CommandOutput { Label = "Repair mesh", Icon = "mesh-repair" };
        foreach (var obj in objs)
        {
            var m = obj.Tri!; var before = MeshAnalysis.Report(m);
            if (_weld.Value) m = MeshOps.Weld(m, Math.Max(_weldTol.Value, 1e-9));
            if (_clean.Value) m = MeshOps.Clean(m).mesh;
            if (_shells.Value) m = MeshOps.RemoveSmallShells(m, _minShell.Value, 0).mesh;
            int flipped = 0; if (_orient.Value) { var r = MeshOps.FixOrientation(m); m = r.mesh; flipped = r.flipped; }
            int filled = 0, skipped = 0; if (_holes.Value) { var r = MeshOps.FillHoles(m, _maxHole.Value); m = r.mesh; filled = r.filled; skipped = r.skipped; }
            if (_holes.Value && filled > 0 && _orient.Value) m = MeshOps.FixOrientation(m).mesh;
            var after = MeshAnalysis.Report(m);
            string p = objs.Count > 1 ? obj.Name + ": " : "";
            Row(p + "Triangles", $"{Count(before.Triangles)} → {Count(after.Triangles)}");
            Row(p + "Holes", $"{before.HoleLoops} → {after.HoleLoops}" + (skipped > 0 ? $"  ({skipped} too large)" : ""), after.HoleLoops == 0 ? 1 : 2);
            if (before.NonManifoldEdges + after.NonManifoldEdges > 0) Row(p + "Non-manifold edges", $"{before.NonManifoldEdges} → {after.NonManifoldEdges}", after.NonManifoldEdges == 0 ? 1 : 3);
            if (flipped > 0) Row(p + "Triangles re-oriented", Count(flipped));
            Row(p + "Result", after.Printable ? "Watertight and printable" : after.Closed ? "Closed, but check normals / non-manifold edges" : "Still open", after.Printable ? 1 : 2);
            o.ReplaceMeshes.Add((obj, m));
        }
        return o;
    }
}

// ================================================================= Print check
public sealed class MeshCheckCommand : MeshCommand
{
    static readonly (string name, double x, double y, double z)[] Printers =
    [
        ("Creality Ender 3 (220 × 220 × 250)", 220, 220, 250), ("Prusa MK4 (250 × 210 × 220)", 250, 210, 220), ("Bambu Lab X1 / P1 (256 × 256 × 256)", 256, 256, 256),
        ("Voron 350 (350 × 350 × 340)", 350, 350, 340), ("Resin printer (192 × 120 × 200)", 192, 120, 200), ("Custom", 200, 200, 200),
    ];
    static readonly (string name, double density)[] Filaments = [("PLA", 1.24), ("PETG", 1.27), ("ABS / ASA", 1.05), ("Nylon", 1.14), ("Resin", 1.10)];
    readonly ChoiceParam _printer, _material;
    readonly NumberParam _bx, _by, _bz, _overhang, _infill;
    List<List<Vec3>> _holes = []; List<(Vec3, Vec3)> _nm = []; (Vec3 min, Vec3 max)? _volume;

    public MeshCheckCommand() : base("Meshes", true)
    {
        _printer = Choice("Printer", 1, Printers.Select(p => p.name).ToArray());
        _bx = Num("Build width X", 200, 1, 5000); _by = Num("Build depth Y", 200, 1, 5000); _bz = Num("Build height Z", 200, 1, 5000);
        _material = Choice("Material", 0, Filaments.Select(f => f.name).ToArray());
        _infill = Num("Infill (solid weight estimate)", 20, 0, 100, 5, "%");
        _overhang = Num("Max overhang angle", 45, 10, 80, 5, "°");
    }

    public override string Title => "Print Check";
    public override string Hint => "A pre-flight check for 3D printing: is the mesh watertight, does it fit your printer, how much needs support, and roughly how heavy will it be.";
    public override string Icon => "mesh-check";
    public override string OkText => "Done";

    protected override void UpdateVisibility()
    {
        bool custom = _printer.Index == Printers.Length - 1;
        _bx.IsVisible = _by.IsVisible = _bz.IsVisible = custom;
    }

    protected override CommandOutput? Build()
    {
        var o = NoOp(); _holes = []; _nm = []; _volume = null;
        var objs = Meshes();
        if (objs.Count == 0) { Row("Pick a mesh", "or press Cancel and import one", 2); return o; }
        var (_, px, py, pz) = Printers[_printer.Index];
        if (_printer.Index == Printers.Length - 1) (px, py, pz) = (_bx.Value, _by.Value, _bz.Value);
        foreach (var obj in objs)
        {
            var m = obj.Tri!; var r = MeshAnalysis.Report(m);
            string p = objs.Count > 1 ? obj.Name + " - " : "";
            Row(p + "Triangles", Count(r.Triangles));
            Row(p + "Watertight", r.Closed ? "Yes" : $"No: {r.HoleLoops} hole(s), {r.BoundaryEdges} open edges", r.Closed ? 1 : 3);
            Row(p + "Manifold", r.NonManifoldEdges == 0 ? "Yes" : $"No: {r.NonManifoldEdges} bad edges", r.NonManifoldEdges == 0 ? 1 : 3);
            Row(p + "Normals", r.InconsistentEdges == 0 && r.Volume >= 0 ? "Consistent, outward" : "Inconsistent or inverted", r.InconsistentEdges == 0 && r.Volume >= 0 ? 1 : 3);
            if (r.Shells > 1) Row(p + "Separate shells", r.Shells.ToString(), 2);
            if (r.Degenerate + r.Duplicate > 0) Row(p + "Junk triangles", $"{r.Degenerate} degenerate, {r.Duplicate} duplicate", 2);
            Row(p + "Size", $"{r.Size.X:0.##} × {r.Size.Y:0.##} × {r.Size.Z:0.##} mm");

            // fit: try all six axis permutations
            var s = new[] { r.Size.X, r.Size.Y, r.Size.Z }; var b = new[] { px, py, pz };
            bool fits = false, fitsNow = s[0] <= b[0] && s[1] <= b[1] && s[2] <= b[2];
            foreach (var perm in new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } })
                if (s[perm[0]] <= b[0] && s[perm[1]] <= b[1] && s[perm[2]] <= b[2]) fits = true;
            Row(p + "Fits the printer", fitsNow ? "Yes" : fits ? "Only if rotated" : "No - scale it down or split it", fitsNow ? 1 : fits ? 2 : 3);
            _volume = (new Vec3(m.Center.X - px / 2, m.Center.Y - py / 2, m.Min.Z), new Vec3(m.Center.X + px / 2, m.Center.Y + py / 2, m.Min.Z + pz));

            if (r.Closed)
            {
                double vol = Math.Abs(r.Volume) / 1000.0;          // cm3
                double dens = Filaments[_material.Index].density;
                Row(p + "Volume", $"{vol:#,0.##} cm³");
                double shell = Math.Min(r.Area * 0.0012, vol); // roughly 1.2 mm of perimeter walls
                double used = _material.Index == 4 ? vol : Math.Min(vol, shell + (vol - shell) * _infill.Value / 100.0);
                Row(p + "Estimated weight", $"{used * dens:0.#} g at {(_material.Index == 4 ? "solid" : _infill.Value + "% infill")}");
            }
            var oh = MeshAnalysis.Overhang(m, _overhang.Value);
            double pct = 100 * oh.SupportArea / Math.Max(oh.TotalArea, 1e-9);
            Row(p + $"Overhangs over {_overhang.Value:0}°", $"{pct:0.#}% of the surface  ({oh.SupportArea:#,0} mm²)", pct < 3 ? 1 : pct < 15 ? 2 : 3);
            var cands = MeshAnalysis.OrientCandidates(m, _overhang.Value, 1);
            if (cands.Count > 0 && cands[0].support < oh.SupportArea * 0.8) Row(p + "Tip", $"a different orientation needs {cands[0].support:#,0} mm² of support - see Overhang Analysis", 2);
            _holes.AddRange(MeshAnalysis.HolePolylines(m)); _nm.AddRange(MeshAnalysis.NonManifoldSegments(m));
            if (r.Printable && fitsNow) Row(p + "Verdict", "Ready to slice", 1);
            else Row(p + "Verdict", r.Printable ? "Printable once it is on the bed" : "Repair the mesh first (Mesh > Repair)", r.Printable ? 2 : 3);
        }
        return o;
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        foreach (var h in _holes) o.AddPolyline(h, 0xFFFF3030, 4.5f);
        foreach (var (a, b) in _nm) o.AddLine(a, b, 0xFFFF40FF, 4.5f);
        if (_volume is { } v) CadSession.AddBox(o, v.min, v.max, 0xFF35B6FF);
    }
}

// ================================================================= Decimate
public sealed class MeshDecimateCommand : MeshCommand
{
    readonly ChoiceParam _mode;
    readonly NumberParam _percent;
    readonly IntParam _target;
    readonly BoolParam _keepBoundary, _keep;

    public MeshDecimateCommand() : base("Mesh")
    {
        _mode = Choice("Reduce to", 0, "A percentage of the triangles", "A triangle count");
        _percent = Num("Keep", 25, 0.5, 100, 5, "%");
        _target = Add(new IntParam { Label = "Triangles", Value = 20000, Min = 100, Max = 5000000 });
        _keepBoundary = Check("Keep open edges", true);
        _keep = Check("Keep the original too", false);
        PreviewButton();
    }

    public override string Title => "Reduce Mesh";
    public override string Hint => "Simplifies a dense mesh while keeping its shape (quadric edge collapse). Great for huge scans before converting to CAD or sending to a slicer.";
    public override string Icon => "mesh-reduce";
    public override string OkText => "Reduce";
    protected override void UpdateVisibility() { _percent.IsVisible = _mode.Index == 0; _target.IsVisible = _mode.Index == 1; }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        var obj = Meshes().FirstOrDefault(); if (obj == null) return null;
        var m = obj.Tri!;
        int target = _mode.Index == 0 ? (int)(m.TriCount * _percent.Value / 100) : _target.Value;
        target = Math.Clamp(target, 4, m.TriCount);
        var res = Cached(Key(obj.Id, m.TriCount, target, _keepBoundary.Value), () => MeshOps.Decimate(m, target, _keepBoundary.Value));
        if (res == null) { Row("Triangles now", Count(m.TriCount)); Row("Target", Count(target)); return null; }
        Row("Triangles", $"{Count(m.TriCount)} → {Count(res.TriCount)}  ({100.0 * res.TriCount / m.TriCount:0.#}%)", 1);
        var step = Math.Max(1, res.VertexCount / 4000);
        var dev = MeshAnalysis.Deviation(Sample(res, step), m, 0.1);
        Row("Shape change (RMS)", N(dev.Rms, "mm")); Row("Shape change (max)", N(dev.AbsMax, "mm"), dev.AbsMax < m.Size.Length * 0.002 ? 1 : 2);
        var o = new CommandOutput { Label = "Reduce mesh", Icon = Icon };
        if (_keep.Value) o.Add.Add(new(null, StripNumber(obj.Name), ObjKind.Mesh, obj.Color, null, null, obj.Look, res));
        else o.ReplaceMeshes.Add((obj, res));
        return o;
    }

    static TriMesh Sample(TriMesh m, int step) { var v = new List<float>(); for (int i = 0; i < m.VertexCount; i += step) { v.Add(m.V[3 * i]); v.Add(m.V[3 * i + 1]); v.Add(m.V[3 * i + 2]); } return new TriMesh([.. v], []); }
    static string StripNumber(string n) { int i = n.LastIndexOf(' '); return i > 0 && int.TryParse(n[(i + 1)..], out _) ? n[..i] : n; }
}

// ================================================================= Smooth / refine / offset
public sealed class MeshSmoothCommand : MeshCommand
{
    readonly IntParam _iter, _refine;
    readonly ChoiceParam _strength;
    readonly BoolParam _keepBoundary, _keepSharp;
    readonly NumberParam _sharp;

    public MeshSmoothCommand() : base("Mesh")
    {
        _iter = Add(new IntParam { Label = "Iterations", Value = 10, Min = 1, Max = 200 });
        _strength = Choice("Strength", 1, "Light", "Medium", "Strong");
        _refine = Add(new IntParam { Label = "Add detail first (subdivisions)", Value = 0, Min = 0, Max = 2 });
        _keepBoundary = Check("Keep open edges in place", true);
        _keepSharp = Check("Keep sharp edges and corners", true);
        _sharp = Num("Edges sharper than", 35, 5, 120, 5, "°");
        PreviewButton();
    }

    protected override void UpdateVisibility() { _sharp.IsVisible = _keepSharp.Value; }

    public override string Title => "Smooth Mesh";
    public override string Hint => "Removes scanner noise and stair-stepping without shrinking the part (Taubin smoothing). Optionally subdivides first so curved areas get smoother.";
    public override string Icon => "mesh-smooth";
    public override string OkText => "Smooth";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        var obj = Meshes().FirstOrDefault(); if (obj == null) return null;
        var m = obj.Tri!;
        double lambda = _strength.Index switch { 0 => 0.3, 1 => 0.5, _ => 0.7 };
        var res = Cached(Key(obj.Id, m.TriCount, _iter.Value, lambda, _refine.Value, _keepBoundary.Value, _keepSharp.Value, _sharp.Value), () =>
        {
            var x = _refine.Value > 0 ? MeshOps.Subdivide(m, _refine.Value) : m;
            double vol = x.Volume();
            var y = MeshOps.Smooth(x, _iter.Value, lambda, -lambda * 1.06, _keepBoundary.Value, _keepSharp.Value ? MeshOps.SharpWeights(x, _sharp.Value) : null);
            return vol > 0 && x.Topology().BoundaryEdges == 0 ? MeshOps.RestoreVolume(y, vol) : y;
        });
        if (res == null) { Row("Triangles", Count(m.TriCount)); return null; }
        Row("Triangles", $"{Count(m.TriCount)} → {Count(res.TriCount)}");
        Row("Volume change", $"{(m.Volume() == 0 ? 0 : 100 * (res.Volume() - m.Volume()) / Math.Abs(m.Volume())):0.##}%");
        var o = new CommandOutput { Label = "Smooth mesh", Icon = Icon };
        o.ReplaceMeshes.Add((obj, res));
        return o;
    }
}

public sealed class MeshOffsetCommand : MeshCommand
{
    readonly NumberParam _d;
    public MeshOffsetCommand() : base("Mesh") { _d = Num("Offset (+ grows, - shrinks)", 0.2, -50, 50, 0.1); }
    public override string Title => "Offset Mesh";
    public override string Hint => "Grows or shrinks the surface along its normals. Handy for print tolerances (clearance for mating parts); big offsets can self-intersect.";
    public override string Icon => "mesh-offset";
    protected override CommandOutput? Build()
    {
        Results.Clear();
        var objs = Meshes(); if (objs.Count == 0) return null;
        var o = new CommandOutput { Label = "Offset mesh", Icon = Icon };
        foreach (var obj in objs) o.ReplaceMeshes.Add((obj, MeshOps.Offset(obj.Tri!, _d.Value)));
        Row("Offset", N(_d.Value, "mm"));
        return o;
    }
}

// ================================================================= Cut
public sealed class MeshCutCommand : MeshCommand
{
    readonly ChoiceParam _plane, _keep;
    readonly NumberParam _offset, _nx, _ny, _nz;
    readonly BoolParam _cap, _flip;
    MeshOps.CutResult? _last; Vec3 _o, _n; double _size = 50;

    public MeshCutCommand() : base("Mesh")
    {
        _plane = Choice("Cut plane", 0, "XY (horizontal)", "XZ", "YZ", "Custom direction");
        _offset = Num("Position along the normal", 0);
        _nx = Num("Normal X", 0, -1, 1, 0.1, ""); _ny = Num("Normal Y", 0, -1, 1, 0.1, ""); _nz = Num("Normal Z", 1, -1, 1, 0.1, "");
        _flip = Check("Flip side", false);
        _cap = Check("Close the cut faces", true);
        _keep = Choice("Keep", 0, "Both halves", "Upper half", "Lower half");
    }

    public override string Title => "Split Mesh";
    public override string Hint => "Slice a mesh with a plane and cap the cut so both halves are printable. Use it to fit a big part on a small bed or to remove a base.";
    public override string Icon => "mesh-cut";
    public override string OkText => "Split";
    protected override void UpdateVisibility() { bool c = _plane.Index == 3; _nx.IsVisible = _ny.IsVisible = _nz.IsVisible = c; }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        var obj = Meshes().FirstOrDefault(); if (obj == null) return null;
        var m = obj.Tri!;
        var n = _plane.Index switch { 0 => Vec3.UnitZ, 1 => Vec3.UnitY, 2 => Vec3.UnitX, _ => new Vec3(_nx.Value, _ny.Value, _nz.Value) };
        if (n.Length < 1e-6) { Row("Normal", "must not be zero", 2); return null; }
        n = n.Normalized(); if (_flip.Value) n = -n;
        var origin = n * _offset.Value;
        _o = origin; _n = n; _size = m.Size.Length * 0.6;
        var cut = MeshOps.PlaneCut(m, origin, n, _cap.Value);
        _last = cut;
        if (cut.Above.TriCount == 0 && cut.Below.TriCount == 0) return null;
        if (cut.Above.TriCount == 0 || cut.Below.TriCount == 0) { Row("The plane misses the mesh", "move it into the part", 2); return null; }
        Row("Upper half", $"{Count(cut.Above.TriCount)} triangles" + (cut.Above.Topology().BoundaryEdges == 0 ? ", closed" : ""), cut.Above.Topology().BoundaryEdges == 0 ? 1 : 2);
        Row("Lower half", $"{Count(cut.Below.TriCount)} triangles" + (cut.Below.Topology().BoundaryEdges == 0 ? ", closed" : ""), cut.Below.Topology().BoundaryEdges == 0 ? 1 : 2);
        Row("Cut outlines", cut.Loops.Count.ToString());
        var o = new CommandOutput { Label = "Split mesh", Icon = Icon };
        string baseName = obj.Name;
        switch (_keep.Index)
        {
            case 0:
                o.ReplaceMeshes.Add((obj, cut.Above));
                o.Add.Add(new(null, baseName + " lower", ObjKind.Mesh, obj.Color, null, null, obj.Look, cut.Below));
                break;
            case 1: o.ReplaceMeshes.Add((obj, cut.Above)); break;
            default: o.ReplaceMeshes.Add((obj, cut.Below)); break;
        }
        return o;
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        if (_last == null) return;
        foreach (var l in _last.Loops) o.AddPolyline(l.Points, 0xFFFFC53D, 2.5f, l.Closed);
        var href = Math.Abs(_n.X) < 0.9 ? Vec3.UnitX : Vec3.UnitY;
        var u = Vec3.Cross(_n, href).Normalized(); var v = Vec3.Cross(_n, u);
        Vec3 P(double a, double b) => _o + u * (a * _size) + v * (b * _size);
        o.AddTri(P(-1, -1), P(1, -1), P(1, 1), 0x1AFFC53D); o.AddTri(P(-1, -1), P(1, 1), P(-1, 1), 0x1AFFC53D);
        o.AddPolyline([P(-1, -1), P(1, -1), P(1, 1), P(-1, 1)], 0x90FFC53D, 1.4f, true);
    }
}

// ================================================================= Section curves
public sealed class MeshSectionCommand : MeshCommand
{
    readonly ChoiceParam _plane;
    readonly NumberParam _offset, _spacing, _tol;
    readonly IntParam _count;
    readonly BoolParam _create;
    readonly List<MeshOps.CutLoop> _loops = [];

    public MeshSectionCommand() : base("Mesh")
    {
        _plane = Choice("Section plane", 0, "XY (horizontal)", "XZ", "YZ");
        _offset = Num("First position", 0);
        _count = Add(new IntParam { Label = "Number of sections", Value = 1, Min = 1, Max = 100 });
        _spacing = Num("Spacing", 5, 0.01);
        _tol = Num("Simplify (max deviation)", 0.05, 0, 5, 0.01);
        _create = Check("Create curves from the sections", false);
    }

    public override string Title => "Mesh Section Curves";
    public override string Hint => "Slice the scan with one or more planes and turn the outlines into curves, ready to extrude, loft or sweep into real CAD geometry.";
    public override string Icon => "mesh-section";
    public override string OkText => "Create curves";
    protected override void UpdateVisibility() { _spacing.IsVisible = _count.Value > 1; }

    protected override CommandOutput? Build()
    {
        Results.Clear(); _loops.Clear();
        var obj = Meshes().FirstOrDefault(); if (obj == null) return null;
        var m = obj.Tri!;
        var n = _plane.Index == 0 ? Vec3.UnitZ : _plane.Index == 1 ? Vec3.UnitY : Vec3.UnitX;
        var o = new CommandOutput { Label = "Section curves", Icon = Icon };
        int total = 0; double area = 0;
        for (int k = 0; k < _count.Value; k++)
        {
            var origin = n * (_offset.Value + k * _spacing.Value);
            foreach (var l in MeshOps.Section(m, origin, n))
            {
                var pts = Simplify(l.Points, _tol.Value, l.Closed);
                if (pts.Count < 2 || (l.Closed && pts.Count < 3)) continue;
                _loops.Add(new MeshOps.CutLoop(pts, l.Closed)); total++;
                if (l.Closed) area += PolyArea(pts, n);
                if (_create.Value)
                {
                    try { o.Add.Add(new(Occ.Polyline(pts, l.Closed), "Section", ObjKind.Curve)); } catch (KernelException) { }
                }
            }
        }
        Row("Outlines", total.ToString(), total > 0 ? 1 : 2);
        if (total == 0) Row("Nothing here", "the plane does not cut the mesh", 2);
        else { Row("Net section area", N(Math.Abs(area), "mm²")); Row("Points", Count(_loops.Sum(l => l.Points.Count))); }
        return _create.Value ? o : NoOp();
    }

    static double PolyArea(List<Vec3> p, Vec3 n)
    {
        Vec3 s = Vec3.Zero; for (int i = 0; i < p.Count; i++) s += Vec3.Cross(p[i], p[(i + 1) % p.Count]);
        return Vec3.Dot(s, n) / 2;
    }

    /// <summary>Douglas-Peucker polyline simplification.</summary>
    public static List<Vec3> Simplify(List<Vec3> pts, double tol, bool closed)
    {
        if (tol <= 0 || pts.Count < 4) return pts;
        var keep = new bool[pts.Count]; keep[0] = keep[^1] = true;
        void Rec(int a, int b)
        {
            double best = 0; int idx = -1; var A = pts[a]; var B = pts[b]; var ab = B - A; double l2 = Vec3.Dot(ab, ab);
            for (int i = a + 1; i < b; i++)
            {
                double t = l2 < 1e-18 ? 0 : Math.Clamp(Vec3.Dot(pts[i] - A, ab) / l2, 0, 1);
                double d = (pts[i] - (A + ab * t)).Length;
                if (d > best) { best = d; idx = i; }
            }
            if (best > tol && idx > 0) { keep[idx] = true; Rec(a, idx); Rec(idx, b); }
        }
        if (closed)
        {
            // split at the point farthest from the start so both halves are well conditioned
            int far = 0; double fd = 0; for (int i = 1; i < pts.Count; i++) { double d = pts[i].DistanceTo(pts[0]); if (d > fd) { fd = d; far = i; } }
            keep[far] = true; Rec(0, far); Rec(far, pts.Count - 1);
        }
        else Rec(0, pts.Count - 1);
        return pts.Where((_, i) => keep[i]).ToList();
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        foreach (var l in _loops) o.AddPolyline(l.Points, 0xFFFFC53D, 3f, l.Closed);
    }
}

// ================================================================= Wall thickness
public sealed class MeshThicknessCommand : MeshCommand
{
    readonly NumberParam _min, _max;
    Vec3? _thin; float _thinVal;
    public MeshThicknessCommand() : base("Mesh")
    {
        _min = Num("Minimum wall for your printer", 0.8, 0.05, 50, 0.1);
        _max = Num("Search distance", 30, 1, 1000, 5);
        PreviewButton();
    }
    public override string Title => "Wall Thickness";
    public override string Hint => "Colors the part by how thick its walls are: red is thinner than your printer can make, blue is solid.";
    public override string Icon => "mesh-thickness";
    public override string OkText => "Done";
    public override void OnCancel() { foreach (var o in Meshes()) S.ClearField(o.Id); }

    protected override CommandOutput? Build()
    {
        var o = NoOp(); _thin = null;
        var obj = Meshes().FirstOrDefault(); if (obj == null) return o;
        var m = obj.Tri!;
        var rep = MeshAnalysis.Report(m);
        if (!rep.Closed) Row("Note", "wall thickness needs a closed mesh - repair holes first", 2);
        var th = Cached(Key(obj.Id, m.TriCount, _max.Value), () => MeshAnalysis.Thickness(m, _max.Value));
        if (th == null) return o;
        var (disp, sc) = m.ToScalarMeshData(th);
        disp.Scalars = sc;
        S.SetField(obj, m, disp, 1, (float)(_min.Value * 3), 0f);
        int thin = 0;
        for (int i = 0; i < th.Length; i++) if (th[i] < _min.Value) thin++;
        // ignore the very thinnest 0.5% of points: a few vertices on edges and slivers always give meaningless zeros
        var order = Enumerable.Range(0, th.Length).OrderBy(i => th[i]).ToArray();
        int mi = order.Length == 0 ? 0 : order[Math.Min(order.Length - 1, (int)(order.Length * 0.005))]; float mn = th.Length == 0 ? 0 : th[mi];
        Row("Thinnest wall", N(mn, "mm"), mn >= _min.Value ? 1 : 3);
        Row("Below the minimum", $"{100.0 * thin / Math.Max(th.Length, 1):0.#}% of the surface", thin == 0 ? 1 : thin * 20 < th.Length ? 2 : 3);
        Row("Legend", "red = too thin, yellow = at the limit, blue = thick");
        _thin = m.P(mi); _thinVal = mn;
        return o;
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        if (_thin is { } p) { o.AddDot(p, 0xFFFFFFFF, 12); o.AddLabel(p, $"{_thinVal:0.##} mm", true, 1); }
    }
}

// ================================================================= Overhangs / orientation
public sealed class MeshOverhangCommand : MeshCommand
{
    readonly NumberParam _angle;
    readonly ChoiceParam _orient;
    List<(Vec3 down, double support, double height, double contact)> _cands = [];

    public MeshOverhangCommand() : base("Mesh")
    {
        _angle = Num("Max overhang angle", 45, 10, 80, 5, "°");
        _orient = Choice("Orientation", 0, "Keep as it is", "Best orientation", "Second best", "Third best");
    }
    public override string Title => "Overhang & Orientation";
    public override string Hint => "Shows which surfaces need support when printing upright (green fine, yellow marginal, red needs support) and finds the orientation that needs the least.";
    public override string Icon => "mesh-overhang";
    public override string OkText => "Apply";
    public override void OnCancel() { foreach (var o in Meshes()) S.ClearField(o.Id); }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        var obj = Meshes().FirstOrDefault(); if (obj == null) return null;
        var m = obj.Tri!;
        var oh = MeshAnalysis.Overhang(m, _angle.Value);
        var (disp, sc) = m.ToLabelledMeshData(oh.Label);
        disp.Scalars = sc;
        S.SetField(obj, m, disp, 2, 0, 3, palette: 1);
        double pct = 100 * oh.SupportArea / Math.Max(oh.TotalArea, 1e-9);
        Row("Needs support", $"{oh.SupportArea:#,0} mm²  ({pct:0.#}%)", pct < 3 ? 1 : pct < 15 ? 2 : 3);
        Row("Marginal", $"{oh.MarginalArea:#,0} mm²");
        Row("On the bed", $"{oh.BedArea:#,0} mm²");
        Row("Support volume (upper bound)", $"{oh.SupportVolumeUpper / 1000:#,0.##} cm³");
        Row("Height", N(oh.Height, "mm"));
        _cands = MeshAnalysis.OrientCandidates(m, _angle.Value, 3);
        for (int i = 0; i < _cands.Count; i++)
            Row($"Option {i + 1}", $"support {_cands[i].support:#,0} mm², height {_cands[i].height:0.#} mm, bed contact {_cands[i].contact:#,0} mm²", i == 0 ? 1 : 0);

        if (_orient.Index == 0 || _orient.Index - 1 >= _cands.Count) return new CommandOutput { NoOp = true };
        var down = _cands[_orient.Index - 1].down;
        var (axis, deg) = MeshAnalysis.Align(down, -Vec3.UnitZ);
        var rotated = MeshOps.Rotate(m, m.Center, axis, deg);
        rotated = MeshOps.Move(rotated, new Vec3(-rotated.Center.X, -rotated.Center.Y, -rotated.Min.Z));
        var o = new CommandOutput { Label = "Orient for printing", Icon = Icon };
        o.ReplaceMeshes.Add((obj, rotated));
        return o;
    }
}

// ================================================================= Deviation
public sealed class MeshDeviationCommand : MeshCommand
{
    readonly SelectionSlot _ref;
    readonly NumberParam _tol, _range;
    Vec3? _worst; double _worstVal;

    public MeshDeviationCommand() : base("Mesh to check")
    {
        _ref = Slot(new SelectionSlot { Label = "Compare with (CAD body or mesh)", Filter = SelFilter.Solid | SelFilter.SurfaceObj | SelFilter.MeshObj | SelFilter.Form });
        _tol = Num("Tolerance", 0.1, 0.0001, 100, 0.05);
        _range = Num("Color range (±)", 0.5, 0.001, 100, 0.1);
        PreviewButton();
    }
    public override string Title => "Deviation Analysis";
    public override string Hint => "Compare a scan with its CAD model (or two meshes): every point is colored by how far it is from the reference. Blue = below the reference, green = on it, red = above.";
    public override string Icon => "mesh-deviation";
    public override string OkText => "Done";
    public override void OnCancel() { foreach (var o in Meshes()) S.ClearField(o.Id); }

    protected override CommandOutput? Build()
    {
        var o = NoOp(); _worst = null;
        var obj = Meshes().FirstOrDefault(); if (obj == null || _ref.Items.Count == 0) return o;
        var refObj = S.Find(_ref.Items[0].Obj); if (refObj == null || refObj.Id == obj.Id) { Row("Reference", "pick a different object", 2); return o; }
        var m = obj.Tri!;
        var dev = Cached(Key(obj.Id, m.TriCount, refObj.Id, refObj.Tri?.TriCount ?? 0, _tol.Value), () =>
        {
            var rm = S.PrintMesh(refObj) ?? throw new InvalidOperationException("The reference has no surface.");
            return MeshAnalysis.Deviation(m, rm, _tol.Value);
        });
        if (dev == null) return o;
        var (disp, sc) = m.ToScalarMeshData(dev.PerVertex); disp.Scalars = sc;
        S.SetField(obj, m, disp, 1, (float)-_range.Value, (float)_range.Value);
        Row("Points within tolerance", $"{dev.WithinTolerance:0.#}%", dev.WithinTolerance > 95 ? 1 : dev.WithinTolerance > 80 ? 2 : 3);
        Row("RMS deviation", N(dev.Rms, "mm"));
        Row("Average (bias)", N(dev.Mean, "mm"));
        Row("Max above", N(dev.Max, "mm")); Row("Max below", N(dev.Min, "mm"));
        Row("Legend", $"blue -{_range.Value:0.##}  green 0  red +{_range.Value:0.##} mm");
        int wi = 0; float wv = 0; for (int i = 0; i < dev.PerVertex.Length; i++) if (Math.Abs(dev.PerVertex[i]) > Math.Abs(wv)) { wv = dev.PerVertex[i]; wi = i; }
        _worst = m.P(wi); _worstVal = wv;
        return o;
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        if (_worst is { } p) { o.AddDot(p, 0xFFFFFFFF, 12); o.AddLabel(p, $"{_worstVal:+0.###;-0.###} mm", true, 1); }
    }
}

// ================================================================= Segment and fit
public sealed class MeshSegmentCommand : MeshCommand
{
    readonly NumberParam _tol, _angle;
    readonly IntParam _minTris, _region;
    readonly BoolParam _pl, _cy, _sp, _co;
    readonly ChoiceParam _create;

    public MeshSegmentCommand() : base("Mesh")
    {
        _tol = Num("Surface tolerance", 0.3, 0.001, 50, 0.1);
        _angle = Num("Normal tolerance", 12, 1, 45, 1, "°");
        _minTris = Add(new IntParam { Label = "Smallest region (triangles)", Value = 40, Min = 3, Max = 100000 });
        _pl = Check("Planes", true); _cy = Check("Cylinders", true); _sp = Check("Spheres", true); _co = Check("Cones", true);
        _create = Choice("Create CAD geometry", 0, "Nothing - just analyze", "Only the region below", "All regions");
        _region = Add(new IntParam { Label = "Region number", Value = 1, Min = 1, Max = 200 });
        PreviewButton();
    }
    public override string Title => "Segment & Fit";
    public override string Hint => "Finds the flat faces, holes, bosses, balls and tapers hiding in a scan. Each region is colored and measured; create exact CAD planes, cylinders, spheres and cones from them to rebuild the part.";
    public override string Icon => "mesh-fit";
    public override string OkText => "Create";
    public override void OnCancel() { foreach (var o in Meshes()) S.ClearField(o.Id); }
    protected override void UpdateVisibility() { _region.IsVisible = _create.Index == 1; }

    List<FitRegion> _regions = [];
    public IReadOnlyList<FitRegion> Regions => _regions;

    protected override CommandOutput? Build()
    {
        Results.Clear();
        var obj = Meshes().FirstOrDefault(); if (obj == null) return null;
        var m = obj.Tri!;
        var found = Cached(Key(obj.Id, m.TriCount, _tol.Value, _angle.Value, _minTris.Value, _pl.Value, _cy.Value, _sp.Value, _co.Value),
            () => PrimitiveFit.Detect(m, new PrimitiveFit.Options(_tol.Value, _angle.Value, _minTris.Value, _pl.Value, _cy.Value, _sp.Value, _co.Value)));
        if (found == null) return null;
        _regions = found;
        var label = new int[m.TriCount];
        for (int i = 0; i < found.Count; i++) foreach (int t in found[i].Tris) label[t] = i + 1;
        var (disp, sc) = m.ToLabelledMeshData(label); disp.Scalars = sc;
        S.SetField(obj, m, disp, 2, 0, found.Count + 1, 0);
        double covered = found.Sum(r => r.Area) / Math.Max(m.Area(), 1e-9) * 100;
        Row("Regions found", found.Count.ToString(), found.Count > 0 ? 1 : 2);
        Row("Surface explained", $"{covered:0.#}%");
        for (int i = 0; i < Math.Min(found.Count, 40); i++)
            Row($"{i + 1}.  {found[i].Describe()}", $"{found[i].Area:#,0} mm²   rms {found[i].Rms:0.###}");
        Row("Gray areas", "free-form surface that is not a simple shape");

        if (_create.Index == 0) return NoOp();
        var o = new CommandOutput { Label = "Fit geometry", Icon = Icon };
        var pick = _create.Index == 1 ? found.Where((_, i) => i + 1 == _region.Value).ToList() : found;
        if (_create.Index == 1 && pick.Count == 0) { Row("Region", $"{_region.Value} does not exist", 2); return NoOp(); }
        foreach (var r in pick)
        {
            try { var nObj = BuildGeometry(r, m); if (nObj != null) o.Add.Add(nObj); }
            catch (KernelException e) { Row("Could not build", $"{r.Describe()}: {e.Message}", 2); }
        }
        return o;
    }

    internal static NewObject? BuildGeometry(FitRegion r, TriMesh m)
    {
        switch (r.Kind)
        {
            case PrimKind.Plane:
            {
                // convex outline of the region projected onto the plane
                var n = r.Axis; var href = Math.Abs(n.X) < 0.9 ? Vec3.UnitX : Vec3.UnitY;
                var u = Vec3.Cross(n, href).Normalized(); var v = Vec3.Cross(n, u);
                var pts = r.Tris.SelectMany(t => new[] { m.P(m.T[3 * t]), m.P(m.T[3 * t + 1]), m.P(m.T[3 * t + 2]) }).Distinct().Select(p => (x: Vec3.Dot(p - r.Origin, u), y: Vec3.Dot(p - r.Origin, v))).ToList();
                var hull = Hull(pts); if (hull.Count < 3) return null;
                var poly = hull.Select(h => r.Origin + u * h.x + v * h.y).ToList();
                var edge = Occ.Polyline(poly, true);
                return new NewObject(Occ.Profile([edge], true), "Plane fit", ObjKind.Surface, Palette.Surface);
            }
            case PrimKind.Cylinder:
            {
                double h = Math.Max(r.T1 - r.T0, 1e-3);
                var s = Occ.Cylinder(r.Radius, h);
                var (ax, deg) = MeshAnalysis.Align(Vec3.UnitZ, r.Axis);
                if (Math.Abs(deg) > 1e-6) s = Occ.Rotate(s, Vec3.Zero, ax, deg);
                s = Occ.Translate(s, r.Origin + r.Axis * r.T0);
                return new NewObject(s, "Cylinder fit", ObjKind.Solid);
            }
            case PrimKind.Sphere:
                return new NewObject(Occ.Translate(Occ.Sphere(r.Radius), r.Origin), "Sphere fit", ObjKind.Solid);
            default:
            {
                double tan = Math.Tan(r.HalfAngle * Math.PI / 180);
                double t0 = Math.Max(r.T0, 0), t1 = Math.Max(r.T1, t0 + 1e-3);
                var s = Occ.Cone(t0 * tan, t1 * tan, t1 - t0);
                var (ax, deg) = MeshAnalysis.Align(Vec3.UnitZ, r.Axis);
                if (Math.Abs(deg) > 1e-6) s = Occ.Rotate(s, Vec3.Zero, ax, deg);
                s = Occ.Translate(s, r.Origin + r.Axis * t0);
                return new NewObject(s, "Cone fit", ObjKind.Solid);
            }
        }
    }

    static List<(double x, double y)> Hull(List<(double x, double y)> p)
    {
        var s = p.OrderBy(a => a.x).ThenBy(a => a.y).ToList();
        if (s.Count < 3) return s;
        double Cr((double x, double y) o, (double x, double y) a, (double x, double y) b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        var h = new List<(double x, double y)>();
        foreach (var q in s) { while (h.Count >= 2 && Cr(h[^2], h[^1], q) <= 0) h.RemoveAt(h.Count - 1); h.Add(q); }
        int lower = h.Count + 1;
        for (int i = s.Count - 2; i >= 0; i--) { while (h.Count >= lower && Cr(h[^2], h[^1], s[i]) <= 0) h.RemoveAt(h.Count - 1); h.Add(s[i]); }
        h.RemoveAt(h.Count - 1);
        return h;
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        if (_create.Index == 1 && _region.Value >= 1 && _region.Value <= _regions.Count)
        {
            var r = _regions[_region.Value - 1];
            if (r.Kind is PrimKind.Cylinder or PrimKind.Cone) { var a = r.Origin + r.Axis * r.T0; var b = r.Origin + r.Axis * r.T1; o.AddLine(a, b, 0xFFFFFFFF, 2.5f); o.AddLabel((a + b) * 0.5, r.Describe(), true, 1); }
            else if (r.Kind == PrimKind.Sphere) { o.AddDot(r.Origin, 0xFFFFFFFF, 10); o.AddLabel(r.Origin, r.Describe(), true, 1); }
            else o.AddLabel((r.Min + r.Max) * 0.5, r.Describe(), true, 1);
        }
    }
}

// ================================================================= Align
public sealed class MeshAlignCommand : MeshCommand
{
    readonly ChoiceParam _mode;
    readonly SelectionSlot _ref;
    readonly IntParam _iter;
    readonly NumberParam _trim;
    readonly BoolParam _pre;

    public MeshAlignCommand() : base("Mesh")
    {
        _mode = Choice("Align", 0, "Place on the print bed (lowest point to Z = 0)", "Center on the origin (X, Y)", "Center of mass to the origin", "Lay the biggest flat face on the bed", "Principal axes to X, Y, Z", "Best fit to a CAD body or mesh");
        _ref = Slot(new SelectionSlot { Label = "Reference body", Filter = SelFilter.Solid | SelFilter.SurfaceObj | SelFilter.MeshObj, Optional = true, IsVisible = false });
        _iter = Add(new IntParam { Label = "Iterations", Value = 40, Min = 5, Max = 300 });
        _trim = Num("Ignore the worst", 10, 0, 60, 5, "%");
        _pre = Check("Find the starting position automatically", true);
        PreviewButton();
    }
    public override string Title => "Align Mesh";
    public override string Hint => "Position a scan: drop it on the bed, center it, lay a flat face down, or best-fit it onto the CAD model it was scanned from.";
    public override string Icon => "mesh-align";
    public override string OkText => "Align";
    protected override void UpdateVisibility() { bool icp = _mode.Index == 5; _ref.IsVisible = icp; _iter.IsVisible = _trim.IsVisible = _pre.IsVisible = icp; }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        var obj = Meshes().FirstOrDefault(); if (obj == null) return null;
        var m = obj.Tri!; TriMesh res;
        switch (_mode.Index)
        {
            case 0: res = MeshOps.Move(m, new Vec3(0, 0, -m.Min.Z)); break;
            case 1: res = MeshOps.Move(m, new Vec3(-m.Center.X, -m.Center.Y, 0)); break;
            case 2: res = MeshOps.Move(m, -m.CenterOfMass()); break;
            case 3:
            {
                var regs = PrimitiveFit.Detect(m, new PrimitiveFit.Options(m.Size.Length * 0.002, 10, Math.Max(20, m.TriCount / 200), true, false, false, false, 6));
                var plane = regs.FirstOrDefault(r => r.Kind == PrimKind.Plane);
                if (plane == null) { Row("No flat face found", "try Segment & Fit with looser tolerances", 2); return null; }
                // the face's outward normal must point down
                var (ax, deg) = MeshAnalysis.Align(plane.Convex ? plane.Axis : plane.Axis, -Vec3.UnitZ);
                var outward = OutwardNormal(m, plane);
                (ax, deg) = MeshAnalysis.Align(outward, -Vec3.UnitZ);
                res = MeshOps.Rotate(m, m.Center, ax, deg);
                res = MeshOps.Move(res, new Vec3(0, 0, -res.Min.Z));
                Row("Flat face", $"{plane.Area:#,0} mm²", 1);
                break;
            }
            case 4:
            {
                var (_, axes) = MeshAnalysis.Principal(m);
                var a2 = Vec3.Cross(axes[0], axes[1]);
                double[] R = new double[9]; Vec3[] A = [axes[0], axes[1], a2], B = [Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ];
                var c = m.Centroid();
                res = m.Transformed(p => { var d = p - c; return new Vec3(Vec3.Dot(d, axes[0]), Vec3.Dot(d, axes[1]), Vec3.Dot(d, a2)); });
                break;
            }
            default:
            {
                if (_ref.Items.Count == 0) { Row("Reference", "pick the CAD body or mesh to align to", 2); return null; }
                var refObj = S.Find(_ref.Items[0].Obj); if (refObj == null || refObj.Id == obj.Id) return null;
                var icp = Cached(Key(obj.Id, m.TriCount, refObj.Id, _iter.Value, _trim.Value, _pre.Value), () =>
                    MeshAnalysis.Icp(m, S.PrintMesh(refObj) ?? throw new InvalidOperationException("The reference has no surface."), _iter.Value, _trim.Value / 100, _pre.Value));
                if (icp == null) return null;
                res = m.Transformed(p => MeshAnalysis.Apply(icp.Transform, p));
                Row("Fit error before", N(icp.RmsBefore, "mm")); Row("Fit error after", N(icp.RmsAfter, "mm"), icp.RmsAfter < icp.RmsBefore ? 1 : 2);
                break;
            }
        }
        var o = new CommandOutput { Label = "Align mesh", Icon = Icon };
        o.ReplaceMeshes.Add((obj, res));
        return o;
    }

    static Vec3 OutwardNormal(TriMesh m, FitRegion plane)
    {
        // average triangle normal of the region (the mesh already points outward)
        Vec3 s = Vec3.Zero; foreach (int t in plane.Tris) s += m.FaceNormal(t) * m.TriArea(t);
        return s.Normalized();
    }
}

// ================================================================= Convert
public sealed record SolidResult(TriMesh Mesh, Shape Shape);

public sealed class MeshToSolidCommand : MeshCommand
{
    readonly ChoiceParam _result;
    readonly IntParam _limit;
    readonly NumberParam _tol;
    readonly BoolParam _keep;

    public MeshToSolidCommand() : base("Mesh")
    {
        _result = Choice("Create", 0, "Solid", "Surface shell");
        _limit = Add(new IntParam { Label = "Reduce to at most (triangles)", Value = 12000, Min = 100, Max = 200000 });
        _tol = Num("Sewing tolerance", 0.01, 0.0001, 5, 0.01);
        _keep = Check("Keep the mesh", true);
        PreviewButton();
    }
    public override string Title => "Mesh to Solid";
    public override string Hint => "Turns a mesh into a CAD body made of one flat face per triangle, so you can cut, combine and measure it. Large meshes are reduced first. For a clean parametric rebuild use Segment & Fit.";
    public override string Icon => "mesh-solid";
    public override string OkText => "Convert";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        var obj = Meshes().FirstOrDefault(); if (obj == null) return null;
        var m = obj.Tri!;
        var shape = Cached(Key(obj.Id, m.TriCount, _result.Index, _limit.Value, _tol.Value), () =>
        {
            var x = m.TriCount > _limit.Value ? MeshOps.Decimate(m, _limit.Value) : m;
            return new SolidResult(x, Occ.FromTriangles(x.V, x.T, _tol.Value, _result.Index == 0));
        });
        if (shape == null) { Row("Triangles", $"{Count(m.TriCount)} (limit {Count(_limit.Value)})"); return null; }
        Row("Triangles used", Count(shape.Mesh.TriCount));
        Row("Result", shape.Shape.HasSolids ? "Solid" : "Shell (not closed)", shape.Shape.HasSolids || _result.Index == 1 ? 1 : 2);
        var o = new CommandOutput { Label = "Mesh to solid", Icon = Icon };
        o.Add.Add(new(shape.Shape, obj.Name + " body", shape.Shape.HasSolids ? ObjKind.Solid : ObjKind.Surface, null, null, null, obj.Look));
        if (!_keep.Value) o.Remove.Add(obj);
        return o;
    }
}

public sealed class SolidToMeshCommand : Command
{
    readonly SelectionSlot _src;
    readonly ChoiceParam _detail;
    readonly NumberParam _defl;
    readonly BoolParam _keep;

    public SolidToMeshCommand()
    {
        _src = Slot(new SelectionSlot { Label = "Bodies", Filter = SelFilter.Solid | SelFilter.SurfaceObj | SelFilter.Form, Multi = true });
        _detail = Choice("Detail", 1, "Coarse (draft)", "Normal", "Fine (print quality)", "Custom");
        _defl = Num("Max chord error", 0.02, 0.0005, 10, 0.01);
        _keep = Check("Keep the original body", true);
    }
    public override string Title => "Solid to Mesh";
    public override string Hint => "Converts CAD bodies to triangle meshes so you can use the mesh tools (smooth, split, thickness, overhang) and export for printing.";
    public override string Icon => "mesh-from-solid";
    public override string OkText => "Convert";
    protected override void UpdateVisibility() { _defl.IsVisible = _detail.Index == 3; }

    protected override CommandOutput? Build()
    {
        if (!Complete) return null;
        var o = new CommandOutput { Label = "Solid to mesh", Icon = Icon };
        foreach (var obj in ObjectsOf(_src).Distinct())
        {
            double diag = obj.Mesh.Max.DistanceTo(obj.Mesh.Min);
            double d = _detail.Index switch { 0 => diag * 0.004, 1 => diag * 0.001, 2 => Math.Max(diag * 0.0002, 0.005), _ => _defl.Value };
            var md = obj.Shape.Tessellate(Math.Max(d, 1e-4));
            var tm = MeshAnalysis.FromMeshData(md);
            o.Add.Add(new(null, obj.Name + " mesh", ObjKind.Mesh, obj.Color, null, null, obj.Look, tm));
            if (!_keep.Value) o.Remove.Add(obj);
        }
        return o;
    }
}
