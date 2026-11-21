#:property PublishAot=false
#:project ../Prism.Kernel/Prism.Kernel.csproj
using Prism.Kernel;
using System.Text.Json;
using static System.Math;

// Run from the checkout with native/bin on PATH: dotnet run --file gallery/TransportStudies.cs -- vermilion-gt|tideline-jet|evergreen-express
var objects = new List<object>();
string red = "D84126", black = "192128", silver = "BEC8CC", glass = "203844", white = "F1F0E7", teal = "159DA8", green = "18513E", gold = "E9AD35";
Vec3 X = Vec3.UnitX, Y = Vec3.UnitY, Z = Vec3.UnitZ, O = Vec3.Zero;
Shape move(Shape s, Vec3 d) => Occ.Translate(s, d);
Shape box(Vec3 c, double l, double w, double h) => move(Occ.Box(l, w, h), c - Z * h / 2);
Shape ell(Vec3 c, double l, double w, double h) => move(Occ.Scale(Occ.Sphere(1), O, new Vec3(l, w, h)), c);
Shape rot(Shape s, Vec3 c, Vec3 axis, double angle) => Occ.Rotate(s, c, axis, angle);
Shape rod(Vec3 a, Vec3 b, double r)
{
    var d = (b - a).Normalized();
    var s = Occ.Cylinder(r, (b - a).Length);
    var axis = Vec3.Cross(Z, d);
    if (axis.Length > 1e-8)
        s = rot(s, O, axis, Acos(Clamp(d.Z, -1, 1)) * 180 / PI);
    else if (d.Z < 0)
        s = rot(s, O, X, 180);
    return move(s, a);
}

Shape ring(Vec3 c, double r, double tube, Vec3 axis) => move(rot(Occ.Torus(r, tube), O, axis == Y ? X : Y, 90), c);
Shape polygon(Vec3[] p, Vec3 extrusion) => Occ.Extrude(Occ.Profile([Occ.Polyline(p, true)]), extrusion);
Shape loft(List<Vec3[]> rings, bool smooth = true, bool straight = false) => Occ.Loft(rings.Select(r => smooth ? Occ.Spline(r, true) : Occ.Polyline(r, true)).ToList(), true, straight);
void add(string name, Shape s, string color, double metal = .15, double gloss = .66)
{
    if (!s.IsValid || !s.HasSolids || s.Props().Volume <= 0)
        throw new Exception($"Invalid geometry: {name}");
    objects.Add(new { Name = name, Kind = "Solid", Color = Convert.ToUInt32("FF" + color, 16), Visible = true, Brep = s.ToBrep(), Metal = metal, Gloss = gloss, Id = Guid.NewGuid() });
}

Shape cut(Shape s, Shape tool) => Occ.Cut(s, tool);
Shape clip(Shape s, Shape tool) => Occ.Common(s, tool);
Shape group(List<Shape> parts) => Occ.Compound(parts);
Shape sideMask((double x, double z)[] outline, double span) => polygon(outline.Select(p => new Vec3(p.x, -span, p.z)).ToArray(), 2 * span * Y);
void paired(string name, Shape right, string color, double metal = .15, double gloss = .65)
{
    add(name + " starboard", right, color, metal, gloss);
    add(name + " port", Occ.Mirror(right, O, Y), color, metal, gloss);
}

