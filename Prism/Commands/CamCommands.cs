using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Prism.Core;
using Prism.Core.Cam;
using Prism.Kernel;

namespace Prism.Commands;

// =================================================================== CNC operations
/// <summary>Common tool and feed settings for a 2.5D machining operation, and the helpers that write moves.</summary>
public abstract class CncOperationCommand : Command
{
    protected readonly NumberParam Dia, Rpm, Feed, Plunge, StepDown, Clear, TopZ, Depth;
    protected readonly ChoiceParam ToolPreset;
    readonly List<double> _moves = [];
    readonly List<int> _starts = [];
    ToolpathInfo? _preview;

    public override void BuildOverlay(CadSession s, Overlay o) { if (_preview != null) s.DrawToolpath(o, _preview, false); }

    protected CncOperationCommand()
    {
        ToolPreset = Choice("Tool from the library", 0, ["Custom (my own values)", .. CamToolLibrary.Tools.Select(t => t.Name)]);
        var rebuild = ToolPreset.Changed;
        ToolPreset.Changed = () => { if (ToolPreset.Index > 0) ApplyTool(CamToolLibrary.Tools[ToolPreset.Index - 1]); rebuild?.Invoke(); };
        Dia = Num("Tool diameter", 6, 0.1, 100, 0.5);
        Rpm = Num("Spindle speed", 12000, 100, 60000, 500, "rpm");
        Feed = Num("Cutting feed", 800, 1, 20000, 50, "mm/min");
        Plunge = Num("Plunge feed", 300, 1, 10000, 25, "mm/min");
        StepDown = Num("Depth per pass", 2, 0.05, 100, 0.25);
        Clear = Num("Clearance above the material", 5, 0.5, 100, 1);
        TopZ = Num("Top of the material (Z)", 0, -1e5, 1e5, 1);
        Depth = Num("Total cutting depth", 3, 0.05, 1e4, 0.5);
    }

    protected override void OnBegin()
    {
        if(S.CamSetup is{} setup){var body=S.Find(setup.BodyId)??throw new InvalidOperationException("The CAM setup's model was deleted.");setup.Validate(body);Dia.Value=setup.ToolDiameter;Feed.Value=setup.Feed;Plunge.Value=setup.Plunge;Rpm.Value=setup.Rpm;Clear.Value=setup.Clearance;TopZ.Value=setup.StockMax[2];return;}
        // the top of the material defaults to the top of whatever is in the scene
        double top = double.MinValue;
        foreach (var o in S.Objects) if (o.Visible && !o.IsAnnotation && o.Kind is ObjKind.Solid or ObjKind.Surface) top = Math.Max(top, o.Mesh.Max.Z);
        if (top > double.MinValue) TopZ.Value = Math.Round(top, 3);
    }

    /// <summary>Fill in the size and speeds of a library tool. Operations that care about the cutter shape override this.</summary>
    protected virtual void ApplyTool(CamTool t) { Dia.Value = t.Diameter; Rpm.Value = t.Rpm; Feed.Value = t.Feed; Plunge.Value = t.Plunge; StepDown.Value = t.StepDown; }
    protected virtual string ToolName => $"{Dia.Value:0.##} mm end mill";

    public override string Icon => "cnc";
    public override string OkText => "Create toolpath";
    protected double Radius => Dia.Value / 2;
    protected double SafeZ => TopZ.Value + Clear.Value;

    // ---- move writing
    protected void Start() { _moves.Clear(); _starts.Clear(); }
    protected void Pass() => _starts.Add(_moves.Count / 4);
    protected void Go(double x, double y, double z, MoveKind k) { _moves.Add(x); _moves.Add(y); _moves.Add(z); _moves.Add((int)k); }
    protected (double x, double y, double z) Last => _moves.Count == 0 ? (0, 0, SafeZ) : (_moves[^4], _moves[^3], _moves[^2]);

    protected ToolpathInfo Seal(string operation)
    {
        if (_starts.Count == 0) _starts.Add(0);
        var inputs=Slots.SelectMany(s=>s.Items).Select(i=>i.Obj).Distinct().Select(id=>S.Find(id)).Where(o=>o!=null).ToList();
        if(inputs.Count==0)inputs.AddRange(S.Objects.Where(o=>o.Visible&&!o.IsAnnotation&&o.Kind is ObjKind.Solid or ObjKind.Surface));
        var sources=inputs.ToDictionary(o=>o!.Id,o=>Simulation.StudySetup.Signature(o!));
        return new ToolpathInfo(operation, ToolName, Dia.Value, Feed.Value, Plunge.Value, Rpm.Value, SafeZ, [.. _moves], [.. _starts], false, false,
            SetupBody:S.CamSetup?.BodyId,SourceSignature:S.CamSetup?.ShapeSignature,Sources:sources,SetupSignature:S.CamSetup?.Signature(),ProgramOrder:S.MillingProgram().Select(o=>((ToolpathInfo)o.Asm!).ProgramOrder).DefaultIfEmpty(-1).Max()+1,SetupName:S.CamSetup?.Name);
    }

    /// <summary>The depth of each pass from the top down to the full depth.</summary>
    protected IEnumerable<double> Levels()
    {
        double floor = TopZ.Value - Depth.Value; double z = TopZ.Value;
        while (z - StepDown.Value > floor + 1e-6) { z -= StepDown.Value; yield return z; }
        yield return floor;
    }

    protected CommandOutput Output(string name, ToolpathInfo tp)
    {
        _preview = tp;
        var o = new CommandOutput { Label = name, Icon = "cnc" };
        o.Add.Add(new(Occ.Line(Vec3.Zero, new Vec3(0, 0, 0.01)), name, ObjKind.Toolpath, Asm: tp));
        var (work, rapid) = tp.Lengths();
        Results.Add(new ResultLine("Cutting path", $"{work:#,0} mm"));
        Results.Add(new ResultLine("Rapid moves", $"{rapid:#,0} mm", 1));
        Results.Add(new ResultLine("Machining time", $"about {tp.EstimateMinutes():0.#} min"));
        return o;
    }

