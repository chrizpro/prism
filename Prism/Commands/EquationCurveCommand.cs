using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>
/// A curve from a formula (x, y, z as functions of t; or cylindrical / spherical coordinates) or from a table of points.
/// The result is an ordinary curve object that sweeps, lofts and projects like any other.
/// </summary>
public sealed class EquationCurveCommand : Command
{
    readonly ChoiceParam _source;
    bool Table => _source.Index == 1;
    bool Helix => _source.Index == 2;
    // formula mode
    readonly ChoiceParam _system = null!, _angles = null!;
    readonly TextParam _e1 = null!, _e2 = null!, _e3 = null!;
    readonly NumberParam _t0 = null!, _t1 = null!;
    readonly IntParam _count = null!;
    // table mode
    readonly TextParam _rows = null!, _file = null!;
    readonly NumberParam _scale = null!;
    // shared
    readonly ChoiceParam _kind;
    readonly BoolParam _closed;
    readonly NumberParam _ox, _oy, _oz;
    // helix mode
    readonly NumberParam _radius = null!, _endRadius = null!, _pitch = null!, _turns = null!;
    readonly ChoiceParam _hand = null!, _axis = null!;

    /// <summary>Read the points from a sheet of the Data panel.</summary>
    public void UseSheet(string name) { _source.Index = 1; _rows.Value = "@" + name; OnChanged(); }

    public EquationCurveCommand(bool table)
    {
        _source = Choice("Points from", table ? 1 : 0, "Equation", "Table or file", "Helix");
        _system = Choice("Coordinates", 0, "Cartesian  x, y, z", "Cylindrical  r, θ, z", "Spherical  ρ, θ (from Z), φ");
        _e1 = Text("x  |  r  |  ρ", "20*cos(t*720)", "formula of t, e.g. 20*cos(t*360)");
        _e2 = Text("y  |  θ  |  θ", "20*sin(t*720)", "angles in degrees unless set below");
        _e3 = Text("z  |  z  |  φ", "40*t", "use pi, sqrt, abs, exp, ln, atan2, ...");
        _t0 = Num("t from", 0, step: 0.1, unit: "");
        _t1 = Num("t to", 1, step: 0.1, unit: "");
        _count = Add(new IntParam { Label = "Points", Min = 4, Max = 5000, Value = 120 });
        _angles = Choice("Angles in", 0, "Degrees", "Radians");
        _rows = Text("Points", "0,0,0; 20,10,5; 40,0,10; 60,-10,15", "x,y,z per point, separated by ; or new lines (x,y works too)");
        _file = Text("…or read a file or sheet", "", "a .csv, .txt or .xlsx file, or @ and the name of a sheet in the Data panel");
        _scale = Num("Scale", 1, 1e-6, 1e6, 0.1, "×");
        _kind = Choice("Curve", 0, "Smooth through the points", "NURBS control points", "Straight segments");
        _closed = Check("Closed", false);
        _ox = Num("Move X", 0); _oy = Num("Move Y", 0); _oz = Num("Move Z", 0);
        _radius = Num("Helix radius", 10, 0.001);
        _endRadius = Num("End radius (0 = same)", 0, 0);
        _pitch = Num("Pitch (rise per turn)", 5, 0.001);
        _turns = Num("Turns", 5, 0.05, 1000, 0.5, "");
        _hand = Choice("Handedness", 0, "Right-handed", "Left-handed");
        _axis = Choice("Axis", 2, "X", "Y", "Z");
        UpdateVisibility();
    }

    protected override void UpdateVisibility()
    {
        _system.IsVisible = _e1.IsVisible = _e2.IsVisible = _e3.IsVisible = _t0.IsVisible = _t1.IsVisible = _count.IsVisible = _angles.IsVisible = !Table;
        _rows.IsVisible = _file.IsVisible = _scale.IsVisible = Table;
        _system.IsVisible = _e1.IsVisible = _e2.IsVisible = _e3.IsVisible = _t0.IsVisible = _t1.IsVisible = _count.IsVisible = _angles.IsVisible = _source.Index == 0;
        _radius.IsVisible = _endRadius.IsVisible = _pitch.IsVisible = _turns.IsVisible = _hand.IsVisible = _axis.IsVisible = Helix;
    }

    /// <summary>A point of the helix about the +Z axis, turned onto the chosen axis (the cycle keeps its handedness) and moved by Move X, Y and Z.</summary>
    Vec3 Place(double x, double y, double z)
    {
        var p = _axis.Index switch { 0 => new Vec3(z, x, y), 1 => new Vec3(y, z, x), _ => new Vec3(x, y, z) };
        return p + new Vec3(_ox.Value, _oy.Value, _oz.Value);
    }

    List<Vec3> HelixPoints()
    {
        double r0 = _radius.Value, r1 = _endRadius.Value > 1e-9 ? _endRadius.Value : r0, sign = _hand.Index == 0 ? 1 : -1;
        int n = Math.Clamp((int)Math.Ceiling(_turns.Value * 24), 8, 4000) + 1;
        var pts = new List<Vec3>(n);
        for (int i = 0; i < n; i++)
        {
            double u = (double)i / (n - 1), a = 2 * Math.PI * _turns.Value * u, r = r0 + (r1 - r0) * u;
            pts.Add(Place(r * Math.Cos(a), sign * r * Math.Sin(a), _pitch.Value * _turns.Value * u));
        }
        return pts;
    }

