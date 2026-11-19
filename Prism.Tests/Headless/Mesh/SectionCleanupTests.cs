using Avalonia.Headless.XUnit;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;
using Prism.Meshing;
using Xunit;
using static Prism.HeadlessTests.Mesh.MeshToolTests;
using static Prism.HeadlessTests.Support.ModelingScenario;

namespace Prism.HeadlessTests.Mesh;

/// <summary>Section Sketch cleanup (MSH-P04): noise-based tolerance, span refits that keep the profile connected, tangency and smooth fits.</summary>
/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/modeling/mesh-and-reverse-engineer/README.md">Mesh guide, Section Sketch cleanup.</seealso>
[Trait("Feature", "Mesh")]
[Trait("Layer", "Headless")]
public sealed class SectionCleanupTests
{
    const double Noise = 0.05;

    static PointerInfo Screen(CadSession session, Vec3 target)
    {
        var p = session.Camera.Project(target)!.Value;
        return new PointerInfo(p.x, p.y, true, false, false, false);
    }

    /// <summary>Samples every 0.5 mm along a path, pushed sideways by uniform noise of ±<see cref="Noise"/> mm.</summary>
    static MeshSectionSample[] Noisy(Func<double, (double x, double y)> at, double length, int seed = 5)
    {
        var random = new Random(seed);
        int n = (int)Math.Round(length / 0.5);
        var res = new List<MeshSectionSample>();
        for (int i = 0; i <= n; i++)
        {
            double t = length * i / n;
            var p = at(t); var q = at(Math.Min(t + 1e-4, length)); var r = at(Math.Max(t - 1e-4, 0));
            double dx = q.x - r.x, dy = q.y - r.y, l = Math.Sqrt(dx * dx + dy * dy);
            double off = i == 0 || i == n ? 0 : (random.NextDouble() * 2 - 1) * Noise;
            res.Add(new(p.x - dy / l * off, p.y + dx / l * off));
        }
        return [.. res];
    }

    /// <summary>A sketch on XY holding the trace and the given starting curves, viewed from above, with the fit tool open.</summary>
    static (CadSession Session, SketchModel Model, FitMeshSectionTool Tool) Open(MeshSectionSample[] trace, Action<SketchModel> curves, int option)
    {
        var session = new CadSession();
        var model = new SketchModel();
        model.MeshSectionTraces.Add(new MeshSectionTrace(false, trace));
        curves(model);
        session.EnterSketch(Plane3.XY, model, alignView: false);
        var sk = session.Sketch!;
        session.Camera.SetView(0, 90);
        session.Camera.Orthographic = true;
        session.Camera.Target = new Vec3(25, 10, 0);
        session.Camera.Distance = 90;
        var tool = new FitMeshSectionTool();
        sk.SetTool(session, tool);
        tool.SetOption(session, option);
        return (session, sk.Model, tool);
    }

    static double Deviation(string report) => double.Parse(report.Split(' ')[2], System.Globalization.CultureInfo.InvariantCulture);

    [AvaloniaFact(DisplayName = "MSH-H144 | Refitting a span replaces the short curves along it with one line that keeps both neighbours connected")]
    [Trait("TestId", "MSH-H144")]
    public void SpanRefitKeepsProfileConnected()
    {
        int left = 0, right = 0, top0 = 0, top1 = 0;
        var (session, model, tool) = Open(Noisy(t => (t, 20), 50), m =>
        {
            int a = m.AddPoint(0, 0); top0 = m.AddPoint(0, 20); top1 = m.AddPoint(50, 20); int b = m.AddPoint(50, 0);
            left = m.AddLine(a, top0); right = m.AddLine(top1, b);
            // the top span over-split into five short lines, the way a tight tolerance cuts a noisy scan
            int last = top0;
            for (int i = 1; i <= 5; i++) { int next = i == 5 ? top1 : m.AddPoint(10 * i, 20 + (i % 2 == 0 ? 0.04 : -0.04)); m.AddLine(last, next); last = next; }
        }, 0);
        Assert.Equal(7, model.Entities.Count);
        Assert.True(tool.PointerDown(session, Screen(session, new Vec3(0.3, 20, 0))));
        Assert.True(tool.PointerDown(session, Screen(session, new Vec3(49.7, 20, 0))));
        Assert.Equal(3, model.Entities.Count);
        var line = Assert.Single(model.Entities, e => e.Id != left && e.Id != right);
        Assert.Equal(new[] { top0, top1 }.Order(), new[] { line.A, line.B }.Order());
        Assert.True(Deviation(tool.LastResult) <= 2 * Noise, tool.LastResult);
        Assert.Contains("replaced 5 curves", tool.LastResult);
    }