    /// <summary>Cut a closed loop at depth z, starting from the vertex nearest to the tool; tabs hold the part by staying higher.</summary>
    protected void CutLoop(List<Vec3> loop, double z, double tabTop, int tabs, double tabWidth, bool linked)
    {
        var (lx, ly, _) = Last;
        int best = 0; double bd = double.MaxValue;
        for (int i = 0; i < loop.Count; i++) { double d = (loop[i].X - lx) * (loop[i].X - lx) + (loop[i].Y - ly) * (loop[i].Y - ly); if (d < bd) { bd = d; best = i; } }
        var pts = new List<Vec3>(); for (int i = 0; i <= loop.Count; i++) pts.Add(loop[(best + i) % loop.Count]);
        if (!linked) { Go(pts[0].X, pts[0].Y, SafeZ, MoveKind.Rapid); Go(pts[0].X, pts[0].Y, Math.Min(SafeZ, TopZ.Value + 1), MoveKind.Rapid); }
        Go(pts[0].X, pts[0].Y, ZAt(0, z, tabTop, tabs, tabWidth, pts), MoveKind.Plunge);
        if (tabs <= 0 || z >= tabTop - 1e-9) { for (int i = 1; i < pts.Count; i++) Go(pts[i].X, pts[i].Y, z, MoveKind.Cut); return; }
        // with tabs: walk the loop and lift over each tab
        double total = CamGeom.Length(pts, false), s = 0;
        double[] centers = Enumerable.Range(0, tabs).Select(k => (k + 0.5) * total / tabs).ToArray();
        for (int i = 1; i < pts.Count; i++)
        {
            double seg = pts[i - 1].DistanceTo(pts[i]); var cuts = new List<double> { 0, 1 };
            foreach (var c in centers) foreach (var e in new[] { c - tabWidth / 2, c + tabWidth / 2 }) { double u = (e - s) / Math.Max(seg, 1e-12); if (u > 1e-6 && u < 1 - 1e-6) cuts.Add(u); }
            cuts.Sort();
            for (int k = 1; k < cuts.Count; k++)
            {
                double u = cuts[k], mid = s + (cuts[k - 1] + cuts[k]) / 2 * seg;
                bool inTab = centers.Any(c => Math.Abs(mid - c) < tabWidth / 2);
                double zz = inTab ? tabTop : z;
                var p = pts[i - 1] + (pts[i] - pts[i - 1]) * u;
                // step up or down at the tab edge
                var (_, _, lz) = Last; if (Math.Abs(lz - zz) > 1e-9) Go(Last.x, Last.y, zz, MoveKind.Plunge);
                Go(p.X, p.Y, zz, MoveKind.Cut);
            }
            s += seg;
        }
    }

    static double ZAt(double s, double z, double tabTop, int tabs, double w, List<Vec3> pts) => tabs > 0 && z < tabTop && w > 0 && (CamGeom.Length(pts, false) / tabs / 2) < w / 2 + 1e-9 ? tabTop : z;
}

/// <summary>Cut along or around outlines: outside, inside or on the line, with several depth passes and optional holding tabs.</summary>
public sealed class ProfileCommand : CncOperationCommand
{
    readonly SelectionSlot _src;
    readonly ChoiceParam _side, _dir;
    readonly IntParam _tabs;
    readonly NumberParam _tabW, _tabH;

    public ProfileCommand()
    {
        _src = Slot(new SelectionSlot { Label = "Outlines (curves, sketches, edges or flat faces)", Filter = SelFilter.CurveObj | SelFilter.SketchObj | SelFilter.Edge | SelFilter.Face, Multi = true });
        _side = Choice("Cut", 0, "Cut out the part (outside the outline, inside the holes)", "Outside the line", "Inside the line", "On the line");
        _dir = Choice("Direction", 0, "Climb", "Conventional");
        _tabs = Add(new IntParam { Label = "Holding tabs", Min = 0, Max = 12, Value = 0 });
        _tabW = Num("Tab width", 6, 0.5, 100, 0.5); _tabH = Num("Tab height", 2, 0.2, 50, 0.5);
        Depth.Value = 6; StepDown.Value = 2;
    }

    public override string Title => "CNC Profile";
    public override string Hint => "Pick closed outlines (sketches, curves or the edges of a flat face). The tool follows them outside, inside or exactly on the line, a few millimetres deeper on every pass. Add holding tabs to keep a cut-out part attached until the end.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_src.Items.Count == 0) return null;
        var loops = CamGeom.LoopsFromItems(S, _src.Items, out double z0);
        if (loops.Count == 0) throw new FormatException("Pick closed outlines.");
        Start();
        int count = 0; double tabTop = TopZ.Value - Depth.Value + _tabH.Value;
        foreach (var l in loops)
        {
            var paths = new List<List<Vec3>>();
            // loops inside other loops are holes: a part is cut out by running outside its outline but inside its holes
            bool isHole = loops.Count(o => !ReferenceEquals(o, l) && Math.Abs(CamGeom.SignedArea(o)) > Math.Abs(CamGeom.SignedArea(l)) && CamGeom.Inside(o, l[0].X, l[0].Y)) % 2 == 1;
            bool outside = _side.Index switch { 0 => !isHole, 1 => true, _ => false };
            if (_side.Index == 3) paths.Add(l);
            else
            {
                var face = CamGeom.FaceFromLoops([l]) ?? throw new FormatException("That outline cannot be machined.");
                paths.AddRange(CamGeom.Offset(face, outside ? Radius : -Radius, z0));
                if (paths.Count == 0) throw new FormatException("The outline is too small for this tool inside the line. Use a smaller tool.");
            }
            foreach (var p0 in paths)
            {
                // climb milling: outside cuts run counter-clockwise, inside cuts clockwise
                bool wantCcw = _side.Index == 3 ? CamGeom.SignedArea(p0) > 0 : outside == (_dir.Index == 0);
                var p = CamGeom.SignedArea(p0) > 0 == wantCcw ? p0 : p0.AsEnumerable().Reverse().ToList();
                bool first = true;
                foreach (var z in Levels())
                {
                    Pass();
                    CutLoop(p, z, tabTop, _tabs.Value, _tabW.Value, !first);
                    first = false;
                }
                Go(Last.x, Last.y, SafeZ, MoveKind.Rapid);
                count++;
            }
        }
        return Output("CNC profile", Seal($"Profile ({_side.Selected.Split(' ')[0].ToLower()})"));
    }
}

