#:property PublishAot=false
#:project ../Prism.Kernel/Prism.Kernel.csproj
using Prism.Kernel;

var folder = Path.GetFullPath(args.Single());
Directory.CreateDirectory(folder);

using var sole = RoundedFoot(134);
using var bulkhead = ProfileXZ([(-58,15), (58,15), (58,145), (-58,145)], -7, 35, 7);
using var flange = AxialCylinder(51, -15, 8, 0, 91);
var carrier = Occ.Fuse(sole, bulkhead);
Join(flange);

foreach (int side in new[] { -1, 1 })
{
    var x = side * 42 - 4;
    using var triangle = Occ.Polyline([new(x,-44,15), new(x,-6,15), new(x,-6,48)], true);
    using var face = Occ.Profile([triangle]);
    using var rib = Occ.Extrude(face, new(8,0,0));
    Join(rib);
}
using (var bore = AxialCylinder(29, -19, 51, 0, 91))
    Cut(bore);
for (int index = 0; index < 8; index++)
{
    var (x,z) = BoltPosition(index, 8, 42, .5);
    using var pilot = AxialCylinder(3.2, -17, 48, x, z);
    using var counterbore = AxialCylinder(5.1, -17, 7, x, z);
    Cut(pilot);
    Cut(counterbore);
}
foreach (int side in new[] { -1, 1 })
{
    using var hole = At(Occ.Cylinder(5.4, 20), side * 56, -33, -2);
    Cut(hole);
}
using (carrier)
    Occ.ExportStep([carrier], Path.Combine(folder, "spindle-carrier.step"));

// Stock added to the imported solid to form the wide-foot casting and recover
// the old hole pattern. The next tool set machines the new openings.
using var wideFoot = RoundedFoot(202);
using var slope = ProfileXZ([(-95, 15), (95, 15), (52, 144), (-52, 144)], -7, 35, 4);
using var newFlange = AxialCylinder(56, -16, 45, 0, 91);
Occ.ExportStep([wideFoot, slope, newFlange], Path.Combine(folder, "spindle-stock.step"));

using var seat = AxialCylinder(41, -18, 9, 0, 91);
Occ.ExportStep([seat], Path.Combine(folder, "spindle-seat.step"));

var tools = new List<Shape>
{
    AxialCylinder(35, -19, 51, 0, 91),
};
for (int index = 0; index < 6; index++)
{
    var (x,z) = BoltPosition(index, 6, 48.5, 0);
    tools.Add(AxialCylinder(3.0, -18, 49, x, z));
    tools.Add(AlongY(Occ.Cone(5.9, 3, 3.9), -17, x, z));
}
foreach (int side in new[] { -1, 1 })
{
    (double x, double z)[] window = [(side*49, 23), (side*83, 23), (side*67, 77)];
    tools.Add(ProfileXZ(window, -48, 79, 4));
    tools.Add(Slot(side * 78));
}
try
{
    Occ.ExportStep(tools, Path.Combine(folder, "spindle-tools.step"));
}
finally
{
    foreach (var tool in tools)
        tool.Dispose();
}

void Replace(Shape next)
{
    if (!next.IsValid || next.Count(SubKind.Solid) != 1)
        throw new InvalidOperationException("Expected one sound spindle carrier.");
    carrier.Dispose();
    carrier = next;
}
void Join(Shape tool) => Replace(Occ.Fuse(carrier, tool));
void Cut(Shape tool) => Replace(Occ.Cut(carrier, tool));

static Shape At(Shape shape, double x, double y, double z)
{
    using (shape)
        return Occ.Translate(shape, new(x,y,z));
}

static Shape AlongY(Shape shape, double y, double x, double z)
{
    using (shape)
        return At(Occ.Rotate(shape, Vec3.Zero, Vec3.UnitX, -90), x, y, z);
}

static Shape AxialCylinder(double radius, double startY, double depth, double x, double z) =>
    AlongY(Occ.Cylinder(radius, depth), startY, x, z);

static (double x, double z) BoltPosition(int i, int count, double radius, double phase)
{
    var angle = (i + phase) * Math.Tau / count;
    return (radius * Math.Sin(angle), 91 + radius * Math.Cos(angle));
}

static Shape ProfileXZ((double x, double z)[] outline, double y, double depth, double radius)
{
    using var wire = Occ.Polyline(outline.Select(p => new Vec3(p.x,y,p.z)).ToArray(), true);
    using var rounded = Occ.WireFillet(wire, radius);
    using var face = Occ.Profile([rounded]);
    return Occ.Extrude(face, new(0,depth,0));
}

static Shape RoundedFoot(double width)
{
    var w = width / 2;
    using var wire = Occ.Polyline([new(-w,-47,0),new(w,-47,0),new(w,47,0),new(-w,47,0)], true);
    using var rounded = Occ.WireFillet(wire, 6);
    using var face = Occ.Profile([rounded]);
    return Occ.Extrude(face, new(0,0,16));
}

static Shape Slot(double x)
{
    const double y = -33, radius = 5.5, halfSpan = 10;
    using var a = At(Occ.Cylinder(radius,22),x-halfSpan,y,-3);
    using var b = At(Occ.Cylinder(radius,22),x+halfSpan,y,-3);
    using var mid = At(Occ.Box(halfSpan*2,2*radius,22),x,y,-3);
    using var first = Occ.Fuse(mid,a);
    return Occ.Fuse(first,b);
}
