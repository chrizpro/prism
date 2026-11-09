using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;
using Prism.Rendering;

namespace Prism.Help;

/// <summary>
/// Before/after pictures for the modeling, sketch, freeform, mesh and assembly tools. They are small
/// illustrations built from kernel shapes and hand-made meshes, not runs of the tools themselves.
/// </summary>
public static partial class ToolHelp
{
    static ToolHelpExample? ExampleButtons(ToolHelpTopic topic, string id)
        => Pictures(topic, id) is { } example ? new(example.Before with { WholeView = true }, example.After with { WholeView = true }) : null;

    static ToolHelpExample? Pictures(ToolHelpTopic topic, string id)
    {
        string b = topic.BeforeLabel, a = topic.AfterLabel;
        var own = new List<Shape>();
        Shape K(Shape s) { own.Add(s); return s; }
        try
        {
            switch (id)
            {
                // ---- datum ----
                case "sketch-point":
                    return new(Plain(K(Occ.Box(30, 22, 14)), b), Marked(K(Occ.Box(30, 22, 14)), K(Dots(new Vec3(5, 3, 14), new(-8, -4, 14), new(15, 0, 7))), a));
                case "origin-axis":
                    return new(Plain(K(Occ.Box(30, 22, 14)), b), Marked(K(Occ.Box(30, 22, 14)), K(Mv(Occ.Cylinder(0.4, 40), 15, 11, -13)), a));
                case "origin-plane":
                    return new(Plain(K(Occ.Box(30, 22, 14)), b), Marked(K(Occ.Box(30, 22, 14)), K(Mv(Occ.Box(42, 32, 0.3), 0, 0, 14)), a));
                case "construction-plane":
                    return new(Plain(K(Occ.Box(30, 22, 14)), b), Marked(K(Occ.Box(30, 22, 14)),
                        K(Join([Mv(Occ.Box(42, 32, 0.3), 0, 0, 18), Mv(Occ.Box(42, 32, 0.3), 0, 0, 23), Mv(Occ.Box(42, 32, 0.3), 0, 0, 28)])), a));

                // ---- curves ----
                case "helix":
                    return new(Lines(K(Occ.Line(new(0, 0, 0), new(0, 0, 24))), b),
                        Lines(K(Occ.Polyline(Enumerable.Range(0, 161).Select(i => { double t = i / 160.0; return new Vec3(8 * Math.Cos(t * 8 * Math.PI), 8 * Math.Sin(t * 8 * Math.PI), 24 * t); }).ToList(), false)), a));
                case "intersection-curve":
                {
                    var cyl = K(Occ.Cylinder(9, 24));
                    var plate = K(Occ.Rotate(K(Mv(Occ.Box(36, 36, 0.4), 0, 0, 12)), new(0, 0, 12), new(1, 0, 0), 20));
                    double s = Math.Sin(20 * Math.PI / 180), c = Math.Cos(20 * Math.PI / 180);
                    return new(Plain(K(Occ.Compound([cyl, plate])), b), Lines(K(Occ.Section(cyl, new(0, 0, 12), new(0, -s, c))), a));
                }
                case "project-curve":
                    return new(Marked(K(Occ.Box(30, 22, 10)), K(Occ.Circle(new(0, 0, 20), Vec3.UnitZ, 6)), b),
                        Marked(K(Occ.Box(30, 22, 10)), K(Occ.Circle(new(0, 0, 10.05), Vec3.UnitZ, 6)), a));
                case "isocurves":
                {
                    var surface = K(Occ.Loft([K(Occ.Arc3(new(-16, -12, 0), new(0, -12, 6), new(16, -12, 0))), K(Occ.Arc3(new(-16, 12, 0), new(0, 12, 10), new(16, 12, 0)))], false, false));
                    var cuts = new List<Shape>();
                    foreach (double x in new[] { -12.0, -6, 0, 6, 12 }) cuts.Add(Occ.Section(surface, new(x, 0, 0), Vec3.UnitX));
                    foreach (double y in new[] { -6.0, 0, 6 }) cuts.Add(Occ.Section(surface, new(0, y, 0), Vec3.UnitY));
                    return new(Plain(surface, b), Lines(K(Join(cuts)), a));
                }
                case "contour":
                {
                    var ball = K(Occ.Sphere(12));
                    return new(Plain(ball, b), Lines(K(Join(new[] { -9.0, -6, -3, 0, 3, 6, 9 }.Select(z => Occ.Section(ball, new(0, 0, z), Vec3.UnitZ)))), a));
                }
                case "duplicate-edge":
                {
                    var box = K(Occ.Box(30, 22, 14));
                    var mesh = box.Tessellate();
                    var (p, q) = EdgeEnds(mesh, 0);
                    var mid = (p + q) / 2; var away = new Vec3(mid.X, mid.Y, 0).Normalized() * 5;
                    return new(Src(mesh, b, [0], null, Blue), Marked(box, K(Occ.Line(p + away, q + away)), a));
                }
                case "offset-curve" or "offset":
                    return new(Lines(K(ArcXY(12)), b), Lines(K(Occ.Compound([K(ArcXY(12)), K(ArcXY(17))])), a));
                case "blend-curve":
                    return new(Lines(K(Lines2((-20, 0, -7, 0), (7, 6, 20, 10))), b),
                        Lines(K(Join([Lines2((-20, 0, -7, 0), (7, 6, 20, 10)), Occ.Spline([new Vec3(-7, 0, 0), new Vec3(-2, 0.6, 0), new Vec3(3, 4, 0), new Vec3(7, 6, 0)])])), a));
                case "join-curves":
                    return new(Lines(K(Join([Lines2((-16, -6, -4, 6), (-4, 6, 6, -4), (6, -4, 16, 6)), Dots(new Vec3(-4, 6, 0), new(6, -4, 0))])), b),
                        Lines(K(Occ.Polyline([new(-16, -6, 0), new(-4, 6, 0), new(6, -4, 0), new(16, 6, 0)], false)), a));
                case "explode-curve":
                    return new(Lines(K(Occ.Polyline([new(-12, -8, 0), new(12, -8, 0), new(12, 8, 0), new(-12, 8, 0)], true)), b),
                        Lines(K(Lines2((-12, -10.5, 12, -10.5), (14.5, -8, 14.5, 8), (12, 10.5, -12, 10.5), (-14.5, 8, -14.5, -8))), a));
                case "trim-curve":
                    return new(Lines(K(Lines2((-20, 0, 20, 0), (6, -8, 6, 8))), b), Lines(K(Lines2((-20, 0, 6, 0), (6, -8, 6, 8))), a));
                case "extend-curve":
                    return new(Lines(K(ArcOnCircle(14, 200, 270)), b), Lines(K(ArcOnCircle(14, 200, 340)), a));
                case "rebuild-curve":
                {
                    var lumpy = Enumerable.Range(0, 12).Select(i => new Vec3(-18 + i * 3.3, 5 * Math.Sin(i * 0.55) + (i % 2 == 0 ? 1.2 : -1.2), 0)).ToArray();
                    var smooth = new Vec3[] { new(-18, 0, 0), new(-6, 5, 0), new(6, 3, 0), new(18.3, -4, 0) };
                    return new(Lines(K(Join([Occ.Polyline(lumpy, false), Dots(lumpy)])), b), Lines(K(Join([Occ.Spline(smooth), Dots(smooth)])), a));
                }
                case "origin-point":
                    return new(Lines(K(ArcXY(18)), b),
                        Lines(K(Join([ArcXY(18), Dots(Enumerable.Range(0, 6).Select(i => Polar(18, -60 + i * 24)).ToArray())])), a));
                case "curve-comb":
                {
                    var pts = Enumerable.Range(0, 25).Select(i => -18 + i * 1.5).Select(x => new Vec3(x, 4 * Math.Sin(x / 6), 0)).ToArray();
                    var tips = new List<Vec3>(); var parts = new List<Shape> { Occ.Spline(pts) };
                    foreach (var p in pts)
                    {
                        double dy = 4 / 6.0 * Math.Cos(p.X / 6), ddy = -4 / 36.0 * Math.Sin(p.X / 6), len = Math.Sqrt(1 + dy * dy);
                        double k = ddy / (len * len * len);
                        var tip = p + new Vec3(-dy / len, 1 / len, 0) * (k * 60);
                        if ((tip - p).Length > 1e-3) parts.Add(Occ.Line(p, tip));
                        tips.Add(tip);
                    }
                    parts.Add(Occ.Polyline(tips, false));
                    return new(Lines(K(Occ.Spline(pts)), b), Lines(K(Join(parts)), a));
                }

                // ---- solid and direct edits ----
                case "emboss":
                    return new(Marked(K(Occ.Box(32, 22, 8)), K(Occ.Circle(new(0, 0, 8.05), Vec3.UnitZ, 6)), b),
                        Plain(K(Occ.Fuse(K(Occ.Box(32, 22, 8)), K(Mv(Occ.Cylinder(6, 3), 0, 0, 8)))), a));
                case "pattern-curve":
                {
                    var path = Occ.Arc3(new(-18, 0, 0), new(0, 10, 0), new(18, 0, 0));
                    double c = -11.2, r = Math.Sqrt(18 * 18 + c * c), a0 = Math.Atan2(-c, -18), a1 = Math.Atan2(-c, 18);
                    var cubes = Enumerable.Range(0, 5).Select(i => { double t = a0 + (a1 - a0) * i / 4; return Mv(Occ.Box(4, 4, 4), r * Math.Cos(t), c + r * Math.Sin(t), 0); });
                    return new(Marked(K(Mv(Occ.Box(4, 4, 4), -18, 0, 0)), K(Occ.Copy(path)), b), Marked(K(Join(cubes)), K(path), a));
                }
                case "pattern-fill":
                {
                    var bumps = new List<Shape> { Occ.Box(40, 28, 3) };
                    for (int i = 0; i < 5; i++) for (int j = 0; j < 3; j++) bumps.Add(Mv(Occ.Cylinder(2, 3), -15 + i * 7.5, -9 + j * 9, 3));
                    return new(Plain(K(Join([Occ.Box(40, 28, 3), Mv(Occ.Cylinder(2, 3), -15, -9, 3)])), b), Plain(K(Join(bumps)), a));
                }
                case "pattern-table":
                    return new(Plain(K(Mv(Occ.Box(4, 4, 4), -14, -8, 0)), b),
                        Plain(K(Join(new (double x, double y)[] { (-14, -8), (-2, 6), (9, -3), (15, 9), (-12, 10) }.Select(p => Mv(Occ.Box(4, 4, 4), p.x, p.y, 0)))), a));
                case "rib":
                {
                    var bracket = K(Occ.Fuse(K(Occ.Box(36, 20, 4)), K(Mv(Occ.Box(4, 20, 26), -16, 0, 0))));
                    var outline = K(Occ.Polyline([new(-14, -1.5, 4), new(8, -1.5, 4), new(-14, -1.5, 22)], true));
                    var rib = K(Occ.Extrude(K(Occ.Profile([outline])), new(0, 3, 0)));
                    return new(Marked(bracket, K(Occ.Line(new(8, 0, 4.05), new(-14, 0, 22))), b), Plain(K(Occ.Fuse(bracket, rib)), a));
                }
                case "mold":
                    return new(Marked(K(Occ.Box(36, 24, 16)), K(Mv(Occ.Box(12, 8, 6), 0, 0, 5)), b),
                        Plain(K(Join([Mv(Occ.Box(36, 24, 7), 0, 0, -6), Mv(Occ.Box(36, 24, 7), 0, 0, 14)])), a));
                case "splitbody":
                    return new(Marked(K(Occ.Box(30, 20, 14)), K(Mv(Occ.Box(0.3, 30, 22), 0, 0, -4)), b),
                        Plain(K(Join([Mv(Occ.Box(14, 20, 14), -8, 0, 0), Mv(Occ.Box(14, 20, 14), 8, 0, 0)])), a));
                case "split-face":
                {
                    var whole = Occ.Box(30, 20, 14).Tessellate();
                    var halves = K(Join([Mv(Occ.Box(15, 20, 14), -7.5, 0, 0), Mv(Occ.Box(15, 20, 14), 7.5, 0, 0)])).Tessellate();
                    return new(Src(whole, b, null, [FaceAtHeight(whole, 14)], Blue), Src(halves, a, null, [FaceAtHeight(halves, 14)], Blue));
                }
                case "pushpull":
                {
                    var low = K(Occ.Box(26, 20, 12)).Tessellate(); var high = K(Occ.Box(26, 20, 20)).Tessellate();
                    return new(Src(low, b, null, [FaceAtHeight(low, 12)], Blue), Src(high, a, null, [FaceAtHeight(high, 20)], Blue));
                }
                case "extend-face":
                    return new(Marked(K(Occ.Box(26, 20, 10)), K(Mv(Occ.Box(36, 30, 0.3), 0, 0, 20)), b),
                        Marked(K(Occ.Box(26, 20, 20)), K(Mv(Occ.Box(36, 30, 0.3), 0, 0, 20)), a));
                case "movehole":
                    return new(Plain(K(Occ.Cut(K(Occ.Box(40, 24, 6)), K(Mv(Occ.Cylinder(4, 8), -10, 0, -1)))), b),
                        Plain(K(Occ.Cut(K(Occ.Box(40, 24, 6)), K(Mv(Occ.Cylinder(4, 8), 10, 0, -1)))), a));
                case "replace-face":
                {
                    var target = K(Occ.Rotate(K(Mv(Occ.Box(36, 26, 0.3), 0, 0, 14)), new(0, 0, 14), Vec3.UnitY, -15));
                    var side = K(Occ.Polyline([new(-15, -10, 0), new(15, -10, 0), new(15, -10, 18), new(-15, -10, 10)], true));
                    return new(Marked(K(Occ.Box(30, 20, 12)), target, b), Plain(K(Occ.Extrude(K(Occ.Profile([side])), new(0, 20, 0))), a));
                }
                case "match-face":
                    return new(Plain(K(Join([Mv(Occ.Cylinder(8, 16), -12, 0, 0), Mv(Occ.Cylinder(4, 16), 12, 0, 0)])), b),
                        Plain(K(Join([Mv(Occ.Cylinder(8, 16), -12, 0, 0), Mv(Occ.Cylinder(8, 16), 12, 0, 0)])), a));
                case "merge-faces":
                    return new(Plain(K(Occ.Fuse(K(Mv(Occ.Box(15, 20, 12), -7.5, 0, 0)), K(Mv(Occ.Box(15, 20, 12), 7.5, 0, 0)))), b), Plain(K(Occ.Box(30, 20, 12)), a));
                case "edit-pattern":
                    return new(Plain(K(Occ.Cut(K(Occ.Box(44, 20, 6)), K(Join(new[] { -15.0, -5, 5, 15 }.Select(x => Mv(Occ.Cylinder(2.5, 8), x, 0, -1)))))), b),
                        Plain(K(Occ.Cut(K(Occ.Box(44, 20, 6)), K(Join(new[] { -12.0, 0, 12 }.Select(x => Mv(Occ.Cylinder(2.5, 8), x, 0, -1)))))), a));
                case "fill-cap":
                {
                    var box = K(Occ.Box(26, 20, 14));
                    return new(Plain(K(Occ.Shell(box, [FaceAtHeight(box.Tessellate(), 14)], -2)), b), Plain(box, a));
                }

                // ---- surfaces ----
                case "surface-trim":
                    return new(Marked(K(Sheet(-16, 16)), K(Mv(Occ.Box(0.3, 30, 14), 4, 0, -4)), b), Plain(K(Sheet(-16, 4)), a));
                case "surface-offset":
                    return new(Plain(K(Sheet(-16, 16)), b), Marked(K(Sheet(-16, 16)), K(Sheet(-16, 16, 4)), a));
                case "thicken":
                {
                    var sheet = K(Sheet(-16, 16));
                    return new(Plain(sheet, b), Plain(K(Occ.Thicken(sheet, 3)), a));
                }
                case "unsew":
                    return new(Plain(K(Occ.Box(24, 24, 16)), b), Plain(K(BoxFaces(24, 24, 16, 4)), a));
                case "knit":
                    return new(Plain(K(BoxFaces(24, 24, 16, 4)), b), Plain(K(Occ.Box(24, 24, 16)), a));
                case "untrim":
                    return new(Plain(K(Occ.Cut(K(Occ.Box(36, 24, 0.6)), K(Mv(Occ.Cylinder(6, 4), 0, 0, -1)))), b), Plain(K(Occ.Box(36, 24, 0.6)), a));
                case "extend-surface":
                    return new(Plain(K(Sheet(-10, 10)), b), Marked(K(Sheet(-10, 10)), K(Sheet(10, 18)), a));
                case "fill":
                {
                    var under = K(Occ.Profile([K(SheetArc(-16, 16, 0)), K(Occ.Line(SheetPoint(16, 0), SheetPoint(-16, 0)))]));
                    var solid = K(Occ.Extrude(under, new(0, 24, 0))).Tessellate();
                    return new(Plain(K(Join([Sheet(-16, 16), Mv(Occ.Box(34, 26, 0.3), 0, 0, -0.3)])), b), Src(solid, a, null, AllFaces(solid), Blue));
                }
                case "flatten":
                {
                    double length = SheetRadius * 2 * Math.Asin(16 / SheetRadius);
                    var flat = K(Occ.Polyline([new(-length / 2, -12, 0), new(length / 2, -12, 0), new(length / 2, 12, 0), new(-length / 2, 12, 0)], true));
                    return new(Plain(K(Sheet(-16, 16)), b), Plain(K(Occ.Profile([flat])), a));
                }
                case "patch":
                {
                    var front = K(Occ.Arc3(new(-16, -12, 0), new(0, -12, 6), new(16, -12, 0)));
                    var back = K(Occ.Arc3(new(-16, 12, 0), new(0, 12, 10), new(16, 12, 0)));
                    return new(Lines(K(Join([Occ.Copy(front), Occ.Copy(back), Occ.Line(new(-16, -12, 0), new(-16, 12, 0)), Occ.Line(new(16, -12, 0), new(16, 12, 0))])), b),
                        Plain(K(Occ.Loft([front, back], false, false)), a));
                }
                case "pipe":
                {
                    var path = K(Occ.Arc3(new(0, 0, 0), new(5.858, 0, 14.142), new(20, 0, 20)));
                    return new(Lines(path, b), Plain(K(Occ.Sweep(K(Occ.Circle(new(0, 0, 0), Vec3.UnitZ, 3)), path, true)), a));
                }
                case "match-surface":
                {
                    var kinked = K(Join([Occ.Extrude(K(Occ.Polyline([new(-20, -12, 0), new(0, -12, 0)], false)), new(0, 24, 0)),
                        Occ.Extrude(K(Occ.Polyline([new(0, -12, 0), new(16, -12, 10)], false)), new(0, 24, 0))]));
                    var smooth = K(Occ.Extrude(K(Occ.Spline([new Vec3(-20, -12, 0), new Vec3(-6, -12, 0), new Vec3(6, -12, 3), new Vec3(16, -12, 10)])), new(0, 24, 0)));
                    return new(Plain(kinked, b), Plain(smooth, a));
                }
                case "warp":
                    return new(Src(GridBox(36, 10, 10, 12, 3, 3), b),
                        Src(Renormal(Deform(GridBox(36, 10, 10, 12, 3, 3), p => Twist(p, (p.X + 18) / 36 * Math.PI / 2))), a));

                // ---- freeform cages ----
                case "form-plane":
                    return new(Spot(b), Cage(GridBox(36, 28, 0.6, 6, 4, 1, true), a));
                case "form-box":
                    return new(Spot(b), Cage(GridBox(24, 20, 16, 3, 3, 3, true), a));
                case "form-sphere":
                    return new(Spot(b), Cage(Spherize(GridBox(20, 20, 20, 4, 4, 4, true), 11), a));
                case "form-cylinder":
                    return new(Spot(b), Cage(Renormal(Deform(GridBox(20, 20, 24, 4, 4, 3, true), p =>
                    {
                        double r = Math.Sqrt(p.X * p.X + p.Y * p.Y);
                        return r < 1e-9 ? p : new(p.X * Math.Max(Math.Abs(p.X), Math.Abs(p.Y)) / r, p.Y * Math.Max(Math.Abs(p.X), Math.Abs(p.Y)) / r, p.Z);
                    })), a));
                case "form-torus":
                    return new(Spot(b), Plain(K(Occ.Torus(11, 4)), a));
                case "form-face":
                {
                    var corners = new Vec3[] { new(-10, -8, 0), new(10, -8, 0), new(10, 8, 0), new(-10, 8, 0) };
                    return new(Plain(K(Dots(corners)), b), Marked(K(Occ.Profile([K(Occ.Polyline(corners, true))])), K(Dots(corners)), a));
                }
                case "form-fill":
                    return new(Cage(DropWhere(GridBox(20, 20, 14, 2, 2, 2, true), c => c.Z > 13.99), b), Cage(GridBox(20, 20, 14, 2, 2, 2, true), a));
                case "form-flatten":
                    return new(Cage(Renormal(Deform(GridBox(30, 24, 6, 6, 5, 1, true), p => p.Z > 5.99 ? p with { Z = p.Z + 3 * Math.Sin(p.X / 5) * Math.Cos(p.Y / 6) } : p)), b),
                        Cage(GridBox(30, 24, 6, 6, 5, 1, true), a));
                case "form-inset":
                {
                    var after = GridBox(24, 24, 10, 3, 3, 1, true);
                    return new(Cage(GridBox(24, 24, 10, 1, 1, 1, true), b, [0]), Cage(after, a, [Patch(after, c => c.Z > 9.99 && Math.Abs(c.X) < 4 && Math.Abs(c.Y) < 4)]));
                }
                case "form-thicken":
                    return new(Cage(GridBox(30, 24, 0.3, 3, 3, 1, true), b), Cage(GridBox(30, 24, 5, 3, 3, 1, true), a));
                case "form-extrude-faces":
                {
                    var after = GridBox(20, 20, 22, 2, 2, 2, true);
                    return new(Cage(GridBox(20, 20, 10, 2, 2, 1, true), b, [0]), Cage(after, a, [Patch(after, c => c.Z > 21.99)]));
                }
                case "edit-points" or "sculpt":
                    return new(Cage(GridBox(36, 28, 0.6, 8, 6, 1, true), b),
                        Cage(Renormal(Deform(GridBox(36, 28, 0.6, 8, 6, 1, true), p => p with { Z = p.Z + 8 * Math.Exp(-(p.X * p.X + p.Y * p.Y) / 60) })), a));
                case "form-quality":
                {
                    var ring = K(Occ.Torus(11, 4));
                    return new(Plain(ring, b), Src(Colorize(ring.Tessellate(), (p, n) => Math.Abs(n.Z)), a));
                }
                case "edge-loop-slide":
                    return new(Cage(GridBox(30, 16, 12, 3, 1, 1, true), b), Cage(Deform(GridBox(30, 16, 12, 3, 1, 1, true), p => Math.Abs(p.X - 5) < 0.01 ? p with { X = 11 } : p), a));
                case "edge-loop-insert":
                    return new(Cage(GridBox(30, 16, 12, 2, 1, 1, true), b), Cage(GridBox(30, 16, 12, 4, 1, 1, true), a));
                case "edge-loop-select":
                {
                    var after = GridBox(30, 16, 12, 3, 2, 2, true);
                    return new(Cage(GridBox(30, 16, 12, 3, 2, 2, true), b), Cage(after, a, [Patch(after, c => Math.Abs(c.X) < 5)]));
                }
                case "form-subdivide":
                    return new(Cage(GridBox(24, 20, 14, 1, 1, 1, true), b), Cage(GridBox(24, 20, 14, 2, 2, 2, true), a));
                case "form-split-face":
                {
                    var after = GridBox(24, 20, 14, 2, 1, 1, true);
                    return new(Cage(GridBox(24, 20, 14, 1, 1, 1, true), b, [0]), Cage(after, a, [Patch(after, c => c.Z > 13.99 && c.X > 0)]));
                }
                case "form-smooth-points":
                    return new(Cage(Renormal(Deform(GridBox(36, 28, 4, 8, 6, 1, true), p => p.Z > 3.99 ? p with { Z = p.Z + 1.8 * Math.Sin(p.X * 1.7) * Math.Cos(p.Y * 2.3) } : p)), b),
                        Cage(Renormal(Deform(GridBox(36, 28, 4, 8, 6, 1, true), p => p.Z > 3.99 ? p with { Z = p.Z + 2 * Math.Cos(p.X / 12) } : p)), a));
                case "form-smooth-surface":
                    return new(Cage(GridBox(24, 20, 16, 1, 1, 1, true), b), Plain(K(Occ.Fillet(K(Occ.Box(24, 20, 16)), Enumerable.Range(0, 12).ToArray(), 5)), a));
                case "form-merge":
                    return new(Cage(GridBox(24, 20, 12, 2, 1, 1, true), b), Cage(GridBox(24, 20, 12, 1, 1, 1, true), a));
                case "form-unweld":
                    return new(Cage(GridBox(28, 20, 12, 2, 1, 1, true), b),
                        Cage(Merge(Shift(GridBox(14, 20, 12, 1, 1, 1, true), new(-9, 0, 0)), Shift(GridBox(14, 20, 12, 1, 1, 1, true), new(9, 0, 0))), a));
                case "form-weld":
                    return new(Cage(GridBox(24, 24, 14, 2, 2, 2, true), b, [0]), Cage(Renormal(Deform(GridBox(24, 24, 14, 2, 2, 2, true), p => p.Z > 13.99 ? new Vec3(0, 0, 20) : p)), a));
                case "form-crease":
                {
                    var box = K(Occ.Box(24, 20, 16));
                    var all = Enumerable.Range(0, 12).ToArray();
                    var top = EdgesWhere(box.Tessellate(), p => p.Z > 15.99);
                    var notTop = all.Except(top).ToArray();
                    return new(Plain(K(Occ.Fillet(box, all, 4)), b), Plain(K(Occ.Fillet(box, notTop, 4)), a));
                }
                case "form-bridge":
                {
                    var left = K(Mv(Occ.Box(12, 12, 12), -14, 0, 0)); var right = K(Mv(Occ.Box(12, 12, 12), 14, 0, 0));
                    return new(Plain(K(Occ.Compound([left, right])), b), Plain(K(Occ.Fuse(K(Occ.Fuse(left, right)), K(Mv(Occ.Box(17, 7, 7), 0, 0, 2.5)))), a));
                }
                case "form-delete":
                    return new(Cage(GridBox(20, 20, 14, 2, 2, 2, true), b, [0]), Cage(DropWhere(GridBox(20, 20, 14, 2, 2, 2, true), c => c.Z > 13.99), a));
                case "form-freeze":
                {
                    var before = GridBox(36, 20, 6, 8, 4, 1, true);
                    var after = Renormal(Deform(GridBox(36, 20, 6, 8, 4, 1, true), p => p.X > 0 ? p with { Z = p.Z + p.X * p.X / 30 } : p));
                    return new(Cage(before, b, [Patch(before, c => c.X < 0)]), Cage(after, a, [Patch(after, c => c.X < 0)]));
                }
                case "form-align":
                {
                    static double F(double x) => 4 * Math.Sin(x / 36 * Math.PI * 2);
                    var guide = Occ.Spline(Enumerable.Range(0, 9).Select(i => -18 + i * 4.5).Select(x => new Vec3(x, 10.5, 9 + F(x))).ToList());
                    var curve = K(guide).Tessellate();
                    var flat = Merge(GridBox(36, 20, 4, 8, 4, 1, true), curve);
                    var bent = Merge(Renormal(Deform(GridBox(36, 20, 4, 8, 4, 1, true), p => p.Z > 3.99 ? p with { Z = 9 + F(p.X) } : p)), curve);
                    return new(Src(flat, b, [1], null, Blue), Src(bent, a, [1], null, Blue));
                }
                case "form-scan":
                    return new(Src(Ball(12, 24, 16, Noise), b), Cage(Spherize(GridBox(20, 20, 20, 3, 3, 3, true), 12), a));

                // ---- NURBS ----
                case "nurbs-patch":
                {
                    Vec3 P(int i, int j) => new(-15 + i * 10, -12 + j * 8, 4 * Math.Sin(i * 1.3) * Math.Cos(j * 0.9));
                    var rows = Enumerable.Range(0, 4).Select(j => K(Occ.Spline(Enumerable.Range(0, 4).Select(i => P(i, j)).ToList()))).ToList();
                    return new(Plain(K(Dots(Enumerable.Range(0, 16).Select(k => P(k % 4, k / 4)).ToArray())), b), Plain(K(Occ.Loft(rows, false, false)), a));
                }
                case "more-control":
                {
                    static Vec3 Bend(Vec3 p) => p with { Z = p.Z + 6 * Math.Cos(p.X / 36 * Math.PI) };
                    return new(Cage(Renormal(Deform(GridBox(36, 28, 0.6, 2, 2, 1, true), Bend)), b), Cage(Renormal(Deform(GridBox(36, 28, 0.6, 8, 6, 1, true), Bend)), a));
                }

                // ---- mesh and scan ----
                case "mesh-from-solid":
                    return new(Plain(K(Occ.Sphere(12)), b), Src(Ball(12, 16, 10), a));
                case "mesh-offset":
                {
                    static double Wave(double x, double y) => 3 * Math.Sin(x / 6) * Math.Cos(y / 7);
                    return new(Src(SheetMesh(Wave, 0), b), Src(Merge(SheetMesh(Wave, 0), SheetMesh(Wave, 4)), a, null, [0], Blue));
                }
                case "mesh-reduce":
                    return new(Src(Ball(12, 40, 24), b), Src(Ball(12, 10, 6), a));
                case "mesh-smooth":
                    return new(Src(Ball(12, 24, 16, Noise), b), Src(Ball(12, 24, 16), a));
                case "mesh-solid":
                    return new(Src(Ball(12, 16, 10), b), Src(Ball(12, 16, 10), a, null, [0], Blue));
                case "mesh-select":
                {
                    var after = Ball(12, 20, 12);
                    return new(Src(Ball(12, 20, 12), b), Src(after, a, null, [Patch(after, c => c.Z > 6)], Blue));
                }
                case "mesh-reconstruct":
                    return new(Src(Cloud(12, 300), b), Src(Ball(12, 24, 16), a));
                case "mesh-align":
                {
                    static Vec3 Tilt(Vec3 p) { double t = 30 * Math.PI / 180; return new(p.X * Math.Cos(t) + (p.Z - 4) * Math.Sin(t), p.Y, -p.X * Math.Sin(t) + (p.Z - 4) * Math.Cos(t) + 16); }
                    var bed = GridBox(40, 30, 1, 1, 1, 1);
                    var before = Merge(bed, Renormal(Deform(GridBox(14, 10, 8, 3, 2, 2, true), Tilt)));
                    var after = Merge(GridBox(40, 30, 1, 1, 1, 1), Shift(GridBox(14, 10, 8, 3, 2, 2, true), new(0, 0, 1)));
                    int[] scan = Enumerable.Range(6, 6).ToArray();
                    return new(Src(before, b, null, scan, Blue), Src(after, a, null, scan, Blue));
                }
                case "mesh-section":
                {
                    var before = Merge(Ball(12, 24, 16), Shift(GridBox(34, 34, 0.3, 1, 1, 1), new(0, 0, 4)));
                    return new(Src(before, b, null, Enumerable.Range(1, 6).ToArray(), Blue), Lines(K(Occ.Circle(new(0, 0, 4), Vec3.UnitZ, Math.Sqrt(144 - 16))), a));
                }
                case "mesh-fit":
                {
                    var exact = K(Occ.Box(24, 16, 12)).Tessellate();
                    return new(Src(Renormal(Deform(GridBox(24, 16, 12, 6, 4, 3, true), p => p + new Vec3(0, 0, 0.5 * Math.Sin(p.X * 3.1 + p.Y * 2.3)))), b),
                        Src(exact, a, null, AllFaces(exact), Blue));
                }
                case "mesh-patch":
                {
                    var before = Ball(12, 24, 16);
                    var cap = K(Occ.Common(K(Occ.Sphere(12)), K(Mv(Occ.Box(30, 30, 8), 0, 0, 5)))).Tessellate();
                    return new(Src(before, b, null, [Patch(before, c => c.Z > 5)], Blue), Src(cap, a, null, AllFaces(cap), Blue));
                }
                case "mesh-deviation":
                    return new(Src(Ball(12, 24, 16, Noise), b), Src(Colorize(Ball(12, 24, 16, Noise), (p, n) => p.Length), a));

                // ---- sheet metal, ECAD ----
                case "flange":
                {
                    var side = K(Occ.Polyline([new(-12, -10, 0), new(12, -10, 0), new(12, -10, 14), new(10.5, -10, 14), new(10.5, -10, 1.5), new(-10.5, -10, 1.5), new(-10.5, -10, 14), new(-12, -10, 14)], true));
                    return new(Lines(K(Occ.Polyline([new(-12, 0, 14), new(-12, 0, 0), new(12, 0, 0), new(12, 0, 14)], false)), b),
                        Plain(K(Occ.Extrude(K(Occ.Profile([side])), new(0, 20, 0))), a));
                }
                case "fold":
                {
                    var side = K(Occ.Polyline([new(-12, -10, 0), new(12, -10, 0), new(12, -10, 14), new(10.5, -10, 14), new(10.5, -10, 1.5), new(-10.5, -10, 1.5), new(-10.5, -10, 14), new(-12, -10, 14)], true));
                    return new(Marked(K(Occ.Box(52, 20, 1.5)), K(Join([Occ.Line(new(-12, -10, 1.55), new(-12, 10, 1.55)), Occ.Line(new(12, -10, 1.55), new(12, 10, 1.55))])), b),
                        Plain(K(Occ.Extrude(K(Occ.Profile([side])), new(0, 20, 0))), a));
                }
                case "edge-flange":
                {
                    var side = K(Occ.Polyline([new(-12, -10, 0), new(12, -10, 0), new(12, -10, 14), new(10.5, -10, 14), new(10.5, -10, 1.5), new(-10.5, -10, 1.5), new(-10.5, -10, 0)], true));
                    return new(Plain(K(Occ.Box(24, 20, 1.5)), b), Plain(K(Occ.Extrude(K(Occ.Profile([side])), new(0, 20, 0))), a));
                }
                case "flat-pattern":
                {
                    var side = K(Occ.Polyline([new(-12, -10, 0), new(12, -10, 0), new(12, -10, 14), new(10.5, -10, 14), new(10.5, -10, 1.5), new(-10.5, -10, 1.5), new(-10.5, -10, 14), new(-12, -10, 14)], true));
                    return new(Plain(K(Occ.Extrude(K(Occ.Profile([side])), new(0, 20, 0))), b),
                        Marked(K(Occ.Box(52, 20, 1.5)), K(Join([Occ.Line(new(-12, -10, 1.55), new(-12, 10, 1.55)), Occ.Line(new(12, -10, 1.55), new(12, 10, 1.55))])), a));
                }
                case "pcb-board":
                    return new(Lines(K(Occ.Polyline([new(-25, -16, 0), new(25, -16, 0), new(25, 16, 0), new(-25, 16, 0)], true)), b), Board(K(Holes(K(Occ.Box(50, 32, 1.6)))), a));
                case "pcb-outline":
                {
                    var outline = new Vec3[] { new(-22, -14, 0), new(22, -14, 0), new(22, 2, 0), new(4, 2, 0), new(4, 14, 0), new(-22, 14, 0) };
                    return new(Lines(K(Occ.Polyline(outline, true)), b), Board(K(Occ.Extrude(K(Occ.Profile([K(Occ.Polyline(outline, true))])), new(0, 0, 1.6))), a));
                }
                case "pcb-hole":
                    return new(Board(K(Occ.Box(40, 26, 1.6)), b), Board(K(Occ.Cut(K(Occ.Box(40, 26, 1.6)), K(Mv(Occ.Cylinder(1.6, 4), 15, 9, -1)))), a));
                case "component-envelope":
                    return new(Board(K(Occ.Box(40, 26, 1.6)), b), Marked(K(Occ.Box(40, 26, 1.6)), K(Mv(Occ.Box(10, 8, 4), 5, 3, 1.6)), a));

                // ---- assembly and motion ----
                case "joint-rigid":
                    return new(Plain(K(Join([Occ.Box(24, 16, 6), Mv(Occ.Box(10, 10, 8), 0, 0, 16)])), b), Plain(K(Join([Occ.Box(24, 16, 6), Mv(Occ.Box(10, 10, 8), 0, 0, 6)])), a));
                case "joint-revolute":
                    return new(Plain(K(Join([Occ.Box(10, 10, 10), Mv(Occ.Box(30, 4, 3), 26, 0, 18)])), b), Plain(K(Hinge(-30)), a));
                case "drive-joints":
                    return new(Plain(K(Hinge(0)), b), Plain(K(Hinge(-50)), a));
                case "joint-slider":
                    return new(Plain(K(Join([Occ.Box(40, 6, 4), Mv(Occ.Box(8, 8, 5), -14, 0, 14)])), b), Plain(K(Join([Occ.Box(40, 6, 4), Mv(Occ.Box(8, 8, 5), 10, 0, 4)])), a));
                case "joint-cyl":
                {
                    Shape Collar() => Occ.Cut(K(Occ.Cylinder(6, 6)), K(Mv(Occ.Cylinder(2.7, 8), 0, 0, -1)));
                    return new(Plain(K(Join([Occ.Cylinder(2.5, 30), Mv(Collar(), 16, 0, 0)])), b), Plain(K(Join([Occ.Cylinder(2.5, 30), Mv(Collar(), 0, 0, 14)])), a));
                }
                case "joint-ball":
                {
                    Shape Socket() => Occ.Cut(K(Occ.Box(14, 14, 8)), K(Mv(Occ.Sphere(5), 0, 0, 8)));
                    Shape Stem(double z) => Occ.Fuse(K(Mv(Occ.Sphere(4.5), 0, 0, z)), K(Mv(Occ.Cylinder(1.5, 14), 0, 0, z)));
                    return new(Plain(K(Join([Socket(), Stem(22)])), b), Plain(K(Join([Socket(), Occ.Rotate(K(Stem(8)), new(0, 0, 8), Vec3.UnitX, 25)])), a));
                }
                case "joint-planar":
                    return new(Plain(K(Join([Occ.Box(36, 26, 3), Mv(Occ.Cylinder(5, 4), -8, 0, 14)])), b), Plain(K(Join([Occ.Box(36, 26, 3), Mv(Occ.Cylinder(5, 4), 6, 4, 3)])), a));
                case "joint-gear":
                    return new(Plain(K(Join([Gear(8, 10), Mv(Gear(5, 7), 24, 0, 0)])), b), Plain(K(Join([Gear(8, 10), Mv(Gear(5, 7), 14.6, 0, 0)])), a));
                case "explode":
                    return new(Plain(K(Join([Occ.Box(30, 20, 4), Mv(Occ.Box(18, 12, 8), 0, 0, 4), Mv(Occ.Cylinder(4, 4), 0, 0, 12)])), b),
                        Plain(K(Join([Occ.Box(30, 20, 4), Mv(Occ.Box(18, 12, 8), 0, 0, 10), Mv(Occ.Cylinder(4, 4), 0, 0, 24)])), a));

                // ---- sketch ----
                case "slot":
                    return new(Plain(K(Dots(new Vec3(-10, 0, 0), new(10, 0, 0))), b),
                        Lines(K(Join([Lines2((-10, 4, 10, 4), (-10, -4, 10, -4)), Occ.Arc3(new(10, 4, 0), new(14, 0, 0), new(10, -4, 0)), Occ.Arc3(new(-10, -4, 0), new(-14, 0, 0), new(-10, 4, 0))])), a));
                case "text":
                    return new(Spot(b), Lines(K(Join([
                        Occ.Polyline([new(-14, 6, 0), new(-4, 6, 0), new(-4, 4, 0), new(-8, 4, 0), new(-8, -6, 0), new(-10, -6, 0), new(-10, 4, 0), new(-14, 4, 0)], true),
                        Occ.Polyline([new(0, 6, 0), new(2, 6, 0), new(2, -4, 0), new(8, -4, 0), new(8, -6, 0), new(0, -6, 0)], true)])), a));
                case "dimension":
                    return new(Lines(K(Lines2((-14, 0, 14, 0))), b), Lines(K(Lines2((-14, 0, 14, 0), (-14, 1, -14, 7), (14, 1, 14, 7), (-14, 5.5, 14, 5.5),
                        (-14, 5.5, -12, 6.5), (-14, 5.5, -12, 4.5), (14, 5.5, 12, 6.5), (14, 5.5, 12, 4.5))), a));
                case "construction":
                {
                    var corners = new Vec3[] { new(-12, -8, 0), new(12, -8, 0), new(12, 8, 0), new(-12, 8, 0) };
                    var dashes = new List<Shape>();
                    for (int i = 0; i < 4; i++)
                    {
                        var p = corners[i]; var q = corners[(i + 1) % 4]; double len = (q - p).Length;
                        for (double s = 0; s < len; s += 3.5) dashes.Add(Occ.Line(Vec3.Lerp(p, q, s / len), Vec3.Lerp(p, q, Math.Min(s + 2, len) / len)));
                    }
                    return new(Lines(K(Occ.Polyline(corners, true)), b), Lines(K(Join(dashes)), a));
                }
                case "trim":
                    return new(Lines(K(Lines2((-20, 0, 20, 0), (-8, -10, -8, 10), (8, -10, 8, 10))), b), Lines(K(Lines2((-20, 0, -8, 0), (8, 0, 20, 0), (-8, -10, -8, 10), (8, -10, 8, 10))), a));
                case "grease-pencil":
                {
                    var frame = K(Lines2((-20, -10, 20, -10), (20, -10, 20, 10), (20, 10, -20, 10), (-20, 10, -20, -10)));
                    var mark = K(Occ.Spline([new(-14, -2, 0), new(-6, 5, 0), new(2, -4, 0), new(10, 4, 0), new(15, -1, 0)]));
                    return new(Lines(frame, b), Lines(K(Occ.Compound([frame, mark])), a));
                }
                case "grease-eraser":
                {
                    var one = K(Occ.Spline([new(-14, -2, 0), new(-6, 5, 0), new(2, -4, 0)]));
                    var two = K(Occ.Spline([new(6, 6, 0), new(12, -2, 0), new(16, 5, 0)]));
                    return new(Lines(K(Occ.Compound([one, two])), b), Lines(two, a));
                }
                case "extend":
                    return new(Lines(K(Lines2((-20, 0, 0, 0), (12, -10, 12, 10))), b), Lines(K(Lines2((-20, 0, 12, 0), (12, -10, 12, 10))), a));
                case "project":
                    return new(Plain(K(Mv(Occ.Box(24, 16, 10), 0, 0, 6)), b),
                        Marked(K(Mv(Occ.Box(24, 16, 10), 0, 0, 6)), K(Occ.Polyline([new(-12, -8, 0), new(12, -8, 0), new(12, 8, 0), new(-12, 8, 0)], true)), a));
                case "sketch-edit":
                {
                    var from = new Vec3[] { new(-12, -8, 0), new(12, -8, 0), new(12, 8, 0), new(-12, 8, 0) };
                    var to = new Vec3[] { new(-12, -8, 0), new(12, -8, 0), new(16, 13, 0), new(-12, 8, 0) };
                    return new(Lines(K(Join([Occ.Polyline(from, true), Dots(from)])), b), Lines(K(Join([Occ.Polyline(to, true), Dots(to)])), a));
                }
                case "sketch-pattern":
                    return new(Lines(K(Occ.Circle(new(-15, 0, 0), Vec3.UnitZ, 3)), b),
                        Lines(K(Join(new[] { -15.0, -5, 5, 15 }.Select(x => Occ.Circle(new(x, 0, 0), Vec3.UnitZ, 3)))), a));

                // ---- sketch constraints ----
                case "c-horizontal": return Sketch2(topic, [Lines2((-12, -5, 12, 4))], [Lines2((-12, -2, 12, -2))]);
                case "c-vertical": return Sketch2(topic, [Lines2((-4, -12, 5, 12))], [Lines2((0, -12, 0, 12))]);
                case "c-coincident":
                    return Sketch2(topic, [Lines2((-14, -8, 0, -8), (2, -6, 2, 10)), Dots(new Vec3(0, -8, 0), new(2, -6, 0))], [Lines2((-14, -8, 2, -8), (2, -8, 2, 10)), Dots(new Vec3(2, -8, 0))]);
                case "c-parallel": return Sketch2(topic, [Lines2((-14, -6, 14, -6), (-14, 2, 14, 10))], [Lines2((-14, -6, 14, -6), (-14, 4, 14, 4))]);
                case "c-perp": return Sketch2(topic, [Lines2((-12, -8, 12, -8), (-6, -8, 4, 10))], [Lines2((-12, -8, 12, -8), (-6, -8, -6, 10))]);
                case "c-tangent":
                    return Sketch2(topic, [Occ.Circle(Vec3.Zero, Vec3.UnitZ, 6), Lines2((-14, 10, 14, 13))], [Occ.Circle(Vec3.Zero, Vec3.UnitZ, 6), Lines2((-14, 6, 14, 6))]);
                case "c-equal": return Sketch2(topic, [Lines2((-14, 4, -4, 4), (-14, -4, 14, -4))], [Lines2((-14, 4, 14, 4), (-14, -4, 14, -4))]);
                case "c-midpoint": return Sketch2(topic, [Lines2((-14, 0, 14, 0)), Dots(new Vec3(5, 0, 0))], [Lines2((-14, 0, 14, 0)), Dots(new Vec3(0, 0, 0))]);
                case "c-concentric":
                    return Sketch2(topic, [Occ.Circle(Vec3.Zero, Vec3.UnitZ, 10), Occ.Circle(new(4, 3, 0), Vec3.UnitZ, 4)], [Occ.Circle(Vec3.Zero, Vec3.UnitZ, 10), Occ.Circle(Vec3.Zero, Vec3.UnitZ, 4)]);
                case "c-collinear": return Sketch2(topic, [Lines2((-16, 0, -2, 0), (2, 4, 16, 4))], [Lines2((-16, 0, -2, 0), (2, 0, 16, 0))]);
                case "c-symmetric":
                    return Sketch2(topic, [Lines2((0, -12, 0, 12)), Dots(new Vec3(-8, 4, 0), new(5, -3, 0))], [Lines2((0, -12, 0, 12)), Dots(new Vec3(-8, 4, 0), new(8, 4, 0))]);

                // ---- display ----
                case "shaded" or "shaded-edges" or "wireframe":
                {
                    var mesh = K(Occ.Cut(K(Occ.Box(30, 20, 16)), K(Mv(Occ.Cylinder(5, 20), 0, 0, -2)))).Tessellate();
                    return id switch
                    {
                        "shaded" => new(Src(mesh, b), Src(NoEdges(mesh), a)),
                        "shaded-edges" => new(Src(NoEdges(mesh), b), Src(mesh, a)),
                        _ => new(Src(mesh, b), Src(EdgesOnly(mesh), a)),
                    };
                }
                case "sel-paint":
                {
                    var after = GridBox(30, 20, 14, 6, 4, 3);
                    return new(Src(GridBox(30, 20, 14, 6, 4, 3), b), Src(after, a, null, [Patch(after, c => c.Z > 13.99 && Math.Abs(c.X - c.Y * 1.3) < 4)], Blue));
                }
                case "visibility-off" or "visibility-on":
                {
                    var two = K(Join([Mv(Occ.Box(14, 14, 12), -16, 0, 0), Occ.Cylinder(7, 16)]));
                    var three = Marked(two, K(Mv(Occ.Sphere(8), 18, 0, 8)), id == "visibility-off" ? b : a);
                    return id == "visibility-off" ? new(three, Plain(two, a)) : new(Plain(two, b), three);
                }
                default: return null;
            }
        }
        finally { foreach (var shape in own) shape.Dispose(); }
    }