/// <summary>Clear the inside of closed outlines (islands are left standing) with concentric passes.</summary>
public sealed class PocketCommand : CncOperationCommand
{
    readonly SelectionSlot _src;
    readonly NumberParam _step;

    public PocketCommand()
    {
        _src = Slot(new SelectionSlot { Label = "Outlines of the pocket (islands inside are left)", Filter = SelFilter.CurveObj | SelFilter.SketchObj | SelFilter.Edge | SelFilter.Face, Multi = true });
        _step = Num("Stepover", 45, 5, 95, 5, "% of tool");
    }

    public override string Title => "CNC Pocket";
    public override string Hint => "Pick the closed outlines of the area to clear. Loops inside another loop are islands that stay. The tool works inward in rings, one depth pass at a time.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_src.Items.Count == 0) return null;
        var loops = CamGeom.LoopsFromItems(S, _src.Items, out double z0);
        if (loops.Count == 0) throw new FormatException("Pick closed outlines.");
        var region = CamGeom.FaceFromLoops(loops) ?? throw new FormatException("Those outlines cannot be made into a pocket.");
        double step = Math.Max(0.05, Dia.Value * _step.Value / 100);
        // the rings, outermost first
        var rings = new List<List<List<Vec3>>>();
        for (int k = 0; k < 400; k++)
        {
            var ring = CamGeom.Offset(region, -(Radius + k * step), z0);
            if (ring.Count == 0) break;
            rings.Add(ring);
        }
        if (rings.Count == 0) throw new FormatException("The pocket is too narrow for this tool. Use a smaller tool.");
        Start();
        foreach (var z in Levels())
        {
            Pass(); bool first = true;
            foreach (var ring in rings)
                foreach (var loop in ring)
                {
                    var l = CamGeom.SignedArea(loop) < 0 ? loop : loop.AsEnumerable().Reverse().ToList();     // clockwise inside a pocket = climb
                    var (lx, ly, _) = Last;
                    double nearest = l.Min(p => Math.Sqrt((p.X - lx) * (p.X - lx) + (p.Y - ly) * (p.Y - ly)));
                    bool linked = !first && nearest < step * 2.2;
                    CutLoop(l, z, 0, 0, 0, linked);
                    first = false;
                }
            Go(Last.x, Last.y, SafeZ, MoveKind.Rapid);
        }
        Results.Add(new ResultLine("Rings", rings.Count.ToString()));
        return Output("CNC pocket", Seal("Pocket"));
    }
}

/// <summary>Clear a pocket with a path that keeps the cutter's load even: gentle sideways bites at the full depth of the flutes, with no slots and no sharp corners.</summary>
public sealed class AdaptiveClearCommand : CncOperationCommand
{
    readonly SelectionSlot _src;
    readonly NumberParam _load;
    readonly BoolParam _finish;

    public AdaptiveClearCommand()
    {
        _src = Slot(new SelectionSlot { Label = "Outlines of the pocket (islands inside are left)", Filter = SelFilter.CurveObj | SelFilter.SketchObj | SelFilter.Edge | SelFilter.Face, Multi = true });
        _load = Num("Sideways cut (load)", 12, 2, 40, 1, "% of tool");
        _finish = Check("Finish the walls with one pass", true);
        StepDown.Value = 6; Depth.Value = 12;
    }

    public override string Title => "CNC Adaptive Clearing";
    public override string Hint => "Pick the closed outlines of the area to clear (loops inside another loop are islands). The tool follows a path that keeps its sideways cut small and even, so you can cut deeper and faster without overloading the cutter. Pre-drill or ramp in at the start points: the tool plunges straight down. Use a cutter with flutes long enough for the depth per pass.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_src.Items.Count == 0) return null;
        var loops = CamGeom.LoopsFromItems(S, _src.Items, out double z0);
        if (loops.Count == 0) throw new FormatException("Pick closed outlines.");
        var region = CamGeom.FaceFromLoops(loops) ?? throw new FormatException("Those outlines cannot be made into a pocket.");
        var reach = CamGeom.Offset(region, -Radius, z0);
        if (reach.Count == 0) throw new FormatException("The pocket is too narrow for this tool. Use a smaller tool.");
        double bite = Dia.Value * _load.Value / 100;
        var plan = AdaptiveClearing.Plan(loops, reach, Radius, bite);
        if (plan.Count == 0) throw new FormatException("Nothing to clear.");
        Start();
        foreach (var z in Levels())
        {
            Pass();
            foreach (var st in plan)
            {
                if (st.Op == AdaptiveClearing.Op.Start)
                {
                    if (Last.z < SafeZ - 1e-9) Go(Last.x, Last.y, SafeZ, MoveKind.Rapid);
                    Go(st.X, st.Y, SafeZ, MoveKind.Rapid);
                    Go(st.X, st.Y, z, MoveKind.Plunge);
                }
                else Go(st.X, st.Y, z, MoveKind.Cut);
            }
            if (_finish.Value)
                foreach (var ring in reach)
                {
                    var l = CamGeom.SignedArea(ring) < 0 ? ring : ring.AsEnumerable().Reverse().ToList();
                    CutLoop(l, z, 0, 0, 0, false);
                }
            Go(Last.x, Last.y, SafeZ, MoveKind.Rapid);
        }
        int starts = plan.Count(p => p.Op == AdaptiveClearing.Op.Start);
        Results.Add(new ResultLine("Path steps", plan.Count.ToString("#,0")));
        Results.Add(new ResultLine("Plunges per depth", starts.ToString(), starts > 3 ? 1 : 0));
        return Output("CNC adaptive clearing", Seal("Adaptive clearing"));
    }
}

/// <summary>Turn a round part on a lathe: face the end, rough it down in passes, then take one finishing pass along the profile.</summary>
public sealed class TurnCommand : CncOperationCommand
{
    readonly SelectionSlot _src;
    readonly NumberParam _stock, _allow, _facing;
    readonly BoolParam _face, _rough, _finish;