    /// <summary>Pull the radius at the start, and the pitch at the far end (the end moves one turn's rise for each turn of pitch).</summary>
    public override IReadOnlyList<HandleSpec> Handles
    {
        get
        {
            if (!Helix) return [];
            Vec3 Dir(double x, double y, double z) => Place(x, y, z) - Place(0, 0, 0);
            return
            [
                new HandleSpec(Place(_radius.Value, 0, 0), Dir(1, 0, 0), _radius),
                new HandleSpec(Place(0, 0, _pitch.Value * _turns.Value), Dir(0, 0, 1), _pitch, 1 / _turns.Value),
            ];
        }
    }

    public override string Title => "Curve from Data";
    public override string Icon => "curve-through";
    public override string OkText => "Create curve";
    public override string Hint => Helix
        ? "A helix about the chosen axis: set the radius, the rise per turn (pitch) and the number of turns, and pull the arrows in the viewport. An end radius makes it a taper. Use it as a sweep path for springs and threads. Move X, Y and Z place its start."
        : Table
        ? "Type or paste points (x,y,z per row), or read a file (CSV, text or Excel) or a sheet of the Data panel with @ and its name. The curve passes through them, follows them as control points, or joins them with straight segments."
        : "Describe the curve with formulas of t. Cartesian, cylindrical and spherical coordinates are supported; t runs from the first value to the last, so a full turn of a helix is x=r*cos(t*360), y=r*sin(t*360) for t from 0 to 1.";

    List<Vec3> Points()
    {
        if (Helix) return HelixPoints();
        var pts = new List<Vec3>();
        if (Table)
        {
            string text = _rows.Value;
            if (!string.IsNullOrWhiteSpace(_file.Value))
            {
                if (!_file.Value.TrimStart().StartsWith('@') && !File.Exists(_file.Value)) throw new FormatException($"Cannot find the file {_file.Value}.");
                text = Prism.Core.Data.DataSource.Expand(S, _file.Value);
            }
            else if (Prism.Core.Data.DataSource.IsReference(text)) text = Prism.Core.Data.DataSource.Expand(S, text);
            foreach (var row in text.Split(['\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries))
            {
                var cols = row.Split([',', '\t', ' '], StringSplitOptions.RemoveEmptyEntries);
                var nums = new List<double>();
                foreach (var c in cols)
                {
                    if (double.TryParse(c, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) nums.Add(v);
                    else if (double.TryParse(c, NumberStyles.Float, CultureInfo.GetCultureInfo("de-DE"), out v) && cols.Length > 3) nums.Add(v);
                    else { nums.Clear(); break; }                       // a header line or text: skip the row
                }
                if (nums.Count < 2) continue;
                pts.Add(new Vec3(nums[0], nums[1], nums.Count > 2 ? nums[2] : 0) * _scale.Value);
            }
            if (pts.Count < 2) throw new FormatException("A curve needs at least two points (x,y,z per row).");
        }
        else
        {
            var f1 = Expression.Parse(_e1.Value); var f2 = Expression.Parse(_e2.Value); var f3 = Expression.Parse(_e3.Value);
            bool deg = _angles.Index == 0;
            double Rad(double a) => deg ? a * Math.PI / 180 : a;
            int n = _count.Value;
            for (int i = 0; i < n; i++)
            {
                double t = _t0.Value + (_t1.Value - _t0.Value) * i / (n - 1);
                double a = f1.Eval(t, deg), b = f2.Eval(t, deg), c = f3.Eval(t, deg);
                Vec3 p = _system.Index switch
                {
                    0 => new Vec3(a, b, c),
                    1 => new Vec3(a * Math.Cos(Rad(b)), a * Math.Sin(Rad(b)), c),
                    _ => new Vec3(a * Math.Sin(Rad(b)) * Math.Cos(Rad(c)), a * Math.Sin(Rad(b)) * Math.Sin(Rad(c)), a * Math.Cos(Rad(b))),
                };
                if (double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsNaN(p.Z) || double.IsInfinity(p.X) || double.IsInfinity(p.Y) || double.IsInfinity(p.Z))
                    throw new FormatException($"The formula has no value at t = {t:0.###} (division by zero, or the root or log of a negative number).");
                pts.Add(p);
            }
        }
        var move = new Vec3(_ox.Value, _oy.Value, _oz.Value);
        return pts.Select(p => p + move).ToList();
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        var pts = Points();
        // drop repeated points so the fit does not choke on them
        var clean = new List<Vec3>();
        foreach (var p in pts) if (clean.Count == 0 || clean[^1].DistanceTo(p) > 1e-9) clean.Add(p);
        if (clean.Count < 2) throw new FormatException("All the points are the same.");
        bool closed = _closed.Value && clean.Count > 2;
        if (closed && clean[0].DistanceTo(clean[^1]) < 1e-9) clean.RemoveAt(clean.Count - 1);
        Shape shape = _kind.Index switch
        {
            0 => Occ.Spline(clean, closed),
            1 => Occ.NurbsCurve(clean, Math.Min(3, clean.Count - 1)),
            _ => Occ.Polyline(clean, closed),
        };
        double len = 0; for (int i = 0; i + 1 < clean.Count; i++) len += clean[i].DistanceTo(clean[i + 1]);
        Results.Add(new ResultLine("Points", clean.Count.ToString()));
        Results.Add(new ResultLine("Approx. length", $"{len:0.##} mm"));
        var o = new CommandOutput();
        o.Add.Add(new(shape, Table ? "Table Curve" : "Equation Curve", ObjKind.Curve));
        o.Icon = "curve-through";
        return o;
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        // show the sample points of a table so a wrong column order is obvious
        if (!Table) return;
        try { foreach (var p in Points()) o.AddDot(p, 0xFFFF8A1F, 6); } catch { }
    }
}