    /// <summary>Pictures of flat sketch geometry: drawn on XY, then turned to face the preview camera so horizontal reads as horizontal.</summary>
    static readonly HashSet<string> Flat = ["line", "rect", "arc", "ellipse", "polygon", "spline", "slot", "text", "dimension", "construction", "trim", "extend",
        "sketch-edit", "sketch-pattern", "offset", "offset-curve", "blend-curve", "join-curves", "explode-curve", "trim-curve", "extend-curve", "rebuild-curve", "origin-point", "curve-comb"];

    static ToolHelpExample Facing(ToolHelpExample example) => new(Facing(example.Before), Facing(example.After));
    static StudioPreviewSource Facing(StudioPreviewSource source)
    {
        var camera = new Core.Camera(); Vec3 right = camera.Right, up = camera.Up, eye = camera.EyeDirection;
        return source with { Mesh = Deform(source.Mesh, p => right * p.X + up * p.Y + eye * p.Z) };
    }

    /// <summary>A single placement point, on a small ground square so the picture keeps its scale.</summary>
    static StudioPreviewSource Spot(string label)
    {
        using var ground = Occ.Box(30, 30, 0.2); using var dot = Dots(new Vec3(0, 0, 0.2));
        return Marked(ground, dot, label);
    }

    // ---- shape helpers ----
    static StudioPreviewSource Plain(Shape shape, string label) => Src(shape.Tessellate(), label);
    static StudioPreviewSource Lines(Shape shape, string label) { var mesh = Fit(shape.Tessellate()); return Src(mesh, label, AllEdges(mesh), null, Blue); }