    public TurnCommand()
    {
        _src = Slot(new SelectionSlot { Label = "Profile of the part (half section on the XZ plane)", Filter = SelFilter.SketchObj | SelFilter.CurveObj | SelFilter.Edge, Multi = true });
        _stock = Num("Stock diameter (0 = automatic)", 0, 0, 2000, 1);
        _facing = Num("Extra length to face off", 1, 0, 100, 0.5);
        _allow = Num("Finish allowance", 0.2, 0, 10, 0.1);
        _face = Check("Face the end", true); _rough = Check("Rough turn", true); _finish = Check("Finish turn", true);
        Dia.IsVisible = Depth.IsVisible = TopZ.IsVisible = false;
        StepDown.Value = 1.5; Feed.Value = 200; Plunge.Value = 100; Rpm.Value = 1500; Clear.Value = 2;
    }

    public override string Title => "CNC Turning";
    public override string Icon => "cnc";
    protected override string ToolName => "Turning tool";
    public override string Hint => "Sketch the half section of the part on the XZ plane: X is the radius, Z runs along the axis, and the end to be faced is at the highest Z. The tool starts at the front and cuts toward the chuck. Depth per pass is the radial depth of each roughing cut. Tool nose radius is not compensated: leave the allowance for it. Post the program for Mach3 or LinuxCNC; X is written as a diameter.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_src.Items.Count == 0) return null;
        var loops = new List<List<Vec3>>(); var closed = new List<bool>();
        foreach (var it in _src.Items)
        {
            var shape = it.Kind == SelKind.Object ? S.Find(it.Obj)?.Shape : S.ShapeOf(it);
            if (shape == null) continue;
            foreach (var (pts, isClosed) in CamGeom.Loops(shape)) { loops.Add(pts); closed.Add(isClosed); }
        }
        if (loops.Count == 0) throw new FormatException("Pick the profile sketch.");
        var profile = TurningProfile.FromLoops(loops, [.. closed]);
        double maxR = profile.MaxR, allow = _allow.Value, doc = StepDown.Value;
        double rs = _stock.Value > 0 ? _stock.Value / 2 : maxR + 3;
        if (rs < maxR + allow + 0.05) throw new FormatException("The stock is not bigger than the part. Increase the stock diameter.");
        double zFront = profile.ZMax + _facing.Value;
        // TopZ is hidden for turning; set it without firing its change handler, which would start a nested rebuild.
        var notify = TopZ.Changed; TopZ.Changed = null;
        try { TopZ.Value = zFront; } finally { TopZ.Changed = notify; }
        double rClear = rs + Clear.Value;
        Start();
        // 1. face the end: cut from the outside to the centre, a little deeper each time
        if (_face.Value && _facing.Value > 1e-6)
        {
            Pass();
            for (double z = zFront; ; )
            {
                z = Math.Max(profile.ZMax, z - doc);
                Go(rClear, 0, z + Clear.Value, MoveKind.Rapid);
                Go(rClear, 0, z, MoveKind.Rapid);
                Go(0, 0, z, MoveKind.Cut);
                Go(0, 0, z + 0.5, MoveKind.Cut);
                if (z <= profile.ZMax + 1e-9) break;
            }
        }
        // 2. rough turn: radial passes from the stock down toward the part, leaving the finish allowance
        int roughCuts = 0;
        if (_rough.Value)
        {
            Pass();
            double minR = profile.Outline(0.01).Where(p => p.r > 0).Select(p => p.r).DefaultIfEmpty(0).Min();
            for (double r = rs - doc; r > minR + allow - 1e-9 || roughCuts == 0 && r > 0; r -= doc)
            {
                double level = Math.Max(r, minR + allow);
                foreach (var (front, back) in profile.StretchesBelow(level - allow + 1e-9))
                {
                    Go(rClear, 0, front, MoveKind.Rapid);
                    Go(level, 0, front, MoveKind.Plunge);
                    Go(level, 0, back, MoveKind.Cut);
                    Go(level + 0.5, 0, back, MoveKind.Cut);
                    Go(rClear, 0, back, MoveKind.Rapid);
                    roughCuts++;
                }
                if (level <= minR + allow + 1e-9) break;
            }
        }
        // 3. finish turn along the profile
        if (_finish.Value)
        {
            Pass();
            var line = profile.Outline(0.004);
            Go(rClear, 0, line[0].z, MoveKind.Rapid);
            Go(line[0].r, 0, line[0].z, MoveKind.Plunge);
            for (int i = 1; i < line.Count; i++) Go(line[i].r, 0, line[i].z, MoveKind.Cut);
            Go(line[^1].r + 0.5, 0, line[^1].z, MoveKind.Cut);
            Go(rClear, 0, line[^1].z, MoveKind.Rapid);
        }
        Results.Add(new ResultLine("Largest diameter", $"{2 * maxR:0.###} mm"));
        Results.Add(new ResultLine("Roughing cuts", roughCuts.ToString()));
        return Output("CNC turning", Seal("Turning"));
    }
}

/// <summary>Finish a 3D shape with parallel passes that follow its top surface (a 3-axis raster finish).</summary>
public sealed class ParallelFinishCommand : CncOperationCommand
{
    readonly SelectionSlot _src;
    readonly ChoiceParam _shape;
    readonly NumberParam _step, _angle;

    public ParallelFinishCommand()
    {
        _src = Slot(new SelectionSlot { Label = "Model to finish", Filter = SelFilter.Solid });
        _shape = Choice("Cutter", 0, "Ball nose", "Flat end mill");
        _step = Num("Stepover", 10, 1, 95, 1, "% of tool");
        _angle = Num("Scan angle", 0, -90, 90, 5, "°");
        Dia.Value = 6; StepDown.IsVisible = Depth.IsVisible = false;
    }

