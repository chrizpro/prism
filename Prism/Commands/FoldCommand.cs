using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Prism.Core;
using Prism.Core.Sketching;
using Prism.Kernel;

namespace Prism.Commands;

/// <summary>
/// Fold a flat sheet along bend lines. The sketch is the flat blank: its outline (with any holes, slots and corner reliefs) and, drawn as construction lines,
/// where it bends. Each bend line is the middle of a bend zone as wide as the bend allowance; the zone becomes a cylindrical strip and the flat regions
/// between zones stay rigid, so parts bent in several directions (trays, boxes, brackets) can be made, and the sketch is the part's flat pattern.
/// </summary>
public sealed class FoldCommand : Command
{
    readonly SelectionSlot _sketch;
    readonly NumberParam _t, _r, _k, _angle;
    readonly TextParam _angles;
    readonly ChoiceParam _side;

    static double _lastT = 2, _lastR = 2;           // the rules stay for the next part

    public FoldCommand()
    {
        _sketch = Slot(new SelectionSlot { Label = "Flat sketch (outline and bend lines)", Filter = SelFilter.SketchObj });
        _t = Num("Thickness", _lastT, 0.01, 100, 0.5);
        _r = Num("Bend radius (inside)", _lastR, 0, 1000, 0.5);
        _k = Num("K-factor", 0.44, 0, 1, 0.01, "");
        _angle = Num("Bend angle", 90, 0.1, 180, 5, "°");
        _angles = Text("Angle of each bend (optional)", "", "Signed degrees in order, e.g. 90, -90, 180. Positive folds toward the sketch normal.");
        _side = Choice("The metal lies", 0, "On the normal side of the sketch", "Opposite the normal");
    }

    public override string Title => "Fold";
    public override string Icon => "fold";
    public override string Hint => "Pick a sketch of the flat sheet: draw its outline with normal lines, and each place it bends as a construction line across the sheet. Set the thickness, bend radius and angle: the sheet folds along those lines. The largest flat region stays where it is.";

    sealed record Bend(int Index, Vec3 P, Vec3 D, Vec3 W, double Theta, double Allowance);
    sealed record Zone(int Bend, int Low, int High, Shape Solid, double D0, double D1);

    static IEnumerable<Shape> Solids(Shape s) =>
        s.Kind == ShapeKind.Solid ? [s] : Enumerable.Range(0, s.Count(SubKind.Solid)).Select(i => s.Sub(SubKind.Solid, i));

    static Shape ToLocal(Shape s, Vec3 u, Vec3 d, Vec3 n, Vec3 origin) =>
        Occ.Transform(s, [u.X, u.Y, u.Z, d.X, d.Y, d.Z, n.X, n.Y, n.Z], new Vec3(-Vec3.Dot(u, origin), -Vec3.Dot(d, origin), -Vec3.Dot(n, origin)));