    /// <summary>A body with an extra shape (points, a curve, a plane) drawn in the highlight color.</summary>
    static StudioPreviewSource Marked(Shape body, Shape extra, string label)
    {
        var first = body.Tessellate();
        using var all = Occ.Compound([body, extra]);
        var mesh = Fit(all.Tessellate());
        return Src(mesh, label, mesh.SegEdge.Where(e => e >= first.EdgeCount).Distinct().ToArray(),
            Enumerable.Range(first.FaceCount, Math.Max(0, mesh.FaceCount - first.FaceCount)).ToArray(), Blue);
    }

    static StudioPreviewSource Board(Shape board, string label) { var mesh = board.Tessellate(); return Src(mesh, label, null, AllFaces(mesh), Green); }
    static StudioPreviewSource Cage(MeshData mesh, string label, int[]? faces = null) => Src(mesh, label, null, faces, Blue);

    static ToolHelpExample Sketch2(ToolHelpTopic topic, Shape[] before, Shape[] after)
    {
        using var b = Join(before); using var a = Join(after);
        return new(Lines(b, topic.BeforeLabel), Lines(a, topic.AfterLabel));
    }

    static Shape Dots(params Vec3[] points) => Join(points.Select(p => Mv(Occ.Sphere(0.9), p.X, p.Y, p.Z)));
    static Shape Lines2(params (double x0, double y0, double x1, double y1)[] lines) => Join(lines.Select(l => Occ.Line(new(l.x0, l.y0, 0), new(l.x1, l.y1, 0))));
    static Vec3 Polar(double r, double degrees) { double t = degrees * Math.PI / 180; return new(r * Math.Sin(t), r * Math.Cos(t), 0); }
    static Shape ArcXY(double r) => Occ.Arc3(Polar(r, -60), Polar(r, 0), Polar(r, 60));
    static Shape ArcOnCircle(double r, double from, double to)
    {
        Vec3 At(double d) => new(r * Math.Cos(d * Math.PI / 180), r * Math.Sin(d * Math.PI / 180), 0);
        return Occ.Arc3(At(from), At((from + to) / 2), At(to));
    }