    [AvaloniaFact(DisplayName = "MSH-H145 | An arc fitted from a line's end is a least-squares arc and is made tangent to that line")]
    [Trait("TestId", "MSH-H145")]
    public void ArcIsTangentToNeighbour()
    {
        // line along y = 0 to (30, 0), then a quarter circle of radius 20 about (30, 20) up to (50, 20)
        (double x, double y) Path(double t) => t <= 30 ? (t, 0) : (30 + 20 * Math.Sin((t - 30) / 20), 20 - 20 * Math.Cos((t - 30) / 20));
        double length = 30 + Math.PI * 10;
        int line = 0, joint = 0;
        var (session, model, tool) = Open(Noisy(Path, length, 9), m =>
        {
            joint = m.AddPoint(30, 0);
            line = m.AddLine(m.AddPoint(0, 0), joint);
        }, 1);
        Assert.True(tool.PointerDown(session, Screen(session, new Vec3(30, 0, 0))));
        Assert.True(tool.PointerDown(session, Screen(session, new Vec3(50, 20, 0))));
        var arc = Assert.Single(model.Entities, e => e.Type == EntType.Arc);
        Assert.Contains(joint, new[] { arc.B, arc.C });
        var centre = model.P(arc.A); var start = model.P(arc.B);
        AssertClose(20, Math.Sqrt(Math.Pow(start.X - centre.X, 2) + Math.Pow(start.Y - centre.Y, 2)), 0.1, "arc radius");
        Assert.Contains(model.Constraints, c => c.Type == CType.Tangent && c.Refs.Contains(arc.Id) && c.Refs.Contains(line));
        Assert.Contains("1 tangent constraint", tool.LastResult);
    }

    [AvaloniaFact(DisplayName = "MSH-H146 | A spline fit follows a noisy wave within its tolerance with few points")]
    [Trait("TestId", "MSH-H146")]
    public void SplineSmoothsNoise()
    {
        var (session, model, tool) = Open(Noisy(t => (t, 20 + 5 * Math.Sin(t / 50 * 2 * Math.PI)), 50, 11), _ => { }, 3);
        Assert.True(tool.PointerDown(session, Screen(session, new Vec3(0, 20, 0))));
        Assert.True(tool.PointerDown(session, Screen(session, new Vec3(50, 20, 0))));
        var spline = Assert.Single(model.Entities);
        Assert.Equal(EntType.Spline, spline.Type);
        Assert.InRange(spline.Pts.Count, 3, 10);
        Assert.True(Deviation(tool.LastResult) <= 0.25, tool.LastResult);
    }

    [AvaloniaFact(DisplayName = "MSH-H147 | Section Sketch estimates its tolerance from scan noise and fits a noisy box with four lines")]
    [Trait("TestId", "MSH-H147")]
    public void SectionToleranceFollowsNoise()
    {
        var box = MeshOps.Subdivide(BoxMesh(Vec3.Zero, 40, 30, 20), 5);
        var random = new Random(2);
        var v = (float[])box.V.Clone();
        for (int i = 0; i < v.Length; i++)
        {
            // move points across their faces only, so the edges stay sharp
            double c = v[i];
            bool onFace = Math.Abs(c) < 1e-4 || Math.Abs(c - (i % 3 == 0 ? 40 : i % 3 == 1 ? 30 : 20)) < 1e-4;
            if (!onFace) continue;
            bool edge = Enumerable.Range(0, 3).Count(k => { double w = v[i - i % 3 + k]; double size = k == 0 ? 40 : k == 1 ? 30 : 20; return Math.Abs(w) < 1e-4 || Math.Abs(w - size) < 1e-4; }) > 1;
            if (!edge) v[i] += (float)((random.NextDouble() * 2 - 1) * Noise);
        }
        var session = new CadSession();
        var scan = AddMesh(session, new TriMesh(v, box.T));
        var command = Begin(session, () => new MeshSectionSketchCommand());
        Pick(command, "Mesh", scan);
        SetNumber(command, "Offset from the plane", 10);
        BuildOutput(command);
        Assert.StartsWith("4 lines + 0 arcs", Result(command, "Elements"));
        Assert.Contains("scan noise about", Result(command, "Fit tolerance"));
        command.Params.OfType<BoolParam>().Single(p => p.Label.StartsWith("Estimate the fit tolerance")).Value = false;
        SetNumber(command, "Fit tolerance", 0.01);
        BuildOutput(command);
        Assert.False(Result(command, "Elements").StartsWith("4 lines + 0 arcs"), Result(command, "Elements"));
    }
}
