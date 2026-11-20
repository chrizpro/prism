#:property PublishAot=false
#:project ../Prism.Kernel/Prism.Kernel.csproj
using Prism.Kernel;

// A cast pump-mount bracket, written to STEP so it opens with no history: the starting point of the Direct Modeling showcase.
if (args.Length != 1)
    throw new ArgumentException("Supply the output .step path.");

// Base plate 220 x 130 x 18 with rounded corners and a chamfered top rim.
var bracket = ChamferedBlock(220, 130, 18, 12, 2, -10);

// Upright wall at the back, 160 wide, 22 thick, rising to 150.
using (var wall = Box(160, 22, 133, -80, 25, 17)) Join(wall);

// Pump flange: a round boss on the front of the wall, with the shaft bore and a circle of six bolt holes.
using (var boss = Front(Occ.Cylinder(48, 14), 0, 26, 92)) Join(boss);
Bore(30, 0, 92);
for (int i = 0; i < 6; i++)
{
    double a = (30 + 60 * i) * Math.PI / 180;
    Bore(3.3, 39 * Math.Cos(a), 92 + 39 * Math.Sin(a));
}

// Two triangular gussets tie the wall to the base.
foreach (int side in new[] { -1, 1 })
{
    double x = side * 69 - 5;
    using var outline = Occ.Polyline([new(x, 26, 17), new(x, -50, 17), new(x, 26, 110)], true);
    using var face = Occ.Profile([outline]);
    using var gusset = Occ.Extrude(face, new(10, 0, 0));
    Join(gusset);
}

// Front feet: slotted for adjustment. Back feet: counterbored.
foreach (int side in new[] { -1, 1 })
{
    using var a = Cylinder(6, 30, side * 95, -62, -5);
    using var b = Cylinder(6, 30, side * 95, -48, -5);
    using var mid = Box(12, 14, 30, side * 95 - 6, -62, -5);
    using var ab = Occ.Fuse(a, b);
    using var slot = Occ.Fuse(ab, mid);
    Remove(slot);

    using var hole = Cylinder(5, 30, side * 95, 40, -5);
    Remove(hole);
    using var cbore = Cylinder(9, 6, side * 95, 40, 13);
    Remove(cbore);
}

// Locating dowels and a cable pocket in the front of the base.
foreach (int side in new[] { -1, 1 })
{
    using var dowel = Cylinder(3, 30, side * 40, -15, -5);
    Remove(dowel);
}
using (var pocket = RoundedBox(60, 26, 8, 5, -30, -68, 10)) Remove(pocket);

using (bracket)
{
    var props = bracket.Props();
    Console.WriteLine($"STEP bracket: {bracket.Count(SubKind.Face)} faces, {props.Volume:F2} mm3, {props.Size} mm.");
    Occ.ExportStep([bracket], Path.GetFullPath(args[0]));
}

void Change(Shape replacement)
{
    if (!replacement.IsValid || replacement.Count(SubKind.Solid) != 1)
    {
        replacement.Dispose();
        throw new InvalidOperationException("A casting operation did not yield one valid solid.");
    }
    bracket.Dispose();
    bracket = replacement;
}

void Join(Shape add) => Change(Occ.Fuse(bracket, add));
void Remove(Shape tool) => Change(Occ.Cut(bracket, tool));
void Bore(double radius, double x, double z)
{
    using var tool = Front(Occ.Cylinder(radius, 60), x, 50, z);
    Remove(tool);
}

static Shape At(Shape shape, double x, double y, double z)
{
    using (shape)
        return Occ.Translate(shape, new(x, y, z));
}

// a cylinder along -Y whose base sits at y
static Shape Front(Shape shape, double x, double y, double z)
{
    using (shape)
        return At(Occ.Rotate(shape, Vec3.Zero, Vec3.UnitX, 90), x, y, z);
}

static Shape Cylinder(double r, double length, double x, double y, double z) => At(Occ.Cylinder(r, length), x, y, z);

// the native box is centred in X and Y; x, y here are its low corner
static Shape Box(double xSize, double ySize, double zSize, double x, double y, double z) => At(Occ.Box(xSize, ySize, zSize), x + xSize / 2, y + ySize / 2, z);

static Shape RoundedBox(double w, double d, double h, double r, double x, double y, double z)
{
    using var block = Occ.Box(w, d, h);
    var vertical = new List<int>();
    for (int i = 0; i < block.Count(SubKind.Edge); ++i)
    {
        using var edge = block.Sub(SubKind.Edge, i);
        if (edge.Props().Size.Z > h - .001) vertical.Add(i);
    }
    return At(Occ.Fillet(block, vertical.ToArray(), r), x + w / 2, y + d / 2, z);
}

// centred on x = 0 and y = y0
static Shape ChamferedBlock(double w, double d, double h, double r, double c, double y0)
{
    using var rounded = RoundedBox(w, d, h, r, -w / 2, -d / 2, 0);   // centred on the origin, then placed
    var top = new List<int>();
    for (int i = 0; i < rounded.Count(SubKind.Edge); ++i)
    {
        using var edge = rounded.Sub(SubKind.Edge, i);
        var p = edge.Props();
        if (p.Size.Z < .001 && Math.Abs(p.Center.Z - h) < .001) top.Add(i);
    }
    return At(Occ.Chamfer(rounded, top.ToArray(), c), 0, y0, 0);
}