void car()
{
    var loops = new List<Vec3[]>();
    foreach (var (x, w, hood, fender, bottom) in new (double, double, double, double, double)[]
    {
        (-2180, 550, 385, 402, 245),
        (-2080, 790, 438, 480, 178),
        (-1780, 916, 515, 648, 146),
        (-1280, 959, 610, 791, 135),
        (-825, 921, 715, 745, 133),
        (-410, 893, 730, 710, 133),
        (330, 900, 726, 729, 134),
        (820, 951, 737, 785, 137),
        (1320, 996, 762, 857, 145),
        (1790, 934, 762, 787, 168),
        (2080, 810, 705, 708, 222),
        (2120, 766, 679, 684, 245)
    })
    {
        (double y, double z)[] half = [(0, hood), (.43 * w, hood + 8), (.76 * w, fender - 8), (.92 * w, fender - 15), (w, fender - 85), (.97 * w, bottom + Min(100, (fender - bottom) * .25)), (.87 * w, bottom + 12), (0, bottom)];
        loops.Add(half.Concat(half.Reverse().Skip(1).SkipLast(1).Select(p => (-p.y, p.z))).Select(p => new Vec3(x, p.Item1, p.Item2)).ToArray());
    }

    var shell = loft(loops, smooth: false);
    foreach (int side in new[]
    {
        -1,
        1
    })
    {
        foreach (int x in new[]
        {
            -1280,
            1320
        })
            shell = cut(shell, rod(new(x, side * 677, 330), new(x, side * 1260, 330), 355));
        Vec3[] opening = [(new(512, side * 850, 300)), new(633, side * 850, 419), new(815, side * 850, 660), new(994, side * 850, 698), new(914, side * 850, 373), new(782, side * 850, 284)];
        shell = cut(shell, polygon(opening, side * Y * 435));
        add("Intake throat " + side, polygon(opening, side * Y * 5), black, .06, .43);
        for (int i = 0; i < 4; i++)
        {
            double z = 384 + i * 58;
            add($"Side intake vane {side} {i}", rod(new(722 + i * 23, side * 863, z), new(897 + i * 7, side * 863, z + 4), 5), "455058", .4);
        }

        Vec3[] scoop = [new(-2250, side * 412, 239), new(-2250, side * 689, 238), new(-2250, side * 741, 338), new(-2250, side * 478, 349)];
        shell = cut(shell, polygon(scoop, X * 267));
        add("Brake duct liner " + side, polygon(scoop.Select(q => q + X * 262).ToArray(), X * 4), black, .08, .46);
    }

    // A transverse arch cutter can leave valid geometry while punching through the hood.
    using var checkBody = Occ.FromBrep(shell.ToBrep());
    using var hoodProbe = box(new(-1280, 0, 475), 10, 10, 10);
    using var hoodMaterial = clip(checkBody, hoodProbe);
    if (Abs(hoodMaterial.Props().Volume - 1000) > .01)
        throw new InvalidOperationException("The centre of the hood must survive the wheel-arch cuts.");
    foreach (int axle in new[] { -1280, 1320 })
        foreach (int side in new[] { -1, 1 })
        {
            using var wellProbe = box(new(axle, side * 830, 330), 10, 10, 10);
            using var obstruction = clip(checkBody, wellProbe);
            if (obstruction.HasSolids)
                throw new InvalidOperationException("Coachwork obstructs a wheel well.");
        }
    add("Sculpted fenders and body", shell, red, .36, .86);
    // Keep the roof, screen and pillars on a shared analytic canopy surface.
    (double x, double w, double h)[] stations = [(-920, 390, 705), (-780, 630, 775), (-460, 700, 1000), (-100, 693, 1150), (290, 681, 1195), (570, 668, 1150), (900, 610, 988), (1290, 445, 765)];
    (double w, double h) cabin(double x)
    {
        int i = 0;
        while (i < stations.Length - 2 && x > stations[i + 1].x)
            i++;
        var a = stations[i];
        var b = stations[i + 1];
        var p = stations[Max(0, i - 1)];
        var q = stations[Min(stations.Length - 1, i + 2)];
        double dx = b.x - a.x, t = (x - a.x) / dx, t2 = t * t, t3 = t2 * t;
        double interp(double v, double u, double v0, double u1) => (2 * t3 - 3 * t2 + 1) * v + (t3 - 2 * t2 + t) * dx * (u - v0) / (b.x - p.x) + (-2 * t3 + 3 * t2) * u + (t3 - t2) * dx * (u1 - v) / (q.x - a.x);
        return (interp(a.w, b.w, p.w, q.w), interp(a.h, b.h, p.h, q.h));
    }

    Vec3 roofPoint(double x, double angle)
    {
        var(w, h) = cabin(x);
        return new(x, w * Sin(angle), 672 + (h - 672) * Pow(Max(0, Cos(angle)), .63));
    }

    var glassSlices = new List<Vec3[]>();
    for (int i = 0; i <= 32; i++)
    {
        double x = -920 + 2210.0 * i / 32;
        var arc = Enumerable.Range(0, 23).Select(k => roofPoint(x, (k - 11) * PI / 22)).ToList();
        arc.Add(new(x, arc[^1].Y * .92, 607));
        arc.Add(new(x, arc[0].Y * .92, 607));
        glassSlices.Add(arc.ToArray());
    }

    add("Panoramic glazing", loft(glassSlices, false, true), glass, .36, .86);
    var roofRings = new List<Vec3[]>();
    for (int i = 0; i <= 12; i++)
    {
        double x = -190 + 816.0 * i / 12, width = .90 - .16 * Pow(2.0 * i / 12 - 1, 2);
        var row = Enumerable.Range(0, 15).Select(k => roofPoint(x, (k - 7) * width / 7) + Z * 5).ToList();
        roofRings.Add(row.Concat(row.AsEnumerable().Reverse().Select(p => p - Z * 12)).ToArray());
    }

    add("Curved roof panel", loft(roofRings, false, true), red, .32, .86);
    foreach (int side in new[]
    {
        -1,
        1
    })
        foreach (bool rear in new[]
        {
            false,
            true
        })
        {
            var strips = new List<Vec3[]>();
            for (int i = 0; i <= 14; i++)
            {
                double t = i / 14.0, angle = side * (.70 + .85 * t), x = rear ? 540 + 626 * t : -175 - 601 * t, width = rear ? 42 + 63 * t : 27;
                var a = roofPoint(x - width, angle) + new Vec3(0, side * 5, 5);
                var b = roofPoint(x + width, angle) + new Vec3(0, side * 5, 5);
                Vec3 inset = new(0, -side * 11, -12);
                strips.Add([a, b, b + inset, a + inset]);
            }

            add((rear ? "Sail pillar " : "Windscreen pillar ") + side, loft(strips, false, true), red, .36, .83);
        }

    foreach (int side in new[]
    {
        -1,
        1
    })
    {
        foreach (int x in new[]
        {
            -1280,
            1320
        })
        {
            string n = (x < 0 ? "Front " : "Rear ") + (side < 0 ? "left " : "right ");
            Vec3 c = new(x, side * (x < 0 ? 830 : 866), 330);
            add(n + "tyre", ring(c, 267, 63, Y), "202124", .03, .30);
            add(n + "inner tyre", rod(c - Y * 83, c + Y * 83, 293), "242528", .04, .29);
            var hub = c + Y * side * 93;
            add(n + "brake rotor", rod(hub - Y * 6, hub + Y * 6, 196), "64676B", .66, .62);
            add(n + "caliper", ell(hub + new Vec3(135, 0, 12), 38, 15, 82), "AD2819");
            add(n + "forged rim", ring(hub, 221, 13, Y), silver, .88, .78);
            List<Shape> spokes = [];
            for (int i = 0; i < 5; i++)
            {
                var a = i * 2 * PI / 5;
                Vec3 ray(double t) => new(Cos(t), 0, Sin(t));
                foreach (int k in new[]
                {
                    -1,
                    1
                })
                {
                    var verts = new[]
                    {
                        hub + ray(a - .13) * 55,
                        hub + ray(a + .13) * 55,
                        hub + ray(a + k * .19 + .045) * 219,
                        hub + ray(a + k * .19 - .045) * 219
                    };
                    spokes.Add(polygon(verts, side * Y * 13));
                }
            }

            add(n + "split spokes", group(spokes), silver, .82, .78);
            add(n + "centre cap", rod(hub - Y * 18, hub + Y * 18, 54), silver, .8);
        }

        var sill = loft([new Vec3[] { new(-910, side * 866, 211), new(-910, side * 863, 160), new(-910, side * 821, 156) }, new Vec3[] { new(0, side * 902, 179), new(0, side * 907, 143), new(0, side * 845, 133) }, new Vec3[] { new(941, side * 943, 238), new(941, side * 953, 165), new(941, side * 882, 148) }], false);
        add("Lower sill blade " + side, sill, "333C41", .37);
        add("Mirror stalk " + side, rod(new(-415, side * 707, 772), new(-403, side * 913, 864), 15), black);
        add("Mirror housing " + side, ell(new(-410, side * 923, 873), 102, 65, 47), red, .35, .85);
    }

    var face = Occ.Scale(shell, new(-1200, 0, 370), new Vec3(1.009, 1.016, 1.022));
    foreach (int side in new[]
    {
        -1,
        1
    })
    {
        var lightMask = polygon(new[] { new Vec3(-2070, side * 495, 445), new(-2005, side * 755, 445), new(-1570, side * 866, 445), new(-1760, side * 715, 445) }, Z * 420);
        add("Smoked headlamp " + side, clip(face, lightMask), "273641", .48, .9);
        var blade = polygon(new[] { new Vec3(-2040, side * 547, 445), new(-1983, side * 745, 445), new(-1670, side * 835, 445), new(-1970, side * 715, 445) }, Z * 380);
        add("LED signature " + side, clip(Occ.Scale(face, O, new Vec3(1.0005, 1.0005, 1.006)), blade), "EDEEDC", .2, .9);
    }

    add("Nose intake", ell(new(-2151, 0, 307), 65, 536, 70), black);
    add("Front splitter", ell(new(-2001, 0, 187), 192, 809, 18), "2D3334");
    for (int i = 0; i < 5; i++)
        add("Engine cover louvre " + i, ell(new(1470 + i * 71, 0, 763 - i * 1.8), 21, 359 - i * 12, 8), black, .25, .52);
    add("Tail light ribbon", ell(new(2103, 0, 604), 37, 720, 23), "AC1726");
}