    public override string Title => "CNC 3D Finish";
    public override string Hint => "Pick a solid. The tool runs back and forth in parallel lines over its top surface, following the shape down to its lowest point. A ball nose leaves a smooth surface at a small stepover; a flat end mill suits flatter shapes. Only surfaces the tool can reach from above are cut.";
    protected override string ToolName => $"{Dia.Value:0.##} mm {(_shape.Index == 0 ? "ball nose" : "end mill")}";
    protected override void ApplyTool(CamTool t) { base.ApplyTool(t); _shape.Index = t.Ball ? 0 : 1; }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_src.Items.Count == 0) return null;
        var obj = S.Find(_src.Items[0].Obj) ?? throw new FormatException("Pick a solid.");
        var mesh = obj.Mesh;
        if (mesh.Indices.Length == 0) throw new FormatException("That object has no surface to machine.");
        bool ball = _shape.Index == 0;
        var map = HeightMap.FromMesh(mesh, Math.Max(Dia.Value / 8, 0.1));
        map.Dilate(Radius, ball);
        double floor = mesh.Min.Z;
        if (mesh.Max.Z > SafeZ - 0.5) throw new FormatException("The top of the material is below the model. Raise 'Top of the material' so the tool can travel above the part.");
        double a = _angle.Value * Math.PI / 180; var d = new Vec3(Math.Cos(a), Math.Sin(a), 0); var p = new Vec3(-Math.Sin(a), Math.Cos(a), 0);
        double dmin = double.MaxValue, dmax = double.MinValue, pmin = double.MaxValue, pmax = double.MinValue;
        foreach (var x in new[] { mesh.Min.X, mesh.Max.X }) foreach (var y in new[] { mesh.Min.Y, mesh.Max.Y })
        {
            var q = new Vec3(x, y, 0); double t = Vec3.Dot(q, d), u = Vec3.Dot(q, p);
            dmin = Math.Min(dmin, t); dmax = Math.Max(dmax, t); pmin = Math.Min(pmin, u); pmax = Math.Max(pmax, u);
        }
        double step = Math.Max(0.05, Dia.Value * _step.Value / 100), sample = map.Cell;
        int lines = (int)Math.Floor((pmax - pmin) / step) + 1, samples = (int)Math.Floor((dmax - dmin) / sample) + 1;
        if ((long)lines * samples > 3_000_000) throw new FormatException("That would make too many moves. Use a larger stepover or a larger tool.");
        Start();
        int runs = 0;
        for (int line = 0; line < lines; line++)
        {
            double off = pmin + line * step;
            var run = new List<Vec3>(); var found = new List<List<Vec3>>();
            for (int k = 0; k < samples; k++)
            {
                double t = dmin + k * sample; var pos = d * t + p * off; double tip = map.Tip(pos.X, pos.Y);
                if (double.IsNaN(tip)) { if (run.Count > 0) { found.Add(run); run = []; } continue; }
                run.Add(new Vec3(pos.X, pos.Y, Math.Max(tip, floor)));
            }
            if (run.Count > 0) found.Add(run);
            if (line % 2 == 1) { found.Reverse(); foreach (var f in found) f.Reverse(); }       // zigzag: every other line runs back
            foreach (var f in found)
            {
                if (f.Count < 2) continue;
                Pass();
                Go(f[0].X, f[0].Y, SafeZ, MoveKind.Rapid);
                Go(f[0].X, f[0].Y, f[0].Z, MoveKind.Plunge);
                for (int i = 1; i < f.Count; i++)
                {
                    // drop points that lie on a straight, level stretch
                    bool keep = i == f.Count - 1 || Math.Abs(f[i].Z - f[i - 1].Z) > 0.002 || Math.Abs(f[i + 1].Z - f[i].Z) > 0.002;
                    if (keep) Go(f[i].X, f[i].Y, f[i].Z, MoveKind.Cut);
                }
                Go(Last.x, Last.y, SafeZ, MoveKind.Rapid);
                runs++;
            }
        }
        if (runs == 0) throw new FormatException("Nothing to machine: the tool cannot reach the model from above.");
        Results.Add(new ResultLine("Passes", runs.ToString()));
        return Output("CNC 3D finish", Seal($"3D finish ({(ball ? "ball nose" : "flat")})"));
    }
}

/// <summary>Drill holes: pick the round faces or circular edges of holes.</summary>
public sealed class DrillCommand : CncOperationCommand
{
    readonly SelectionSlot _src;
    readonly BoolParam _holeDepth;
    readonly NumberParam _peck;

    public DrillCommand()
    {
        _src = Slot(new SelectionSlot { Label = "Holes (round faces or circular edges)", Filter = SelFilter.Face | SelFilter.Edge, Multi = true });
        _holeDepth = Check("Drill each hole as deep as it is", true);
        _peck = Num("Peck depth (0 = straight in)", 0, 0, 100, 0.5);
        Dia.Value = 5; Feed.Value = 400; Plunge.Value = 150; Rpm.Value = 8000;
    }

