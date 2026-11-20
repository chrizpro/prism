#:property PublishAot=false
#:project ../Prism.Kernel/Prism.Kernel.csproj
using Prism.Kernel;

if (args.Length != 1)
    throw new ArgumentException("Supply the output .step path.");

using var basePlate = RoundedBlock(244, 168, 18, 0, 8);
using var caseBody = RoundedBlock(184, 114, 48, 17, 12);
using var mainFlange = Cylinder(54, 21, -22, 0, 62);
using var auxiliaryFlange = Cylinder(27, 27, 64, 0, 63);
using var blendedShoulder = At(Occ.Cone(63, 54, 10), -22, 0, 54);

var housing = Occ.Fuse(basePlate, caseBody);
Join(blendedShoulder);
Join(mainFlange);
Join(auxiliaryFlange);

// Webs link the lower sole to the side of the casting.
foreach (int side in new[] { -1, 1 })
    foreach (int x in new[] { -66, 37 })
    {
        using var profile = Occ.Polyline([
            new(x - 4, side * 53, 17), new(x - 4, side * 78, 17),
            new(x - 4, side * 57, 49), new(x - 4, side * 53, 49)], true);
        using var face = Occ.Profile([profile]);
        using var web = Occ.Extrude(face, new(8, 0, 0));
        Join(web);
    }

// Side service port, including its tapered transition into the case.
using var transition = Side(Occ.Cone(22, 16, 12), 48, -51, 40);
using var spigot = Side(Occ.Cylinder(16, 12), 48, -62, 40);
Join(transition);
Join(spigot);

Bore(26, 88, -22, 0, -2);
Bore(33, 11, -22, 0, 75);
Bore(30, 8, -22, 0, -2);
using (var lead = At(Occ.Cone(33, 36, 3), -22, 0, 81))
    Remove(lead);
Bore(12, 41, 64, 0, 51);
Bore(17, 7, 64, 0, 85);

for (int i = 0; i < 8; i++)
{
    double angle = (i + .5) * Math.PI / 4;
    double x = -22 + 43 * Math.Cos(angle), y = 43 * Math.Sin(angle);
    Bore(3.3, 23, x, y, 62);
    Bore(5.7, 7, x, y, 77);
}

for (int i = 0; i < 4; i++)
{
    double angle = (i + .5) * Math.PI / 2;
    double x = 64 + 21 * Math.Cos(angle), y = 21 * Math.Sin(angle);
    Bore(2.6, 14, x, y, 78);
    using var sink = At(Occ.Cone(2.6, 4.9, 2.3), x, y, 87.8);
    Remove(sink);
}

// Four elongated feet accept adjustable fasteners.
foreach (int x in new[] { -91, 91 })
    foreach (int y in new[] { -69, 69 })
    {
        using var end1 = Cylinder(5.5, 22, x - 10, y, -2);
        using var end2 = Cylinder(5.5, 22, x + 10, y, -2);
        using var bridge = Box(20, 11, 22, x, y, -2);
        using var span = Occ.Fuse(bridge, end1);
        using var slot = Occ.Fuse(span, end2);
        Remove(slot);
    }

// One round dowel hole beside the slotted feet is available for mirroring.
Bore(4, 20, 106, 31, -1);

using (var radial = Side(Occ.Cylinder(8, 36), 48, -41, 40))
    Remove(radial);
using (var counterbore = Side(Occ.Cylinder(11, 4), 48, -72, 40))
    Remove(counterbore);

// Flat machined pad and two blind holes for the service identification plate.
using (var pad = Box(42, 26, 4, 49, 35, 63))
    Join(pad);
Bore(2.25, 8, 34, 35, 61);
Bore(2.25, 8, 64, 35, 61);

// Relief pockets on the underside leave a web between the main case and each foot.
foreach (int side in new[] { -1, 1 })
{
    using var pocket = RoundedBlock(143, 20, 8, -2, 4);
    using var placed = Occ.Translate(pocket, new(0, side * 62, 0));
    Remove(placed);
}

using (housing)
{
    var props = housing.Props();
    Console.WriteLine($"STEP housing: {housing.Count(SubKind.Face)} faces, {props.Volume:F2} mm3, {props.Size} mm.");
    Occ.ExportStep([housing], Path.GetFullPath(args[0]));
}

void Change(Shape replacement)
{
    if (!replacement.IsValid || replacement.Count(SubKind.Solid) != 1)
    {
        replacement.Dispose();
        throw new InvalidOperationException("A casting operation did not yield one valid solid.");
    }
    housing.Dispose();
    housing = replacement;
}

void Join(Shape add) => Change(Occ.Fuse(housing, add));
void Remove(Shape tool) => Change(Occ.Cut(housing, tool));
void Bore(double radius, double length, double x, double y, double z)
{
    using var tool = Cylinder(radius, length, x, y, z);
    Remove(tool);
}

static Shape At(Shape shape, double x, double y, double z)
{
    using (shape)
        return Occ.Translate(shape, new(x, y, z));
}

static Shape Cylinder(double r, double length, double x, double y, double z) =>
    At(Occ.Cylinder(r, length), x, y, z);

static Shape Side(Shape shape, double x, double y, double z)
{
    using (shape)
        return At(Occ.Rotate(shape, Vec3.Zero, Vec3.UnitX, 90), x, y, z);
}

static Shape Box(double xSize, double ySize, double zSize, double x, double y, double z) =>
    At(Occ.Box(xSize, ySize, zSize), x, y, z);

static Shape RoundedBlock(double width, double depth, double height, double z, double radius)
{
    using var block = Occ.Box(width, depth, height);
    var vertical = new List<int>();
    for (int i = 0; i < block.Count(SubKind.Edge); ++i)
    {
        using var edge = block.Sub(SubKind.Edge, i);
        if (edge.Props().Size.Z > height - .001)
            vertical.Add(i);
    }
    return At(Occ.Fillet(block, vertical.ToArray(), radius), 0, 0, z);
}