void jet()
{
    // The constant-section barrel gives every cabin window the same seating surface.
    Vec3[] ellipseAt(double x, double w, double h, double z) => Enumerable.Range(0, 24).Select(i =>
    {
        double a = 2 * PI * i / 24;
        return new Vec3(x, w * Cos(a), z + h * Sin(a));
    }).ToArray();
    Shape fuselage((double x, double w, double h, double z)[] stations) => loft(stations.Select(s => ellipseAt(s.x, s.w, s.h, s.z)).ToList());
    var fore = fuselage([(-18450, 24, 25, 3300), (-18350, 450, 330, 3320), (-17800, 1050, 760, 3400), (-16800, 1460, 1240, 3590), (-15100, 1780, 1750, 3790), (-13000, 1890, 1930, 3880), (-12000, 1900, 1940, 3880)]);
    var aft = fuselage([(11600, 1900, 1940, 3880), (12600, 1888, 1925, 3885), (14500, 1660, 1690, 3940), (16400, 1100, 1180, 4060), (18400, 520, 566, 4240), (19600, 90, 100, 4360)]);
    var barrel = loft([ellipseAt(-12000, 1900, 1940, 3880), ellipseAt(11600, 1900, 1940, 3880)], straight: true);
    var belly = box(new(0, 0, 1500), 46000, 5600, 3000);
    foreach (var (s, n)in new[]
    {
        (fore, "Flight deck"),
        (barrel, "Passenger cabin"),
        (aft, "Tail cone")
    })
    {
        add(n + " upper skin", cut(s, belly), white, .17, .78);
        add(n + " blue belly", clip(s, belly), "193E65", .24, .8);
    }

    var glazing = Occ.Scale(fore, new(-17300, 0, 3860), new Vec3(1.004, 1.008, 1.009));
    var mask = sideMask([(-17840, 3860), (-15900, 4170), (-15080, 4350), (-15200, 4830), (-17250, 4590)], 3300);
    var deckGlass = clip(glazing, mask);
    foreach (double x in new[]
    {
        -16650,
        -15760
    })
        deckGlass = cut(deckGlass, box(new(x, 0, 4350), 65, 5500, 2000));
    deckGlass = cut(deckGlass, box(new(-17000, 0, 4340), 7000, 95, 1800));
    add("Segmented flight deck windscreen", deckGlass, glass, .55, .91);
    List<Shape> windows = [];
    for (int i = 0; i < 38; i++)
    {
        double x = -11570 + i * 603;
        foreach (int side in new[]
        {
            -1,
            1
        })
            windows.Add(ell(new(x, side * 1842, 4320), 142, 54, 206));
    }

    add("Passenger window glazing", group(windows), "283E50", .45, .84);
    foreach (int side in new[]
    {
        -1,
        1
    })
    {
        foreach (int x in new[]
        {
            -11900,
            11250
        })
        {
            var door = box(new(x, side * 1840, 3970), 740, 123, 1610);
            var inset = box(new(x, side * 1840, 3970), 692, 300, 1558);
            add($"Cabin door seal {x} {side}", clip(Occ.Scale(barrel, new(0, 0, 3880), new Vec3(1, 1.01, 1.01)), cut(door, inset)), "738591");
        }
    }

    Vec3[] foil(double x, double span, double z, double chord, double thick, bool vertical = false)
    {
        (double u, double v)[] p = [(0, 0), (.045, .59), (.18, .98), (.40, .91), (.72, .49), (.98, .055), (1, 0), (.97, -.055), (.64, -.38), (.26, -.56), (.07, -.44)];
        return p.Select(q => vertical ? new Vec3(x + q.u * chord, q.v * thick, z) : new Vec3(x + q.u * chord, span, z + q.v * thick)).ToArray();
    }

    var wing = loft([foil(-3900, 1030, 2860, 6860, 470), foil(-2020, 5900, 3150, 4760, 310), foil(2050, 14700, 3910, 2140, 135), foil(3380, 16400, 4230, 1370, 85)], false);
    paired("Swept wing", wing, "D7E1E4", .37, .72);
    paired("Curved winglet", loft([foil(3380, 16400, 4230, 1370, 85), foil(3540, 16680, 4510, 1180, 71), foil(4130, 17070, 5530, 580, 39)], false), teal);
    paired("Horizontal stabilizer", loft([foil(13250, 590, 4220, 4200, 250), foil(15550, 5850, 4700, 1480, 99)], false, true), white);
    var fin = loft([foil(10700, 0, 4920, 7760, 500, true), foil(13100, 0, 6820, 4880, 360, true), foil(15380, 0, 10180, 2030, 120, true)], false);
    var sash = sideMask([(12000, 6200), (17450, 8530), (17450, 8910), (12000, 6580)], 1200);
    add("Turquoise tail fin", cut(fin, sash), teal, .32, .78);
    add("Diagonal fin stripe", clip(fin, sash), white);
    foreach (int side in new[]
    {
        -1,
        1
    })
    {
        string n = side < 0 ? "Port" : "Starboard";
        Vec3 center = new(-2410, side * 5230, 2370);
        (double x, double r)[] profile = [(-1780, 1010), (-1740, 1110), (-1440, 1205), (-750, 1230), (100, 1190), (1050, 1010), (1530, 838), (1525, 755), (810, 770), (-480, 897), (-1520, 920), (-1750, 955)];
        var wall = Occ.Profile([Occ.Spline(profile.Select(p => center + X * p.x + Z * p.r).ToArray(), true)]);
        add(n + " fan cowl", Occ.Revolve(wall, center, X, 360), white, .32, .83);
        add(n + " intake lip", ring(center - X * 1735, 988, 48, X), silver, .9, .82);
        add(n + " bypass duct", rod(center - X * 1380, center - X * 1340, 934), "26323B");
        List<Shape> blades = [];
        for (int i = 0; i < 21; i++)
        {
            double a = i * 2 * PI / 21;
            Vec3 radial(double t) => new(0, Cos(t), Sin(t));
            blades.Add(polygon([center - X * 1425 + radial(a) * 160, center - X * 1425 + radial(a + .36) * 900, center - X * 1425 + radial(a + .54) * 900, center - X * 1425 + radial(a + .18) * 162], X * 28));
        }

        add(n + " fan blades", group(blades), "738491", .86, .72);
        add(n + " spinner", ell(center - X * 1450, 270, 192, 192), "45515A", .69);
        add(n + " engine pylon", ell(center + new Vec3(730, 0, 1010), 1610, 153, 475), "C1CDD2");
    }

    foreach (var (x, y, r)in new[]
    {
        (-13520, 0, 395),
        (1940, -3100, 575),
        (1940, 3100, 575)
    })
    {
        add($"Landing strut {y}", rod(new(x, y, r), new(x + 190, y, 2760), 110), silver, .77);
        foreach (int side in new[]
        {
            -1,
            1
        })
        {
            var c = new Vec3(x, y + side * (r * .57), r);
            add($"Tyre {y} {side}", rod(c - Y * r * .30, c + Y * r * .30, r), black, .05, .34);
            add($"Wheel hub {y} {side}", rod(c - Y * r * .32, c + Y * r * .32, r * .55), silver, .72);
        }
    }
}