    /// <summary>The curved sheet used by the surface examples: part of a cylinder bulging 6 mm over a 32 mm span.</summary>
    const double SheetRadius = 24.3333, SheetCenter = -18.3333;
    static Vec3 SheetPoint(double x, double grow) { double r = SheetRadius + grow, t = Math.Asin(x / SheetRadius); return new(r * Math.Sin(t), -12, SheetCenter + r * Math.Cos(t)); }
    static Shape SheetArc(double x0, double x1, double grow) => Occ.Arc3(SheetPoint(x0, grow), SheetPoint((x0 + x1) / 2, grow), SheetPoint(x1, grow));
    static Shape Sheet(double x0, double x1, double grow = 0) { using var arc = SheetArc(x0, x1, grow); return Occ.Extrude(arc, new(0, 24, 0)); }

    /// <summary>The six faces of a box as separate surfaces, each pushed out by the gap.</summary>
    static Shape BoxFaces(double dx, double dy, double dz, double gap)
    {
        double x = dx / 2, y = dy / 2;
        Shape Face(Vec3[] corners, Vec3 push) { using var wire = Occ.Polyline(corners.Select(c => c + push * gap).ToList(), true); return Occ.Profile([wire]); }
        return Join([
            Face([new(-x, -y, dz), new(x, -y, dz), new(x, y, dz), new(-x, y, dz)], Vec3.UnitZ),
            Face([new(-x, -y, 0), new(x, -y, 0), new(x, y, 0), new(-x, y, 0)], -Vec3.UnitZ),
            Face([new(x, -y, 0), new(x, y, 0), new(x, y, dz), new(x, -y, dz)], Vec3.UnitX),
            Face([new(-x, -y, 0), new(-x, y, 0), new(-x, y, dz), new(-x, -y, dz)], -Vec3.UnitX),
            Face([new(-x, y, 0), new(x, y, 0), new(x, y, dz), new(-x, y, dz)], Vec3.UnitY),
            Face([new(-x, -y, 0), new(x, -y, 0), new(x, -y, dz), new(-x, -y, dz)], -Vec3.UnitY)]);
    }

