using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Prism.Core;
using Prism.Kernel;
using Prism.Meshing;
using Prism.Simulation;

namespace Prism.Commands;

/// <summary>Flow through a network of pipes and pumps, typed as rows or read from curves in the model.</summary>
public sealed class PipeNetworkCommand : AnalysisCommand
{
    readonly SelectionSlot _curves;
    readonly TextParam _pipes, _pumps, _pressures, _drawn, _heights;
    readonly NumberParam _density, _viscosity, _roughness, _bore;
    readonly Dictionary<string, Vec3> _points = [];
    public NetworkResult? Result { get; private set; }

    public PipeNetworkCommand()
    {
        _curves = Slot(new SelectionSlot { Label = "Curves to use as pipes (optional)", Filter = SelFilter.AnyCurve | SelFilter.Edge, Multi = true, Optional = true });
        _bore = Num("Diameter for picked curves", 25, .01, 100000, 1);
        Add(new ActionParam { Label = "Read picked curves", Run = ReadCurves });
        _pipes = Text("Pipes", "A, B, 5000, 40; B, C, 8000, 25; B, D, 8000, 25", "from, to, length mm, diameter mm, optional loss K. Separate rows with ; or new lines");
        _pumps = Text("Pumps", "", "from, to, head at no flow (m), flow at no head (L/min)");
        _pressures = Text("Known pressures", "A=200; C=0; D=0", "point=gauge pressure in kPa, such as a tank or the open air at 0");
        _drawn = Text("Flow drawn off", "", "point=L/min taken out; negative puts flow in");
        _heights = Text("Heights", "", "point=mm above the reference");
        _density = Num("Fluid density", 998.2, .001, 100000, 10, "kg/m³");
        _viscosity = Num("Dynamic viscosity", .001002, 1e-9, 10000, .0001, "Pa·s");
        _roughness = Num("Absolute roughness", .0015, 0, 1000, .001);
        _curves.Section = "Setup";
        foreach (var parameter in Params) parameter.Section = "Setup";
    }

    public override string Title => "Pipe Network";
    public override string Icon => "cae-network";
    public override string Hint => "Steady flow through pipes and pumps: how the flow splits, the pressure at each point and where a pump settles. Type rows, or pick curves and press Read picked curves. Give at least one point a known pressure. Pipes are round and full; flow is incompressible.";