    /// <summary>Signed bend angles in degrees: the list overrides the default angle bend by bend; an empty entry keeps the default.</summary>
    static double[] Angles(string text, double fallback, int count)
    {
        var result = Enumerable.Repeat(fallback, count).ToArray();
        if (string.IsNullOrWhiteSpace(text)) return result;
        var parts = text.Split([',', ';'], StringSplitOptions.None);
        if (parts.Length > count) throw new InvalidOperationException($"{parts.Length} angles were typed but the sketch has {count} bend line{(count == 1 ? "" : "s")}.");
        for (int i = 0; i < parts.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(parts[i])) continue;
            if (!double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var a) || !double.IsFinite(a))
                throw new InvalidOperationException($"'{parts[i].Trim()}' is not an angle. Type degrees separated by commas, for example 90, -90.");
            result[i] = a;
        }
        return result;
    }

    protected override CommandOutput? Build()
    {
        if (_sketch.Items.Count == 0) return null;
        Results.Clear();
        _lastT = _t.Value; _lastR = _r.Value;
        double t = _t.Value, r = _r.Value, k = _k.Value;
        var obj = S.Find(_sketch.Items[0].Obj) ?? throw new InvalidOperationException("The sketch is gone.");
        if (obj.SketchData is not { } model || obj.SketchPlane is not { } plane) throw new InvalidOperationException("Pick a sketch.");
        if (model.Sections.Count > 0) throw new InvalidOperationException("Draw the flat sheet on one sketch plane.");
        var n = plane.Normal.Normalized();
        double z0 = _side.Index == 0 ? 0 : -t;

        // bend lines: the construction lines, in a steady order (left to right, then bottom to top)
        var lines = model.Entities.Where(e => e.Construction && e.Type == EntType.Line)
            .Select(e => (a: plane.PointAt(model.P(e.A).X, model.P(e.A).Y), b: plane.PointAt(model.P(e.B).X, model.P(e.B).Y)))
            .Where(l => l.a.DistanceTo(l.b) > 1e-6)
            .OrderBy(l => Math.Round(Vec3.Dot((l.a + l.b) * 0.5 - plane.Origin, plane.U), 3)).ThenBy(l => Math.Round(Vec3.Dot((l.a + l.b) * 0.5 - plane.Origin, plane.V), 3))
            .ToList();
        if (lines.Count == 0) throw new InvalidOperationException("Draw each bend as a construction line across the sheet in the same sketch. The normal lines make the outline.");
        if (lines.Count > 30) throw new InvalidOperationException("A sheet can have at most 30 bends.");
        var angles = Angles(_angles.Value, _angle.Value, lines.Count);
        var bends = new List<Bend>();
        for (int i = 0; i < lines.Count; i++)
        {
            double deg = angles[i];
            if (Math.Abs(deg) < 0.1 || Math.Abs(deg) > 180) throw new InvalidOperationException($"Bend {i + 1} has an angle of {deg:0.##}°. Use 0.1° to 180°, negative to fold the other way.");
            double theta = Math.Abs(deg) * Math.PI / 180;
            var d = (lines[i].b - lines[i].a).Normalized();
            bends.Add(new Bend(i, lines[i].a, d, Vec3.Cross(n, d).Normalized(), deg < 0 ? -theta : theta, SheetFold.BendAllowance(theta, r, k, t)));
        }

        var (prof, _) = ProfileUtil.Resolve(S, _sketch, true);
        if (prof.Count(SubKind.Face) != 1) throw new InvalidOperationException("Draw one closed outline for the sheet. Holes and slots inside it are fine.");
        var plate = Occ.Extrude(Occ.Translate(prof.Sub(SubKind.Face, 0), n * z0), n * t);
        var box = plate.Props(); double reach = (box.Max - box.Min).Length + 10;

        // each bend zone is a strip across the plate; the rest of the plate falls into flat regions
        var strips = bends.Select(b =>
        {
            var c = new[] { b.P - b.D * reach - b.W * (b.Allowance / 2), b.P + b.D * reach - b.W * (b.Allowance / 2), b.P + b.D * reach + b.W * (b.Allowance / 2), b.P - b.D * reach + b.W * (b.Allowance / 2) };
            return Occ.Extrude(Occ.Translate(Occ.Profile([Occ.Polyline(c, true)]), n * (z0 - 2)), n * (t + 4));
        }).ToList();
        var zoneSolids = new List<Shape>();
        for (int i = 0; i < bends.Count; i++)
        {
            var common = Occ.Common(plate, strips[i]);
            if (!Solids(common).Any()) throw new InvalidOperationException($"Bend {i + 1} does not cross the sheet. Extend its construction line over the outline.");
            zoneSolids.Add(common);
        }
        for (int i = 0; i < bends.Count; i++)
            for (int j = i + 1; j < bends.Count; j++)
                if (Solids(Occ.Common(zoneSolids[i], strips[j])).Any(overlap => Math.Abs(Occ.Mass(overlap).Volume) > 1e-6))
                    throw new InvalidOperationException($"Bends {i + 1} and {j + 1} cross inside the sheet. Bend lines may meet outside the outline but not on the metal.");
        var regions = plate;
        foreach (var strip in strips) regions = Occ.Cut(regions, strip);

        int KeyAt(Vec3 c) { int key = 0; for (int i = 0; i < bends.Count; i++) if (Vec3.Dot(c - bends[i].P, bends[i].W) > 0) key |= 1 << i; return key; }
        var cells = new Dictionary<int, List<Shape>>(); var cellVolume = new Dictionary<int, double>();
        foreach (var piece in Solids(regions))
        {
            var mass = Occ.Mass(piece); int key = KeyAt(mass.Cog);
            if (!cells.TryGetValue(key, out var list)) cells[key] = list = [];
            list.Add(piece); cellVolume[key] = cellVolume.GetValueOrDefault(key) + Math.Abs(mass.Volume);
        }
        if (cells.Count < 2) throw new InvalidOperationException("The bend lines do not divide the outline into flat regions.");

        // a zone's footprint must be a rectangle across the bend, or it cannot wrap onto a cylinder
        var zones = new List<Zone>();
        for (int i = 0; i < bends.Count; i++)
        {
            var b = bends[i];
            foreach (var comp in Solids(zoneSolids[i]))
            {
                int around = KeyAt(Occ.Mass(comp).Cog);
                int low = around & ~(1 << i), high = around | (1 << i);
                if (!cells.ContainsKey(low) || !cells.ContainsKey(high))
                    throw new InvalidOperationException($"Bend {i + 1} has no flat region on one side of it. Extend the outline past the bend line, or remove the line.");
                var local = ToLocal(comp, b.W, b.D, n, b.P); var lp = local.Props();
                double width = lp.Max.X - lp.Min.X, along = lp.Max.Y - lp.Min.Y;
                if (Math.Abs(width - b.Allowance) > 1e-4 * Math.Max(1, b.Allowance) || Math.Abs(Math.Abs(Occ.Mass(comp).Volume) - width * along * t) > 1e-4 * width * along * t)
                    throw new InvalidOperationException($"Bend {i + 1} runs through a hole, slot or notch, or ends inside the sheet. A bend zone ({b.Allowance:0.##} mm wide) must be a clean rectangle of metal: keep cut-outs clear of it and cut reliefs beside it.");
                zones.Add(new Zone(i, low, high, comp, lp.Min.Y, lp.Max.Y));
            }
        }

        // fold outward from the largest region, one bend at a time
        int baseKey = cellVolume.MaxBy(kv => kv.Value).Key;
        var frames = new Dictionary<int, Rigid> { [baseKey] = Rigid.Identity };
        var order = new List<(int parent, int child, int bend)>();
        var queue = new Queue<int>(); queue.Enqueue(baseKey);
        var found = new Dictionary<int, (int parent, int bend)>();
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            foreach (var group in zones.Where(z => z.Low == current || z.High == current).GroupBy(z => (z.Low, z.High, z.Bend)))
            {
                int other = group.Key.Low == current ? group.Key.High : group.Key.Low;
                if (found.TryGetValue(current, out var via) && via.parent == other && via.bend == group.Key.Bend) continue;      // the way we came
                if (frames.ContainsKey(other)) throw new InvalidOperationException("The bends form a closed loop, which cannot fold without tearing. Leave a gap in the outline.");
                var b = bends[group.Key.Bend];
                bool childOnPlus = other == group.Key.High;
                var u = childOnPlus ? b.W : -b.W; var d = childOnPlus ? b.D : -b.D;
                frames[other] = SheetFold.Child(frames[current], b.P, u, d, n, Math.Abs(b.Theta), b.Theta < 0 ? -1 : 1, r, k, t, z0);
                found[other] = (current, group.Key.Bend); order.Add((current, other, group.Key.Bend)); queue.Enqueue(other);
            }
        }
        if (frames.Count != cells.Count) throw new InvalidOperationException("Some flat regions are not joined to the rest by a bend. Check that every region touches a bend line.");

        // build the folded pieces: rigid regions and exact cylindrical strips for the zones
        double rEff = Math.Max(r, 1e-3);
        Shape Sector(Zone z, Vec3 u, double theta, int s)
        {
            var b = bends[z.Bend];
            double half = b.Allowance / 2, zIn = s > 0 ? z0 + t : z0;
            var start = b.P + b.D * z.D0;
            var centre = start - u * half + n * (zIn + s * rEff);
            Vec3 a0 = n * (-s), a1 = a0 * Math.Cos(theta) + u * Math.Sin(theta), am = a0 * Math.Cos(theta / 2) + u * Math.Sin(theta / 2);
            double ri = rEff, ro = rEff + t;
            var edges = new List<Shape>
            {
                Occ.Line(centre + a0 * ri, centre + a0 * ro), Occ.Arc3(centre + a0 * ro, centre + am * ro, centre + a1 * ro),
                Occ.Line(centre + a1 * ro, centre + a1 * ri), Occ.Arc3(centre + a1 * ri, centre + am * ri, centre + a0 * ri),
            };
            return Occ.Extrude(Occ.Profile(edges, true), b.D * (z.D1 - z.D0));
        }
        var pieces = new List<Shape>();
        foreach (var piece in cells[baseKey]) pieces.Add(piece);
        foreach (var (parent, child, bendIndex) in order)
        {
            var b = bends[bendIndex]; bool childOnPlus = (child & (1 << bendIndex)) != 0;
            var u = childOnPlus ? b.W : -b.W; int s = b.Theta < 0 ? -1 : 1;
            foreach (var z in zones.Where(z => z.Bend == bendIndex && ((z.Low == parent && z.High == child) || (z.Low == child && z.High == parent))))
                pieces.Add(Occ.Transform(Sector(z, u, Math.Abs(b.Theta), s), frames[parent].M, frames[parent].T));
            foreach (var piece in cells[child]) pieces.Add(Occ.Transform(piece, frames[child].M, frames[child].T));
        }
        var body = pieces[0];
        for (int i = 1; i < pieces.Count; i++) body = Occ.Fuse(body, pieces[i]);
        if (!body.IsValid || body.Count(SubKind.Solid) != 1)
            throw new InvalidOperationException("The folded pieces did not join into one solid. A flange may be folding into another one: use a smaller angle or leave more room.");

        Results.Add(new ResultLine("Bends", bends.Count.ToString()));
        Results.Add(new ResultLine("Blank", $"{box.Max.X - box.Min.X:0.###} × {box.Max.Y - box.Min.Y:0.###} × {box.Max.Z - box.Min.Z:0.###} mm bounding box of the flat sheet"));
        for (int i = 0; i < bends.Count; i++)
            Results.Add(new ResultLine($"Bend {i + 1}", $"{Math.Abs(bends[i].Theta) * 180 / Math.PI:0.##}° {(bends[i].Theta < 0 ? "away from the normal" : "toward the normal")}, {bends[i].Allowance:0.###} mm of flat for the bend"));
        var o = new CommandOutput { Label = "Fold", Icon = "fold" };
        o.Add.Add(new(body, "Folded sheet metal", ObjKind.Solid));
        return o;
    }
}