    static Shape Holes(Shape board) { using var drills = Join(new (double x, double y)[] { (-21, -12), (21, -12), (21, 12), (-21, 12) }.Select(p => Mv(Occ.Cylinder(1.6, 4), p.x, p.y, -1))); return Occ.Cut(board, drills); }

    /// <summary>A base block with an arm pinned on a hinge, turned by the given angle.</summary>
    static Shape Hinge(double degrees)
    {
        using var arm = Mv(Occ.Box(30, 4, 3), 15, 0, 10);
        return Join([Occ.Box(10, 10, 10), Occ.Rotate(arm, new(0, 0, 11.5), Vec3.UnitY, degrees), Mv(Occ.Rotate(Occ.Cylinder(1.2, 8), Vec3.Zero, Vec3.UnitX, 90), 0, 4, 11.5)]);
    }

    static Shape Gear(double r, int teeth)
    {
        var parts = new List<Shape> { Occ.Cylinder(r, 4) };
        for (int i = 0; i < teeth; i++) { using var tooth = Mv(Occ.Box(2.4, 2.4, 4), r + 0.8, 0, 0); parts.Add(Occ.Rotate(tooth, Vec3.Zero, Vec3.UnitZ, i * 360.0 / teeth)); }
        return Join(parts);
    }

    static (Vec3, Vec3) EdgeEnds(MeshData mesh, int edge)
    {
        var points = Enumerable.Range(0, mesh.SegmentCount).Where(s => mesh.SegEdge[s] == edge).SelectMany(s => new[] { At(mesh.EdgePoints, s * 2), At(mesh.EdgePoints, s * 2 + 1) }).ToArray();
        return (points[0], points[^1]);
    }