    static string[] Rows(string text) => text.Split(['\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    static double Number(string s, string what) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : throw new FormatException($"{what}: \"{s}\" is not a number.");

    static Dictionary<string, double> Pairs(string text, double scale)
    {
        var res = new Dictionary<string, double>();
        foreach (var row in Rows(text))
        {
            var cells = row.Split('=', 2, StringSplitOptions.TrimEntries);
            if (cells.Length != 2) throw new FormatException($"\"{row}\" should look like A=100.");
            res[cells[0]] = Number(cells[1], cells[0]) * scale;
        }
        return res;
    }

    /// <summary>Each picked curve becomes a pipe. Curves that meet end to end share a point.</summary>
    void ReadCurves()
    {
        try
        {
            var ends = new List<(Vec3 p, string name)>(); _points.Clear(); var rows = new List<string>();
            string Point(Vec3 p)
            {
                foreach (var (at, name) in ends) if ((at - p).Length < .01) return name;
                string fresh = "P" + (ends.Count + 1); ends.Add((p, fresh)); _points[fresh] = p; return fresh;
            }
            foreach (var item in _curves.Items)
            {
                var pts = MotionPicks.CurveOf(S, item); if (pts.Count < 2) continue;
                double length = 0; for (int i = 1; i < pts.Count; i++) length += (pts[i] - pts[i - 1]).Length;
                rows.Add(string.Create(CultureInfo.InvariantCulture, $"{Point(pts[0])}, {Point(pts[^1])}, {length:0.###}, {_bore.Value:0.###}"));
            }
            if (rows.Count == 0) throw new FormatException("Pick the curves to use as pipes first.");
            _pipes.Value = string.Join("; ", rows);
            S.Say($"Read {rows.Count} pipes meeting at {ends.Count} points. Their names show in the view; set the known pressures.");
        }
        catch (Exception e) when (e is FormatException or KernelException) { S.Say(e.Message, true); }
    }

    protected override void Analyze()
    {
        Result = null;
        try
        {
            var pipes = new List<NetworkPipe>();
            foreach (var row in Rows(_pipes.Value))
            {
                var c = row.Split(',', StringSplitOptions.TrimEntries);
                if (c.Length is < 4 or > 5) throw new FormatException($"\"{row}\" should be from, to, length, diameter, and an optional loss K.");
                pipes.Add(new NetworkPipe(c[0], c[1], Number(c[2], "Length") / 1000, Number(c[3], "Diameter") / 1000, c.Length == 5 ? Number(c[4], "Loss K") : 0));
            }
            var pumps = new List<NetworkPump>();
            foreach (var row in Rows(_pumps.Value))
            {
                var c = row.Split(',', StringSplitOptions.TrimEntries);
                if (c.Length != 4) throw new FormatException($"\"{row}\" should be from, to, head at no flow, flow at no head.");
                pumps.Add(new NetworkPump(c[0], c[1], Number(c[2], "Head"), Number(c[3], "Flow") / 60000));
            }
            var r = PipeNetwork.Solve(pipes, pumps, new NetworkFluid(_density.Value, _viscosity.Value, _roughness.Value / 1000), Pairs(_pressures.Value, 1000), Pairs(_drawn.Value, 1 / 60000.0), Pairs(_heights.Value, .001));
            Result = r;
            foreach (var p in r.Pipes)
                Row($"{p.Pipe.From} → {p.Pipe.To}", $"{(p.Flow) * 60000:0.###} L/min   ·   {p.Velocity:0.###} m/s   ·   Re {p.Reynolds:#,0} ({p.Regime})   ·   loses {p.HeadLoss * _density.Value * 9.80665 / 1000:0.###} kPa", p.Regime == "transitional" ? 2 : 0);
            foreach (var p in r.Pumps) Row($"Pump {p.Pump.From} → {p.Pump.To}", $"{p.Flow * 60000:0.###} L/min at {p.Head:0.###} m head   ·   {p.Power:0.#} W into the fluid", 1);
            foreach (var (name, pressure) in r.Pressure.OrderBy(kv => kv.Key, StringComparer.Ordinal)) Row("Pressure at " + name, $"{pressure / 1000:0.###} kPa");
            foreach (var note in r.Notes.Distinct()) Row("Note", note, 2);
        }
        catch (Exception e) when (e is FormatException or ArgumentException or InvalidOperationException) { Row("Setup", e.Message, 2); }
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        foreach (var (name, p) in _points)
            o.AddLabel(p, Result != null && Result.Pressure.TryGetValue(name, out var v) ? $"{name}: {v / 1000:0.#} kPa" : name, true, 1);
    }
}

/// <summary>Basic 3D flow around a body, or through a body that stands for the fluid.</summary>
public sealed class FlowStudyCommand : AnalysisCommand
{
    static readonly string[] Fluids = ["Air (20 °C)", "Water (20 °C)", "Custom"];
    static readonly string[] Axes = ["+X", "−X", "+Y", "−Y", "+Z", "−Z"];
    static readonly Vec3[] AxisVectors = [Vec3.UnitX, -Vec3.UnitX, Vec3.UnitY, -Vec3.UnitY, Vec3.UnitZ, -Vec3.UnitZ];

    readonly SelectionSlot _body, _inlet, _outlet;
    readonly ChoiceParam _mode, _dir, _fluid, _detail, _show, _sliceAxis;
    readonly NumberParam _speed, _density, _viscosity, _slice;
    readonly BoolParam _lines;
    CancellationTokenSource? _cancel;
    string _status = "", _error = "";
    readonly List<Overlay.Tri> _tris = []; readonly List<Overlay.Line> _flowLines = [];
    public FlowResult? Result { get; private set; }

    public FlowStudyCommand()
    {
        _body = Slot(new SelectionSlot { Label = "Body", Filter = SelFilter.Solid });
        _inlet = Slot(new SelectionSlot { Label = "Inlet face", Filter = SelFilter.Face, Optional = true });
        _outlet = Slot(new SelectionSlot { Label = "Outlet face", Filter = SelFilter.Face, Optional = true });
        _mode = Choice("Flow", 0, "Around the body (wind or water tunnel)", "Through the body (the body is the fluid)");
        _dir = Choice("Flow direction", 0, Axes);
        _speed = Num("Speed", 1, .0001, 1000, .1, "m/s");
        _fluid = Choice("Fluid", 0, Fluids);
        _density = Num("Density", 1.204, .001, 100000, .1, "kg/m³");
        _viscosity = Num("Dynamic viscosity", 1.825e-5, 1e-9, 10000, 1e-6, "Pa·s");
        _detail = Choice("Detail", 1, "Quick (60 cells)", "Normal (100 cells)", "Fine (160 cells)");
        Add(new ActionParam { Label = "Run flow", Run = Start });
        Add(new ActionParam { Label = "Stop flow", Run = () => _cancel?.Cancel() });
        _show = Choice("Show", 0, "Speed", "Pressure");
        _sliceAxis = Choice("Slice across", 1, "X", "Y", "Z");
        _slice = Num("Slice position", 50, 0, 100, 5, "%");
        _lines = Check("Streamlines", true);
        _show.Changed = _sliceAxis.Changed = _slice.Changed = _lines.Changed = () => { Paint(); OnChanged(); };
        foreach (var slot in Slots) slot.Section = "Setup";
        foreach (var parameter in Params) parameter.Section = "Setup";
        _detail.Section = "Solve";
        foreach (var action in Params.OfType<ActionParam>()) action.Section = "Solve";
        foreach (var parameter in new Param[] { _show, _sliceAxis, _slice, _lines }) parameter.Section = "Results";
        _mode.HelpText = "Through uses the volume occupied by the fluid. Pick a solid representing the inside of the duct.";
    }

    public override string Title => "Flow Study";
    public override string Icon => "cae-flow";
    public override string Hint => "Basic 3D flow on a block grid, with no fluid mesh to make. Around: put a body in a wind or water tunnel and read drag. Through: model the fluid itself as a solid, like the inside of a duct, and pick its inlet and outlet faces. Trust it for slow, smooth flow. For fast, turbulent flow it is a picture of the flow, not a dependable number.";

    protected override void UpdateVisibility()
    {
        if (_lines == null) return;
        bool through = _mode.Index == 1;
        _inlet.IsVisible = _outlet.IsVisible = through; _dir.IsVisible = !through;
        _density.IsVisible = _viscosity.IsVisible = _fluid.Index == 2;
        _show.IsVisible = _sliceAxis.IsVisible = _slice.IsVisible = _lines.IsVisible = Result != null;
    }

    (double rho, double mu) Fluid() => _fluid.Index switch { 0 => (1.204, 1.825e-5), 1 => (998.2, 1.002e-3), _ => (_density.Value, _viscosity.Value) };

    /// <summary>The setup from the panel's picks.</summary>
    public FlowSetup Setup()
    {
        if (_body.Items.Count == 0) throw new InvalidOperationException("Pick the body first.");
        var body = S.Find(_body.Items[0].Obj) ?? throw new InvalidOperationException("The body no longer exists.");
        var (rho, mu) = Fluid();
        var surface = MeshAnalysis.FromMeshData(body.Mesh);
        var size = surface.Size;
        int cells = _detail.Index switch { 0 => 60, 1 => 100, _ => 160 };
        if (_mode.Index == 0)
        {
            var d = AxisVectors[_dir.Index];
            double across = Math.Max(Math.Abs(d.X) > .5 ? 0 : size.X, Math.Max(Math.Abs(d.Y) > .5 ? 0 : size.Y, Math.Abs(d.Z) > .5 ? 0 : size.Z));
            return new FlowSetup(surface, FlowMode.Around, d, _speed.Value, rho, mu, across, cells);
        }
        if (_inlet.Items.Count == 0 || _outlet.Items.Count == 0) throw new InvalidOperationException("Pick the inlet and outlet faces.");
        if (_inlet.Items[0].Obj != body.Id || _outlet.Items[0].Obj != body.Id) throw new InvalidOperationException("The inlet and outlet must be faces of the body.");
        TriMesh Face(SelItem it) => surface.Subset(Enumerable.Range(body.Mesh.FaceTriStart[it.Index], body.Mesh.FaceTriStart[it.Index + 1] - body.Mesh.FaceTriStart[it.Index]));
        var inlet = Face(_inlet.Items[0]); var outlet = Face(_outlet.Items[0]);
        double width = 2 * Math.Sqrt(inlet.Area() / Math.PI);
        return new FlowSetup(surface, FlowMode.Through, Vec3.UnitX, _speed.Value, rho, mu, width, cells, inlet, outlet);
    }

    void Start()
    {
        try
        {
            _error = ""; var setup = Setup();
            _cancel?.Cancel(); var cancel = _cancel = new CancellationTokenSource();
            Result = null; _status = "Starting…"; Paint(); OnChanged();
            _ = Task.Run(() =>
            {
                try
                {
                    var r = FlowSolver.Solve(setup, m => Dispatcher.UIThread.Post(() => { _status = m; if (ReferenceEquals(S.ActiveCommand, this)) OnChanged(); }), cancel.Token);
                    Dispatcher.UIThread.Post(() => { Show(r); _status = ""; });
                }
                catch (OperationCanceledException) { Dispatcher.UIThread.Post(() => { _status = "Stopped."; OnChanged(); }); }
                catch (Exception e) when (e is InvalidOperationException or ArgumentException) { Dispatcher.UIThread.Post(() => { _error = e.Message; _status = ""; OnChanged(); }); }
            });
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException) { _error = e.Message; S.Say(e.Message, true); OnChanged(); }
    }

    /// <summary>Shows a finished run. Tests run the solver themselves and hand it over here.</summary>
    public void Show(FlowResult r)
    {
        Result = r; Paint();
        if (ReferenceEquals(S?.ActiveCommand, this)) OnChanged();
        S?.Invalidate();
    }

    public override void OnEnd() => _cancel?.Cancel();

    static uint Ramp(double t)
    {
        t = Math.Clamp(t, 0, 1);
        double r = Math.Clamp(1.5 - Math.Abs(4 * t - 3), 0, 1), g = Math.Clamp(1.5 - Math.Abs(4 * t - 2), 0, 1), b = Math.Clamp(1.5 - Math.Abs(4 * t - 1), 0, 1);
        return 0xFF000000u | (uint)(r * 255) << 16 | (uint)(g * 255) << 8 | (uint)(b * 255);
    }

    /// <summary>The colored slice and the streamlines. Worked out here, once, because the view draws them every frame.</summary>
    void Paint()
    {
        _tris.Clear(); _flowLines.Clear();
        if (Result is not { } r) return;
        int axis = _sliceAxis.Index; int layers = axis == 0 ? r.Nx : axis == 1 ? r.Ny : r.Nz;
        int layer = Math.Clamp((int)(_slice.Value / 100 * (layers - 1)), 0, layers - 1);
        int na = axis == 0 ? r.Ny : r.Nx, nb = axis == 2 ? r.Ny : r.Nz;
        double lo = 0, hi = r.MaxSpeed;
        if (_show.Index == 1)
        {
            lo = double.MaxValue; hi = double.MinValue;
            for (int c = 0; c < r.Kind.Length; c++) if (r.Kind[c] == FlowSolver.Fluid) { lo = Math.Min(lo, r.Pressure[c]); hi = Math.Max(hi, r.Pressure[c]); }
        }
        (int i, int j, int k) Cell(int a, int b) => axis == 0 ? (layer, a, b) : axis == 1 ? (a, layer, b) : (a, b, layer);
        for (int b = 0; b < nb; b++)
            for (int a = 0; a < na; a++)
            {
                var (i, j, k) = Cell(a, b); if (!r.IsFluid(i, j, k)) continue;
                int c = r.Index(i, j, k);
                uint color = Ramp(((_show.Index == 0 ? r.SpeedAt(c) : r.Pressure[c]) - lo) / Math.Max(hi - lo, 1e-12));
                var centre = r.CellCentre(i, j, k); double h = r.Dx / 2;
                Vec3 Corner(double x, double y) => centre + (axis == 0 ? new Vec3(0, x, y) : axis == 1 ? new Vec3(x, 0, y) : new Vec3(x, y, 0));
                var p0 = Corner(-h, -h); var p1 = Corner(h, -h); var p2 = Corner(h, h); var p3 = Corner(-h, h);
                _tris.Add(new(p0, p1, p2, color)); _tris.Add(new(p0, p2, p3, color));
            }
        if (!_lines.Value) return;
        foreach (var seed in Seeds(r))
        {
            var line = r.Streamline(seed);
            for (int i = 1; i < line.Count; i++) _flowLines.Add(new(line[i - 1].p, line[i].p, Ramp(line[i].speed / Math.Max(r.MaxSpeed, 1e-12)), 1.6f));
        }
    }

    /// <summary>Streamlines start where the flow comes in.</summary>
    static IEnumerable<Vec3> Seeds(FlowResult r)
    {
        var cells = new List<int>();
        for (int c = 0; c < r.Kind.Length; c++) if (r.Kind[c] == FlowSolver.Inlet) cells.Add(c);
        var inflow = cells.Where(c => r.Ux[c] * r.Ux[c] + r.Uy[c] * r.Uy[c] + r.Uz[c] * r.Uz[c] > 0).ToList();
        int stride = Math.Max(1, inflow.Count / 40);
        for (int n = 0; n < inflow.Count; n += stride)
        {
            int c = inflow[n], k = c / (r.Nx * r.Ny), j = (c - k * r.Nx * r.Ny) / r.Nx, i = c - r.Nx * (j + r.Ny * k);
            yield return r.CellCentre(i, j, k);
        }
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        o.Tris.AddRange(_tris); o.Lines.AddRange(_flowLines);
    }

    protected override void Analyze()
    {
        if (_error.Length > 0) Row("Could not run", _error, 3);
        if (_status.Length > 0) Row("Status", _status);
        if (Result is not { } r) { if (_status.Length == 0 && _error.Length == 0) Row("Setup", "Pick the body, set the flow, then Run flow."); return; }
        Row("Grid", $"{r.Nx} × {r.Ny} × {r.Nz} cells of {r.Dx:0.###} mm   ·   {r.Steps:N0} steps", 1);
        Row("Settled", r.Settled ? "yes" : "no: it was still changing when the step limit ran out, so read this as a rough picture", r.Settled ? 1 : 2);
        Row("Reynolds number", $"{r.Reynolds:#,0}");
        if (r.SolvedReynolds < r.Reynolds * .98) Row("Solved at", $"Re {r.SolvedReynolds:#,0}: this grid cannot hold the real flow, so the fluid was run thicker. Read this as a picture, not a number.", 2);
        else if (r.Reynolds > 2000) Row("Flow", "likely turbulent: this solver has no turbulence model, so read this as a picture, not a number.", 2);
        Row("Peak speed", $"{r.MaxSpeed:0.###} m/s");
        if (r.FrontalArea > 0) { Row("Drag", $"{r.Drag:G4} N"); Row("Drag coefficient", $"{r.DragCoefficient:0.###} on {r.FrontalArea:#,0} mm² frontal area"); }
        else
        {
            Row("Pressure drop", $"{r.PressureDrop:G4} Pa");
            Row("Flow in / out", $"{r.FlowIn:0.###} / {r.FlowOut:0.###} L/min");
        }
        if (_show.Index == 0) Row("Colors", $"0 to {r.MaxSpeed:0.###} m/s");
    }
}
