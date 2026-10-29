using System.Runtime.InteropServices;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;

NativeSearch.Initialize();
int count = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS " + message); count++;
}

var session = new CadSession();
session.SetSelection(new SelItem(Guid.Empty, SelKind.Origin, 0), false);
var probe = new InputProbe(); session.StartCommand(probe);
Check(probe.Profiles.Items.Count == 1, "preselection populates the required profile input");
Check(probe.Rails.Items.Count == 0, "preselection does not also populate optional rails");
int builds = probe.BuildCount;
probe.ConfigureInputs(() => { probe.Width.Value = 20; probe.Height.Value = 30; });
Check(probe.BuildCount == builds, "batched input changes do not build intermediate previews");
probe.OnChanged();
Check(probe.BuildCount == builds + 1, "configured inputs build one final preview");
try { probe.ConfigureInputs(() => throw new ArgumentException("fixture")); } catch (ArgumentException) { }
probe.Width.Value = 21;
Check(probe.BuildCount == builds + 2, "failed configuration does not leave previews suspended");
session.CancelCommand();

// A flat B-spline surface must have zero height, regardless of display tessellation.
var poles = new List<Vec3>();
for (int r = 0; r < 8; r++) for (int c = 0; c < 6; c++)
{
    double t = r / 7.0, x = (2 * c / 5.0 - 1) * 36 * (.65 + .35 * Math.Sin(Math.PI * t));
    poles.Add(new(x, -65 + 130 * t, 4.8));
}
using var surface = Occ.NurbsSurface(poles.ToArray(), 8, 6, 3, 3);
var original = surface.Props();
surface.Tessellate();
var displayed = surface.Props();
Check(original.Size.Z < 1e-5, "flat NURBS surface initially has zero geometric height");
Check(displayed.Size.Z < 1e-5, "rendering does not inflate a flat NURBS surface's height");
Check((displayed.Size - original.Size).Length < 1e-5, "geometric dimensions are independent of display mesh state");
Check(displayed.Max.X <= poles.Max(p => p.X) + 1e-5 && displayed.Min.X >= poles.Min(p => p.X) - 1e-5,
    "surface bounds remain within the control-net convex hull");
using var thickened = Occ.Thicken(surface, -2.8);
Check(thickened.IsValid && thickened.HasSolids, "a flat NURBS surface thickens into a valid solid");
thickened.Tessellate();
Check(Math.Abs(thickened.Props().Size.Z - 2.8) < 1e-5, "thickened NURBS plate reports its actual 2.8mm thickness");
// Reproduce the live mouse operation: OCCT can finish a fillet with invalid topology.
var mousePoles = new List<Vec3>();
for (int r = 0; r < 8; r++) for (int c = 0; c < 6; c++)
{
    double t = r / 7.0, u = 2 * c / 5.0 - 1;
    mousePoles.Add(new(u * 36 * (.65 + .35 * Math.Sin(Math.PI * t)), -65 + 130 * t,
        7 + 29 * Math.Pow(Math.Sin(Math.PI * t), .75) * (1 - u * u)));
}
using var mouseSkin = Occ.NurbsSurface(mousePoles.ToArray(), 8, 6, 3, 3);
using var mouseSolid = Occ.Thicken(mouseSkin, -2);
using var wheelBox = Occ.Box(9, 25, 70);
using var wheelCut = Occ.Translate(wheelBox, new(0, -25, -10));
using var slotted = Occ.Cut(mouseSolid, wheelCut);
using var seamBox = Occ.Box(1, 30, 70);
using var seamCut = Occ.Translate(seamBox, new(0, -50, -10));
using var mouse = Occ.Cut(slotted, seamCut);
Check(mouse.IsValid, "mouse housing before the problematic fillet is valid");
bool rejected = false;
try { using var rounded = Occ.Fillet(mouse, [0], .5); Check(rounded.IsValid, "accepted mouse fillet must be valid"); }
catch (KernelException) { rejected = true; }
Check(rejected, "invalid mouse fillet is rejected instead of replacing the housing");
Check(mouse.IsValid, "rejected fillet preserves the original valid housing");
Console.WriteLine($"SUCCESS: {count} checks");

sealed class InputProbe : Command
{
    public SelectionSlot Profiles { get; }
    public SelectionSlot Rails { get; }
    public NumberParam Width { get; }
    public NumberParam Height { get; }
    public int BuildCount { get; private set; }
    public override string Title => "Input configuration fixture";
    public InputProbe()
    {
        Profiles = Slot(new SelectionSlot { Label = "Profiles", Filter = SelFilter.Plane, Multi = true });
        Rails = Slot(new SelectionSlot { Label = "Rails", Filter = SelFilter.Plane, Multi = true, Optional = true });
        Width = Num("Width", 10); Height = Num("Height", 10);
    }
    protected override CommandOutput? Build() { BuildCount++; return null; }
}
static class NativeSearch
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path);
    public static void Initialize()
    {
        string folder = Environment.GetEnvironmentVariable("PRISM_NATIVE_DIR") ?? Path.GetFullPath("native/bin");
        if (OperatingSystem.IsWindows()) SetDllDirectory(folder);
    }
}