    public override string Title => "CNC Drill";
    public override string Hint => "Pick holes by their round faces or circular edges. Each is drilled at its centre. With a peck depth the drill backs out between pecks to clear the chips.";

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_src.Items.Count == 0) return null;
        var pts = new List<(double x, double y, double zTop, double zBot, double dia)>();
        foreach (var it in _src.Items)
        {
            var sh = S.ShapeOf(it); if (sh == null) continue;
            var ax = Occ.AxisOf(sh) ?? throw new FormatException("One of the picks is not round.");
            if (Math.Abs(ax.Dir.Normalized().Z) < 0.99) throw new FormatException("Only holes drilled straight down (along Z) can be drilled here.");
            double zt = TopZ.Value, zb = TopZ.Value - Depth.Value, dia = 0;
            var o = S.Find(it.Obj);
            if (o != null && it.Kind == SelKind.Face)
            {
                var info = Occ.FaceInfoOf(sh); dia = info.Type == "Cylinder" ? info.R1 * 2 : 0;
                if (_holeDepth.Value) { var m = o.Mesh; double lo = double.MaxValue, hi = double.MinValue; for (int t = m.FaceTriStart[it.Index]; t < m.FaceTriStart[it.Index + 1]; t++) for (int c = 0; c < 3; c++) { double zz = m.Positions[3 * m.Indices[3 * t + c] + 2]; lo = Math.Min(lo, zz); hi = Math.Max(hi, zz); } zt = hi; zb = lo; }
            }
            else if (it.Kind == SelKind.Edge) { var ei = Occ.EdgeInfoOf(sh); dia = ei.Radius * 2; }
            pts.Add((ax.Origin.X, ax.Origin.Y, zt, zb, dia));
        }
        // visit the holes in a sensible order (always to the nearest next one)
        var order = new List<(double x, double y, double zTop, double zBot, double dia)>(); var left = pts.ToList(); double cx = 0, cy = 0;
        while (left.Count > 0) { var n = left.OrderBy(p => (p.x - cx) * (p.x - cx) + (p.y - cy) * (p.y - cy)).First(); order.Add(n); left.Remove(n); cx = n.x; cy = n.y; }
        Start(); Pass();
        foreach (var h in order)
        {
            double r0 = Math.Max(h.zTop, TopZ.Value) + 1;
            Go(h.x, h.y, SafeZ, MoveKind.Rapid); Go(h.x, h.y, r0, MoveKind.Rapid);
            double z = h.zTop;
            if (_peck.Value <= 0) Go(h.x, h.y, h.zBot, MoveKind.Plunge);
            else
            {
                while (z - _peck.Value > h.zBot + 1e-6) { z -= _peck.Value; Go(h.x, h.y, z, MoveKind.Plunge); Go(h.x, h.y, r0, MoveKind.Rapid); Go(h.x, h.y, z + 0.5, MoveKind.Rapid); }
                Go(h.x, h.y, h.zBot, MoveKind.Plunge);
            }
            Go(h.x, h.y, SafeZ, MoveKind.Rapid);
        }
        var diaTxt = string.Join(", ", order.Select(h => h.dia > 0 ? $"Ø{h.dia:0.##}" : "?").Distinct());
        Results.Add(new ResultLine("Holes", $"{order.Count}  ({diaTxt})"));
        if (order.Any(h => h.dia > 0 && Math.Abs(h.dia - Dia.Value) > 0.2)) Results.Add(new ResultLine("Note", $"The tool is Ø{Dia.Value:0.##} mm but the holes are {diaTxt} mm. Change the tool diameter to match.", 2));
        return Output("CNC drill", Seal("Drill"));
    }
}

/// <summary>Skim the top of the material flat with zigzag passes over a rectangle.</summary>
public sealed class FaceMillCommand : CncOperationCommand
{
    readonly NumberParam _x0, _x1, _y0, _y1, _step;
    readonly ChoiceParam _dir;

    public FaceMillCommand()
    {
        _x0 = Num("X from", 0); _x1 = Num("X to", 100); _y0 = Num("Y from", 0); _y1 = Num("Y to", 100);
        _step = Num("Stepover", 70, 5, 95, 5, "% of tool");
        _dir = Choice("Passes run along", 0, "X", "Y");
        Dia.Value = 12; Depth.Value = 1; StepDown.Value = 1;
    }

    public override string Title => "CNC Face";
    public override string Hint => "Flatten the top of the stock over a rectangle with side-by-side passes. The rectangle starts as the size of everything in the scene.";

    protected override void OnBegin()
    {
        base.OnBegin();
        if(S.CamSetup is{} setup){_x0.Value=setup.StockMin[0];_y0.Value=setup.StockMin[1];_x1.Value=setup.StockMax[0];_y1.Value=setup.StockMax[1];return;}
        Vec3 mn = new(double.MaxValue, double.MaxValue, 0), mx = new(double.MinValue, double.MinValue, 0); bool any = false;
        foreach (var o in S.Objects) if (o.Visible && !o.IsAnnotation && o.Kind is ObjKind.Solid or ObjKind.Surface) { var m = o.Mesh; mn = new(Math.Min(mn.X, m.Min.X), Math.Min(mn.Y, m.Min.Y), 0); mx = new(Math.Max(mx.X, m.Max.X), Math.Max(mx.Y, m.Max.Y), 0); any = true; }
        if (any) { _x0.Value = Math.Floor(mn.X); _x1.Value = Math.Ceiling(mx.X); _y0.Value = Math.Floor(mn.Y); _y1.Value = Math.Ceiling(mx.Y); }
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        double step = Math.Max(0.1, Dia.Value * _step.Value / 100), r = Radius;
        double xa = Math.Min(_x0.Value, _x1.Value) - r, xb = Math.Max(_x0.Value, _x1.Value) + r, ya = Math.Min(_y0.Value, _y1.Value) - r, yb = Math.Max(_y0.Value, _y1.Value) + r;
        bool alongX = _dir.Index == 0;
        Start();
        foreach (var z in Levels())
        {
            Pass(); bool fwd = true; bool first = true;
            double lo = alongX ? ya : xa, hi = alongX ? yb : xb;
            for (double c = lo; c <= hi + 1e-6; c += step)
            {
                double cc = Math.Min(c, hi);
                double a = alongX ? xa : ya, b = alongX ? xb : yb; if (!fwd) (a, b) = (b, a);
                double x1 = alongX ? a : cc, y1 = alongX ? cc : a, x2 = alongX ? b : cc, y2 = alongX ? cc : b;
                if (first) { Go(x1, y1, SafeZ, MoveKind.Rapid); Go(x1, y1, z, MoveKind.Plunge); first = false; }
                else Go(x1, y1, z, MoveKind.Cut);
                Go(x2, y2, z, MoveKind.Cut); fwd = !fwd;
                if (cc >= hi) break;
            }
            Go(Last.x, Last.y, SafeZ, MoveKind.Rapid);
        }
        return Output("CNC face", Seal("Face"));
    }
}

// =================================================================== G-code output
/// <summary>Write the program for the machine: every toolpath in the order it was made.</summary>
public sealed class CncPostCommand : Command
{
    readonly ChoiceParam _ctl, _zero;
    readonly TextParam _path;

    public CncPostCommand()
    {
        _ctl = Choice("Machine", 0, "GRBL (hobby routers and mills)", "Mach3", "LinuxCNC");
        _zero = Choice("Work zero (G54)", 2, "Lower-left corner of the model, top surface", "The model's own origin", "Saved CAM setup");
        _path = Text("Save to (blank = Documents\\Prism\\GCode)", "", "program file (e.g. part.nc) or a folder");
        Add(new ActionParam { Label = "Save G-code", Run = Save });
    }

    public override string Title => "Post G-code";
    public override string Icon => "gcode";
    public override string OkText => "Done";
    public override string Hint => "Writes one G-code program from all the machining toolpaths. Set your machine's work zero (G54) at the corner this tells you about. Always run the first part in the air or on scrap.";