    /// <summary>Edges whose every drawn point passes the test.</summary>
    static int[] EdgesWhere(MeshData mesh, Func<Vec3, bool> test) =>
        Enumerable.Range(0, mesh.SegmentCount).GroupBy(s => mesh.SegEdge[s])
            .Where(g => g.All(s => test(At(mesh.EdgePoints, s * 2)) && test(At(mesh.EdgePoints, s * 2 + 1)))).Select(g => g.Key).ToArray();

    static Vec3 Twist(Vec3 p, double angle) { double c = Math.Cos(angle), s = Math.Sin(angle), z = p.Z - 5; return new(p.X, p.Y * c - z * s, p.Y * s + z * c + 5); }
    static double Noise(Vec3 dir) => 0.6 * Math.Sin(dir.X * 17 + dir.Y * 5) * Math.Cos(dir.Z * 13 - dir.X * 7);

    // ---- mesh helpers ----
    static MeshData Spherize(MeshData mesh, double r)
    {
        var center = (mesh.Min + mesh.Max) / 2;
        return Renormal(Deform(mesh, p => center + (p - center).Normalized() * r));
    }

    static MeshData Shift(MeshData mesh, Vec3 by) => Deform(mesh, p => p + by);

    /// <summary>Smooth vertex normals from the triangles around each vertex (vertices shared per face keep faces faceted); each keeps the side its old normal faced.</summary>
    static MeshData Renormal(MeshData mesh)
    {
        var sum = new Vec3[mesh.VertexCount];
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            int i = mesh.Indices[t * 3], j = mesh.Indices[t * 3 + 1], k = mesh.Indices[t * 3 + 2];
            var n = Vec3.Cross(At(mesh.Positions, j) - At(mesh.Positions, i), At(mesh.Positions, k) - At(mesh.Positions, i));
            sum[i] += n; sum[j] += n; sum[k] += n;
        }
        for (int v = 0; v < sum.Length; v++)
        {
            var n = sum[v].Normalized(); var old = At(mesh.Normals, v);
            if (Vec3.Dot(n, old) < 0) n = -n;
            mesh.Normals[v * 3] = (float)n.X; mesh.Normals[v * 3 + 1] = (float)n.Y; mesh.Normals[v * 3 + 2] = (float)n.Z;
        }
        return mesh;
    }

    /// <summary>One mesh from several; face and edge ids of later parts follow the earlier ones.</summary>
    static MeshData Merge(params MeshData[] parts)
    {
        var mesh = new MeshData(parts.Sum(p => p.VertexCount), parts.Sum(p => p.TriangleCount), parts.Sum(p => p.SegmentCount))
        { FaceCount = parts.Sum(p => p.FaceCount), EdgeCount = parts.Sum(p => p.EdgeCount) };
        int v = 0, t = 0, s = 0, f = 0, e = 0;
        var lo = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue); var hi = -lo;
        foreach (var p in parts)
        {
            Array.Copy(p.Positions, 0, mesh.Positions, v * 3, p.Positions.Length);
            Array.Copy(p.Normals, 0, mesh.Normals, v * 3, p.Normals.Length);
            for (int i = 0; i < p.Indices.Length; i++) mesh.Indices[t * 3 + i] = p.Indices[i] + v;
            for (int i = 0; i < p.TriangleCount; i++) mesh.TriFace[t + i] = p.TriFace[i] + f;
            Array.Copy(p.EdgePoints, 0, mesh.EdgePoints, s * 6, p.EdgePoints.Length);
            for (int i = 0; i < p.SegmentCount; i++) mesh.SegEdge[s + i] = p.SegEdge[i] + e;
            lo = new(Math.Min(lo.X, p.Min.X), Math.Min(lo.Y, p.Min.Y), Math.Min(lo.Z, p.Min.Z)); hi = new(Math.Max(hi.X, p.Max.X), Math.Max(hi.Y, p.Max.Y), Math.Max(hi.Z, p.Max.Z));
            v += p.VertexCount; t += p.TriangleCount; s += p.SegmentCount; f += p.FaceCount; e += p.EdgeCount;
        }
        mesh.Min = lo; mesh.Max = hi;
        return mesh;
    }

    /// <summary>The mesh without the triangles whose center passes the test (an opening in a cage).</summary>
    static MeshData DropWhere(MeshData mesh, Func<Vec3, bool> drop)
    {
        var keep = Enumerable.Range(0, mesh.TriangleCount).Where(t => !drop((At(mesh.Positions, mesh.Indices[t * 3]) + At(mesh.Positions, mesh.Indices[t * 3 + 1]) + At(mesh.Positions, mesh.Indices[t * 3 + 2])) / 3)).ToArray();
        var lines = Enumerable.Range(0, mesh.SegmentCount).Where(s => !(drop(At(mesh.EdgePoints, s * 2)) && drop(At(mesh.EdgePoints, s * 2 + 1)))).ToArray();
        var result = new MeshData(mesh.VertexCount, keep.Length, lines.Length) { FaceCount = mesh.FaceCount, EdgeCount = mesh.EdgeCount, Min = mesh.Min, Max = mesh.Max };
        Array.Copy(mesh.Positions, result.Positions, mesh.Positions.Length); Array.Copy(mesh.Normals, result.Normals, mesh.Normals.Length);
        for (int i = 0; i < keep.Length; i++) { for (int k = 0; k < 3; k++) result.Indices[i * 3 + k] = mesh.Indices[keep[i] * 3 + k]; result.TriFace[i] = mesh.TriFace[keep[i]]; }
        for (int i = 0; i < lines.Length; i++) { Array.Copy(mesh.EdgePoints, lines[i] * 6, result.EdgePoints, i * 6, 6); result.SegEdge[i] = mesh.SegEdge[lines[i]]; }
        return result;
    }

    static MeshData NoEdges(MeshData mesh)
    {
        var result = new MeshData(mesh.VertexCount, mesh.TriangleCount, 0) { FaceCount = mesh.FaceCount, EdgeCount = 0, Min = mesh.Min, Max = mesh.Max };
        Array.Copy(mesh.Positions, result.Positions, mesh.Positions.Length); Array.Copy(mesh.Normals, result.Normals, mesh.Normals.Length);
        Array.Copy(mesh.Indices, result.Indices, mesh.Indices.Length); Array.Copy(mesh.TriFace, result.TriFace, mesh.TriFace.Length);
        return result;
    }

    /// <summary>A triangle-mesh ball with its triangle edges drawn, as a scan or print mesh looks; the bump varies the radius by direction.</summary>
    static MeshData Ball(double r, int nu, int nv, Func<Vec3, double>? bump = null)
        => GridMesh(nu, nv, (u, v) =>
        {
            double theta = Math.PI * v, phi = 2 * Math.PI * u;
            var dir = new Vec3(Math.Sin(theta) * Math.Cos(phi), Math.Sin(theta) * Math.Sin(phi), Math.Cos(theta));
            return dir * (r + (bump?.Invoke(dir) ?? 0));
        }, p => p);

    /// <summary>A wavy triangulated sheet lifted by the given height.</summary>
    static MeshData SheetMesh(Func<double, double, double> z, double lift)
        => GridMesh(14, 10, (u, v) => { double x = -18 + 36 * u, y = -12 + 24 * v; return new(x, y, z(x, y) + lift); }, _ => Vec3.UnitZ);

    static MeshData GridMesh(int nu, int nv, Func<double, double, Vec3> at, Func<Vec3, Vec3> outward)
    {
        int stride = nu + 1, quads = nu * nv;
        var mesh = new MeshData(stride * (nv + 1), quads * 2, quads * 3 + nu + nv) { FaceCount = 1, EdgeCount = 1 };
        var lo = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue); var hi = -lo;
        for (int j = 0; j <= nv; j++)
            for (int i = 0; i <= nu; i++)
            {
                var p = at((double)i / nu, (double)j / nv); int k = j * stride + i;
                mesh.Positions[k * 3] = (float)p.X; mesh.Positions[k * 3 + 1] = (float)p.Y; mesh.Positions[k * 3 + 2] = (float)p.Z;
                var o = outward(p); mesh.Normals[k * 3] = (float)o.X; mesh.Normals[k * 3 + 1] = (float)o.Y; mesh.Normals[k * 3 + 2] = (float)o.Z;
                lo = new(Math.Min(lo.X, p.X), Math.Min(lo.Y, p.Y), Math.Min(lo.Z, p.Z)); hi = new(Math.Max(hi.X, p.X), Math.Max(hi.Y, p.Y), Math.Max(hi.Z, p.Z));
            }
        int t = 0, s = 0;
        void Line(int i, int j)
        {
            Array.Copy(mesh.Positions, i * 3, mesh.EdgePoints, s * 6, 3); Array.Copy(mesh.Positions, j * 3, mesh.EdgePoints, s * 6 + 3, 3);
            mesh.SegEdge[s++] = 0;
        }
        for (int j = 0; j < nv; j++)
            for (int i = 0; i < nu; i++)
            {
                int a = j * stride + i, b = a + 1, c = a + stride, d = c + 1;
                foreach (var (x, y, z) in new[] { (a, c, d), (a, d, b) })
                { mesh.Indices[t * 3] = x; mesh.Indices[t * 3 + 1] = y; mesh.Indices[t * 3 + 2] = z; mesh.TriFace[t++] = 0; }
                Line(a, b); Line(a, c); Line(a, d);
                if (i == nu - 1) Line(b, d);
                if (j == nv - 1) Line(c, d);
            }
        mesh.Min = lo; mesh.Max = hi;
        return Renormal(mesh);
    }

    /// <summary>Scan points spread over a ball, each drawn as a small cross.</summary>
    static MeshData Cloud(double r, int count)
    {
        var mesh = new MeshData(0, 0, count * 3) { FaceCount = 0, EdgeCount = 1, Min = new(-r, -r, -r), Max = new(r, r, r) };
        for (int i = 0; i < count; i++)
        {
            double z = 1 - 2 * (i + 0.5) / count, phi = i * Math.PI * (3 - Math.Sqrt(5)), ring = Math.Sqrt(1 - z * z);
            var p = new Vec3(ring * Math.Cos(phi), ring * Math.Sin(phi), z) * r;
            int k = 0;
            foreach (var axis in new[] { Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ })
            {
                var from = p - axis * 0.4; var to = p + axis * 0.4; int s = i * 3 + k++;
                mesh.EdgePoints[s * 6] = (float)from.X; mesh.EdgePoints[s * 6 + 1] = (float)from.Y; mesh.EdgePoints[s * 6 + 2] = (float)from.Z;
                mesh.EdgePoints[s * 6 + 3] = (float)to.X; mesh.EdgePoints[s * 6 + 4] = (float)to.Y; mesh.EdgePoints[s * 6 + 5] = (float)to.Z;
            }
        }
        return mesh;
    }
}