void train()
{
    const double h = 3420, w = 1460, baseZ = 751;
    Vec3[] section(double x, double width, double bottom, double top)
    {
        (double a, double b)[] half = [(0, 1), (.63, .998), (.87, .965), (.98, .89), (1, .77), (1, .26), (.98, .14), (.89, .04), (.72, .004), (0, 0)];
        return half.Concat(half.Reverse().Skip(1).SkipLast(1).Select(p => (-p.a, p.b))).Select(p => new Vec3(x, width * p.Item1, bottom + (top - bottom) * p.Item2)).ToArray();
    }

    var mainWire = Occ.Spline(section(0, w, baseZ, baseZ + h), true);
    var nose = loft([section(-23800, 140, 1130, 1490), section(-23500, 681, 940, 1880), section(-22800, 1080, 830, 2660), section(-21850, 1300, 792, 3440), section(-20500, 1442, 760, 4080), section(-19400, w, baseZ, baseZ + h)]);
    var wedge = sideMask([(-25000, 0), (-21700, 0), (-19610, 4800), (-25000, 4800)], 2900);
    var noseSkin = Occ.Scale(nose, new(-20000, 0, 2200), new Vec3(1.005, 1.015, 1.018));
    void endPair(string label, Shape s, string color, double metal = .2, double gloss = .76)
    {
        add("Leading " + label, s, color, metal, gloss);
        add("Trailing " + label, Occ.Mirror(s, O, X), color, metal, gloss);
    }

    endPair("cab in yellow", clip(nose, wedge), gold);
    endPair("cab shoulders", cut(nose, wedge), green);
    var windshieldMask = sideMask([(-23700, 1910), (-22000, 2230), (-20210, 2700), (-20500, 3850), (-23300, 3560)], 3200);
    endPair("wraparound windscreen", clip(noseSkin, windshieldMask), glass, .45, .88);
    foreach (int side in new[]
    {
        -1,
        1
    })
    {
        var lampMask = box(new(-22890, side * 1040, 1720), 1130, 570, 150);
        endPair("lamp " + side, clip(noseSkin, lampMask), "F8F2D4", .22, .82);
    }

    foreach (var (a, b, index)in new[]
    {
        (-19400, -7890, 1),
        (-7490, 7490, 2),
        (7890, 19400, 3)
    })
    {
        var coach = move(Occ.Extrude(Occ.Profile([mainWire]), X * (b - a)), X * a);
        add("Coach " + index, coach, green, .27, .8);
        var skin = Occ.Scale(coach, new((a + b) / 2.0, 0, 2450), new Vec3(1.0, 1.009, 1.004));
        var stripe = clip(skin, box(new((a + b) / 2.0, 0, 1940), b - a - 8, 3500, 82));
        add("Ivory waist stripe " + index, stripe, "E8D9AA", .16, .76);
        List<Shape> panes = [], seals = [];
        foreach (int side in new[]
        {
            -1,
            1
        })
        {
            int count = index == 2 ? 9 : 7;
            var step = (b - a - 2000.0) / count;
            for (int i = 0; i < count; i++)
            {
                double x = a + 1000 + (i + .5) * step;
                var paneMask = box(new(x, side * 1450, 2810), step - 95, 370, 1040);
                panes.Add(clip(skin, paneMask));
            }

            foreach (int x in new[]
            {
                a + 510,
                b - 510
            })
            {
                var frame = box(new(x, side * 1450, 2340), 765, 340, 2280);
                var inset = box(new(x, side * 1450, 2340), 707, 600, 2215);
                seals.Add(clip(skin, cut(frame, inset)));
                panes.Add(clip(skin, box(new(x, side * 1450, 2810), 609, 370, 1030)));
            }
        }

        add("Glazing coach " + index, group(panes), "243C46", .51, .9);
        add("Door seals coach " + index, group(seals), black);
        add("Roof climate pack " + index, box(new((a + b) / 2.0, 0, 4190), 3280, 1530, 185), "566363");
        add("Underfloor equipment " + index, box(new((a + b) / 2.0, 0, 767), b - a - 2800, 1860, 560), "333738");
    }

    foreach (int x in new[]
    {
        -7690,
        7690
    })
    {
        var core = move(Occ.Extrude(Occ.Profile([Occ.Spline(section(0, 1290, 840, 3970), true)]), X * 470), X * (x - 235));
        add("Articulation " + x, core, black);
        for (int j = -2; j <= 2; j++)
            add($"Bellows {x} {j}", move(Occ.Extrude(Occ.Profile([Occ.Spline(section(0, 1390, 810, 4050), true)]), X * 38), X * (x + 77 * j - 19)), "3F4547");
    }

    foreach (int x in new[]
    {
        -19100,
        -7690,
        7690,
        19100
    })
    {
        List<Shape> tyres = [], hubs = [];
        foreach (int xx in new[]
        {
            x - 870,
            x + 870
        })
            foreach (int side in new[]
            {
                -1,
                1
            })
            {
                Vec3 c = new(xx, side * 798, 448);
                tyres.Add(rod(c - Y * 87, c + Y * 87, 425));
                hubs.Add(rod(c - Y * 104, c + Y * 104, 252));
            }

        add("Bogie wheels " + x, group(tyres), "383E42", .7);
        add("Axle boxes " + x, group(hubs), "8C9697", .65);
        add("Bogie frame " + x, box(new(x, 0, 726), 2890, 1830, 248), black);
    }

    double px = -8650;
    add("Pantograph pedestal", box(new(px, 0, 4280), 2300, 1190, 195), "4A5152");
    foreach (int s in new[]
    {
        -1,
        1
    })
    {
        add("Lower pantograph arm " + s, rod(new(px - 850, s * 455, 4360), new(px + 430, s * 310, 5120), 55), black);
        add("Upper pantograph arm " + s, rod(new(px + 430, s * 310, 5120), new(px - 310, s * 510, 5760), 41), black);
    }

    add("Collector head", rod(new(px - 310, -845, 5760), new(px - 310, 845, 5760), 52), "535A5A", .65);
    // The wheel treads and railheads meet at Z=23. Sleepers sit under both rails.
    var sleepers = new List<Shape>();
    for (int x = -24300; x <= 24300; x += 600)
        sleepers.Add(box(new(x, 0, -139), 234, 2390, 160));
    add("Concrete sleepers", group(sleepers), "8D8A80", .02, .33);
    foreach (int s in new[]
    {
        -1,
        1
    })
        add("Rail " + s, box(new(0, s * 798, -18), 49800, 120, 82), "737C81", .68);
}

var id = args.Single();
switch (id)
{
    case "vermilion-gt":
        car();
        break;
    case "tideline-jet":
        jet();
        break;
    case "evergreen-express":
        train();
        break;
    default:
        throw new ArgumentException(id);
}

var document = new
{
    Version = 5,
    Objects = objects,
    Tab = "Solid"
};
var path = Path.Combine("gallery", "models", id + ".prism");
var staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
try
{
    File.WriteAllText(staging, JsonSerializer.Serialize(document));
    File.Move(staging, path, overwrite: true);
}
finally
{
    File.Delete(staging);
}

Console.WriteLine($"{id}: {objects.Count} validated objects");