    IEnumerable<ToolpathInfo> Paths() => (_zero.Index==2?S.ActiveMillingProgram():S.MillingProgram()).Select(o => (ToolpathInfo)o.Asm!);

    Vec3 Origin()
    {
        if(_zero.Index==2)return S.CamSetup?.Origin()??throw new InvalidOperationException("Create a CAM setup or choose an explicit work zero.");
        if (_zero.Index == 1) return Vec3.Zero;
        double mx = double.MaxValue, my = double.MaxValue, mz = double.MinValue; bool any = false;
        foreach (var o in S.Objects) if (o.Visible && !o.IsAnnotation && o.Kind is ObjKind.Solid or ObjKind.Surface) { var m = o.Mesh; mx = Math.Min(mx, m.Min.X); my = Math.Min(my, m.Min.Y); mz = Math.Max(mz, m.Max.Z); any = true; }
        return any ? new Vec3(mx, my, mz) : Vec3.Zero;
    }

    void Save()
    {
        try
        {
            var paths = Paths().ToList();
            if (paths.Count == 0) { S.Say("There are no machining toolpaths yet. Create a Profile, Pocket, Drill or Face first.", true); return; }
            foreach(var tp in paths)CamValidation.Validate(S,tp);
            if(paths.Select(p=>p.SetupSignature).Distinct().Count()>1)throw new InvalidOperationException("Post one setup at a time. Select its saved setup and use Saved CAM setup work zero.");
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Prism", "GCode"); Directory.CreateDirectory(dir);
            string given = _path.Value?.Trim().Trim('"') ?? "";
            string stamped = $"program_{DateTime.Now:yyyyMMdd_HHmmss}.nc";
            // A folder (existing, or written with a trailing slash) gets a timestamped program file inside it.
            bool isFolder = given.Length > 0 && (Directory.Exists(given) || given.EndsWith('/') || given.EndsWith('\\'));
            string file = given.Length == 0 ? Path.Combine(dir, stamped) : isFolder ? Path.Combine(given, stamped) : given;
            var parent = Path.GetDirectoryName(Path.GetFullPath(file)); if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            string title = S.FilePath != null ? Path.GetFileNameWithoutExtension(S.FilePath) : "Prism program";
            bool lathe = paths.All(p => p.Operation.StartsWith("Turning", StringComparison.Ordinal));
            if (!lathe && paths.Any(p => p.Operation.StartsWith("Turning", StringComparison.Ordinal))) { S.Say("Turning and milling toolpaths go to different machines. Hide one kind and post again.", true); return; }
            var text = lathe ? Post.Lathe(paths, (Controller)_ctl.Index, title) : Post.Cnc(paths, (Controller)_ctl.Index, Origin(), title);
            File.WriteAllText(file, text);
            S.Say($"Saved {file}  ({text.Count(c => c == '\n')} lines)");
        }
        catch (Exception e) { S.Say(e.Message, true); }
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        var paths = Paths().ToList(); var o = Origin();
        Results.Add(new ResultLine("Operations", paths.Count.ToString()));
        Results.Add(new ResultLine("Machining time", $"about {paths.Sum(p => p.EstimateMinutes()):0.#} min"));
        Results.Add(new ResultLine("Set work zero at", $"X {o.X:0.###}  Y {o.Y:0.###}  Z {o.Z:0.###}  (model coordinates)", 1));
        return new CommandOutput { NoOp = true };
    }
}

// =================================================================== 3D printing
/// <summary>Slice solids into layers and write G-code for an FDM printer.</summary>
public sealed class SliceCommand : Command
{
    readonly SelectionSlot _src;
    readonly ChoiceParam _preset;
    readonly NumberParam _layer, _first, _width, _infill, _speed, _first_speed, _travel, _retract, _bedX, _bedY, _fil;
    readonly IntParam _walls, _top, _bottom, _brim, _nozzle, _bed, _fan;
    readonly ChoiceParam _pattern;
    readonly TextParam _file;

    SliceSettings? _cachedFor; Slicer.Result? _result; string? _error;

    public SliceCommand()
    {
        _src = Slot(new SelectionSlot { Label = "Bodies to print (none picked = everything visible)", Filter = SelFilter.Solid, Multi = true, Optional = true });
        _preset = Choice("Quality preset", 1, "Draft  0.3 mm, fast", "Standard  0.2 mm", "Fine  0.12 mm", "Strong  0.2 mm, 5 walls, 40% infill");
        _layer = Num("Layer height", 0.2, 0.05, 1, 0.05); _first = Num("First layer height", 0.3, 0.05, 1, 0.05); _width = Num("Line width", 0.45, 0.2, 2, 0.05);
        _walls = Add(new IntParam { Label = "Walls", Min = 0, Max = 12, Value = 3 });
        _top = Add(new IntParam { Label = "Top layers", Min = 0, Max = 30, Value = 4 }); _bottom = Add(new IntParam { Label = "Bottom layers", Min = 0, Max = 30, Value = 4 });
        _infill = Num("Infill", 15, 0, 100, 5, "%"); _pattern = Choice("Infill pattern", 0, "Lines", "Grid");
        _brim = Add(new IntParam { Label = "Brim lines", Min = 0, Max = 30, Value = 0 });
        _speed = Num("Print speed", 50, 5, 300, 5, "mm/s"); _first_speed = Num("First layer speed", 20, 5, 100, 5, "mm/s"); _travel = Num("Travel speed", 150, 20, 500, 10, "mm/s");
        _retract = Num("Retraction", 1, 0, 10, 0.5); _nozzle = Add(new IntParam { Label = "Nozzle temperature (°C)", Min = 150, Max = 320, Value = 205 });
        _bed = Add(new IntParam { Label = "Bed temperature (°C)", Min = 0, Max = 120, Value = 60 }); _fan = Add(new IntParam { Label = "Cooling fan (%)", Min = 0, Max = 100, Value = 100 });
        _fil = Num("Filament diameter", 1.75, 1, 3, 0.05);
        _bedX = Num("Bed size X", 220, 20, 2000, 10); _bedY = Num("Bed size Y", 220, 20, 2000, 10);
        _file = Text("Save G-code to (blank = Documents\\Prism\\GCode)", "", "full path, e.g. part.gcode");
        _preset.Changed = ApplyPreset;
        Add(new ActionParam { Label = "Slice", Run = SliceNow });
        Add(new ActionParam { Label = "Save G-code", Run = Save });
    }

