using Prism.Commands;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;

/// <summary>Extra lines in a sketch must not stop its closed outline from working as a profile.</summary>
static class SketchProfileTests
{
    static SketchModel Rectangle(double w, double h, out int[] corners)
    {
        var m = new SketchModel();
        corners = [m.AddPoint(0, 0), m.AddPoint(w, 0), m.AddPoint(w, h), m.AddPoint(0, h)];
        for (int i = 0; i < 4; i++) m.AddLine(corners[i], corners[(i + 1) % 4]);
        return m;
    }

    static (CadSession s, SceneObject sketch) Add(SketchModel m)
    {
        var s = new CadSession();
        var shape = Occ.Profile(m.ToShapes(Plane3.XY), faces: false);
        return (s, s.AddObject(shape, "Sketch", ObjKind.Sketch, plane: Plane3.XY, sketch: m));
    }

    static double Extrude(CadSession s, SceneObject sketch, double depth, int region = 0)
    {
        s.StartCommand(() => new ExtrudeCommand());
        var c = s.ActiveCommand!;
        c.Slots[0].Items.Clear(); c.Slots[0].TryAdd(SelItem.Of(sketch));
        c.Params.OfType<NumberParam>().First(p => p.Label.StartsWith("Distance") || p.Label.StartsWith("Depth")).Value = depth;
        if (region > 0) { var p = c.Params.First(x => x.Label.Contains("Region", StringComparison.OrdinalIgnoreCase)); if (p is IntParam ip) ip.Value = region; else ((NumberParam)p).Value = region; }
        c.OnChanged();
        var built = c.TryBuild() ?? throw new Exception("Extrude failed: " + c.Error);
        double volume = built.Add.Sum(a => a.Shape?.Props().Volume ?? 0);
        s.CancelCommand();
        return volume;
    }

    public static void Run(Action<bool, string> check)
    {
        bool Near(double a, double b) => Math.Abs(a - b) < b * 1e-3;

        var diag = Rectangle(60, 10, out var c1); diag.AddLine(c1[0], c1[2]);
        var (s1, k1) = Add(diag);
        check(Near(Extrude(s1, k1, 5), 60 * 10 * 5), "a diagonal across a rectangle does not stop it extruding: the whole outline comes out");
        check(Near(Extrude(s1, k1, 5, 1), 60 * 10 * 5 / 2.0), "and each side of the diagonal can be extruded on its own");

        var cross = Rectangle(40, 40, out _);
        cross.AddLine(cross.AddPoint(-10, 20), cross.AddPoint(50, 20));
        var (s2, k2) = Add(cross);
        check(Near(Extrude(s2, k2, 2), 40 * 40 * 2), "a line running right across the outline and past it is fine too");

        var stub = Rectangle(40, 20, out var c3); stub.AddLine(c3[0], stub.AddPoint(10, 10));
        var (s3, k3) = Add(stub);
        check(Near(Extrude(s3, k3, 3), 40 * 20 * 3), "a line that stops inside the outline is ignored");

        var withHole = Rectangle(60, 40, out var c4); withHole.AddLine(c4[0], c4[2]);
        withHole.AddCircle(withHole.AddPoint(30, 20), 5);
        var (s4, k4) = Add(withHole);
        double wantHole = (60 * 40 - Math.PI * 25) * 4;
        double gotHole = Extrude(s4, k4, 4);
        check(Math.Abs(gotHole - wantHole) < wantHole * .005, $"a circle stays a hole even with a line through the outline ({gotHole:0} against {wantHole:0} mm³)");

        var plainBox = Rectangle(30, 20, out _);
        var (s5, k5) = Add(plainBox);
        check(Near(Extrude(s5, k5, 3), 30 * 20 * 3), "a clean rectangle is unchanged");

        var open = new SketchModel(); int a = open.AddPoint(0, 0), b = open.AddPoint(10, 0), d = open.AddPoint(10, 10); open.AddLine(a, b); open.AddLine(b, d);
        var (s6, k6) = Add(open);
        bool refused = false; try { Extrude(s6, k6, 3); } catch (Exception e) { refused = e.Message.Length > 0; }
        check(refused, "an open shape is still refused");

        // the other tools read profiles the same way
        var (s7, k7) = Add(diag);
        var revolved = ProfileUtilProbe(s7, k7);
        check(Near(revolved, 60 * 10), "every tool that reads a profile sees the whole outline");
    }

    static double ProfileUtilProbe(CadSession s, SceneObject sketch)
    {
        s.StartCommand(() => new RevolveCommand());
        var c = s.ActiveCommand!;
        c.Slots[0].Items.Clear(); c.Slots[0].TryAdd(SelItem.Of(sketch));
        var face = ProfileUtil.Resolve(s, c.Slots[0], true).shape;
        s.CancelCommand();
        return face.Props().Area;
    }
}