    public override string Title => "3D Print: Slice";
    public override string Icon => "print3d";
    public override string OkText => "Keep layers";
    public override string Hint => "Choose a quality preset, set your printer's bed size and press Slice. The layers appear in the viewport and the time and filament are estimated. Then Save G-code and open it in your printer's software or copy it to the machine. No supports are generated yet: orient the part so overhangs stay gentle.";

    void ApplyPreset()
    {
        switch (_preset.Index)
        {
            case 0: _layer.Value = 0.3; _first.Value = 0.3; _width.Value = 0.5; _walls.Value = 2; _top.Value = 3; _bottom.Value = 3; _infill.Value = 10; _speed.Value = 70; break;
            case 1: _layer.Value = 0.2; _first.Value = 0.3; _width.Value = 0.45; _walls.Value = 3; _top.Value = 4; _bottom.Value = 4; _infill.Value = 15; _speed.Value = 50; break;
            case 2: _layer.Value = 0.12; _first.Value = 0.2; _width.Value = 0.4; _walls.Value = 3; _top.Value = 6; _bottom.Value = 6; _infill.Value = 15; _speed.Value = 35; break;
            default: _layer.Value = 0.2; _first.Value = 0.3; _width.Value = 0.45; _walls.Value = 5; _top.Value = 5; _bottom.Value = 5; _infill.Value = 40; _speed.Value = 45; break;
        }
    }

    SliceSettings Settings() => new()
    {
        LayerHeight = _layer.Value, FirstLayer = _first.Value, LineWidth = _width.Value, Walls = _walls.Value, TopLayers = _top.Value, BottomLayers = _bottom.Value, BrimLines = _brim.Value,
        InfillPercent = _infill.Value, GridInfill = _pattern.Index == 1, PrintSpeed = _speed.Value, FirstLayerSpeed = _first_speed.Value, TravelSpeed = _travel.Value, Retract = _retract.Value,
        NozzleTemp = _nozzle.Value, BedTemp = _bed.Value, FanPercent = _fan.Value, FilamentDia = _fil.Value, BedX = _bedX.Value, BedY = _bedY.Value,
    };

    IEnumerable<SceneObject> Bodies() => _src.Items.Count > 0 ? _src.Items.Select(i => S.Find(i.Obj)).Where(o => o != null).Cast<SceneObject>().Distinct()
        : S.Objects.Where(o => o.Visible && o.Kind == ObjKind.Solid && o.HasBrep);

    void SliceNow()
    {
        try
        {
            var s = Settings();
            var meshes = Bodies().Select(b => b.Mesh).ToList();
            _result = Slicer.Slice(meshes, s, m => S.Say(m)); _cachedFor = s; _error = null;
            S.CamViewLayer = int.MaxValue;
            S.Say($"Sliced into {_result.Layers} layers. About {_result.MinutesEstimate / 60:0.#} h, {_result.FilamentMm / 1000:0.00} m of filament.");
        }
        catch (Exception e) { _result = null; _error = e.Message; S.Say(e.Message, true); }
        OnChanged();
    }

    void Save()
    {
        if (_result == null) { S.Say("Slice first, then save.", true); return; }
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Prism", "GCode"); Directory.CreateDirectory(dir);
            string file = string.IsNullOrWhiteSpace(_file.Value) ? Path.Combine(dir, $"print_{DateTime.Now:yyyyMMdd_HHmmss}.gcode") : _file.Value;
            File.WriteAllText(file, Post.Print(_result.Path, S.FilePath != null ? Path.GetFileNameWithoutExtension(S.FilePath) : "Prism print"));
            S.Say($"Saved {file}");
        }
        catch (Exception e) { S.Say(e.Message, true); }
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_error != null) Results.Add(new ResultLine("Problem", _error, 2));
        if (_result == null) { Results.Add(new ResultLine("Status", "Press Slice to compute the layers.")); return null; }
        var r = _result;
        Results.Add(new ResultLine("Layers", r.Layers.ToString()));
        Results.Add(new ResultLine("Print time", $"about {r.MinutesEstimate / 60:0.#} h  ({r.MinutesEstimate:0} min)"));
        Results.Add(new ResultLine("Filament", $"{r.FilamentMm / 1000:0.00} m   about {r.FilamentMm * Math.PI * Math.Pow(_fil.Value / 2, 2) / 1000 * 1.24:0.#} g (PLA)", 1));
        foreach (var n in r.Notes.Take(3)) Results.Add(new ResultLine("Note", n, 2));
        var o = new CommandOutput { Label = "Slice for printing", Icon = "print3d" };
        o.Add.Add(new(Occ.Line(Vec3.Zero, new Vec3(0, 0, 0.01)), "Print layers", ObjKind.Toolpath, Asm: r.Path));
        return o;
    }

    public override void BuildOverlay(CadSession s, Overlay o) { if (_result != null) s.DrawToolpath(o, _result.Path, false); }
    public override void OnCancel() { S.CamViewLayer = int.MaxValue; }
}

/// <summary>Slide through the layers (or depth passes) of the toolpaths to see how they build up.</summary>
public sealed class ToolpathViewCommand : Command
{
    readonly IntParam _layer;
    public ToolpathViewCommand() { _layer = Add(new IntParam { Label = "Show up to layer / pass", Min = 1, Max = 2000, Value = 2000 }); }
    public override string Title => "Layer View";
    public override string Icon => "toolpath";
    public override string OkText => "Done";
    public override string Hint => "Slide to see the toolpaths build up layer by layer (or depth pass by depth pass). Set it to the maximum to see everything.";
    protected override void OnBegin() { _layer.Value = S.CamViewLayer == int.MaxValue ? 2000 : S.CamViewLayer + 1; }
    protected override CommandOutput? Build() { S.CamViewLayer = _layer.Value >= 2000 ? int.MaxValue : _layer.Value - 1; S.Invalidate(); return new CommandOutput { NoOp = true }; }
}
