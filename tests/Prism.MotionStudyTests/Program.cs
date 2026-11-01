using Avalonia;
using Avalonia.Headless;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Assembly;
using Prism.Kernel;
using Prism.Simulation;

// The Motion tab on a real assembly: parts from the CAD kernel, joints and gears made as in the Assemble workspace, studies run through the commands.
AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
int checks = 0;
void Check(bool ok, string text) { if (!ok) throw new Exception("FAIL " + text); Console.WriteLine("PASS " + text); checks++; }
bool Near(double a, double b, double rel) => Math.Abs(a - b) <= rel * Math.Max(Math.Abs(b), 1e-12);

SceneObject Part(CadSession s, string name, Shape shape, bool ground, Xform? at = null, string? material = "6061")
{
    var o = s.AddObject(shape, name, ObjKind.Solid);
    s.Update(o.Id, v => v.WithAsm(new ComponentInfo(CadSession.NewKey(), name, null, (at ?? Xform.Identity).ToArray(), ground), v.Shape), false);
    if (material != null) s.Update(o.Id, v => v.WithMaterial(Materials.Find(material)!), false);
    return s.Find(o.Id)!;
}
SceneObject Joint(CadSession s, string key, string name, JointType type, SceneObject parent, SceneObject child, Vec3 at, Vec3 axis, Vec3 x, bool limits = false, double lo = 0, double hi = 0)
{
    var f = JointInfo.Pack(Xform.FromFrame(at, x, axis));
    var j = s.AddObject(Occ.Line(at, at + new Vec3(0, 0, 0.01)), name, ObjKind.Joint);
    s.Update(j.Id, v => v.WithAsm(new JointInfo(key, name, type, parent.Component!.Key, child.Component!.Key, f, f, false, 0, 0, 0, limits, lo, hi, 0, 0)), false);
    return s.Find(j.Id)!;
}
NumberParam Num(Command c, string label, string? section = null) => c.Params.OfType<NumberParam>().First(p => (p.Label == label || p.Label.StartsWith(label + " ")) && (section == null || p.Section.StartsWith(section)));
ChoiceParam Pick(Command c, string labelStart) => c.Params.OfType<ChoiceParam>().First(p => p.Label.StartsWith(labelStart));
void Run(Command c) => c.Params.OfType<ActionParam>().First(p => p.Label == "Run study").Run!();
string Text(Command c) => string.Join("\n", c.Results.Select(r => r.Label + ": " + r.Value));

// ---------------------------------------------------------------- a pendulum made in the assembly, swung in CAE
{
    var s = new CadSession();
    var baseBlock = Part(s, "Base", Occ.Box(40, 40, 10), true);
    var rod = Part(s, "Rod", Occ.Translate(Occ.Box(10, 10, 200), new Vec3(0, 0, -200)), false);
    Joint(s, "hinge", "Hinge", JointType.Revolute, baseBlock, rod, Vec3.Zero, new Vec3(0, 1, 0), new Vec3(1, 0, 0));
    Check(s.Components.Count() == 2 && s.JointObjects.Count() == 1, "the mechanism is made with the assembly's own parts and joints");

    var body = s.BodyOf(s.Find(rod.Id)!, out bool has);
    double mass = 2.7e-6 * 10 * 10 * 200;
    Check(has && Near(body.Mass, mass, 1e-3) && Near(body.Com.Z, -100, 1e-3), $"a part's mass comes from its geometry and material: {body.Mass * 1000:0.###} g");

    s.StartCommand(() => new MotionStudyCommand(true));
    var cmd = (MotionStudyCommand)s.ActiveCommand!;
    Check(cmd.Title == "Motion Dynamics" && Text(cmd).Contains("press Run study"), "Motion Dynamics opens on the assembly and asks for a run");
    Pick(cmd, "Hinge").Index = 3;                                // free
    Num(cmd, "Starting position", "Hinge").Value = 3;
    Num(cmd, "Duration").Value = 2; Num(cmd, "Output interval").Value = 0.005;
    Run(cmd);
    var r = cmd.Result!;
    double L = 0.2, w = 0.01, d = L / 2, ip = body.Mass * (L * L + w * w) / 12 + body.Mass * d * d;
    double theory = 2 * Math.PI * Math.Sqrt(ip / (body.Mass * MotionStudyCommand.G * d)) * (1 + Math.Pow(3 * Math.PI / 180, 2) / 16);
    var cross = new List<double>();
    for (int i = 1; i < r.Samples; i++) if (r.Pos[0][i - 1] * r.Pos[0][i] < 0) cross.Add(r.Time[i - 1] + 0.005 * Math.Abs(r.Pos[0][i - 1]) / (Math.Abs(r.Pos[0][i - 1]) + Math.Abs(r.Pos[0][i])));
    double period = 2 * (cross[1] - cross[0]);
    Check(Near(period, theory, 0.005), $"a rod swung under gravity in CAE has the physical-pendulum period: {period:0.####} s vs {theory:0.####} s");
    Check(s.Motion == r && Text(cmd).Contains("Solved") && Text(cmd).Contains("Rod"), "the study shows its summary and keeps the result on the document for the paths");
    Check(!Text(cmd).Contains("press Run study"), "the panel replaces the prompt with results");
    var overlay = new Overlay(); s.BuildOverlay(overlay);
    Check(overlay.Lines.Count > 20, "the path the rod's centre takes is drawn in the viewport");

    // scrub: the model is posed at the shown time and nothing is committed
    var time = Pick(cmd, "Hinge"); var scrub = Num(cmd, "Show the motion at"); scrub.Value = 25;
    var moved = cmd.TryBuild()!;
    Check(moved.NoOp && moved.Replace.Any(x => x.Old.Id == rod.Id), "scrubbing poses the rod for that moment without changing the document");
    double angleBefore = ((JointInfo)s.JointObjects.Single().joint).Value;
    s.CommitCommand();
    Check(Math.Abs(((JointInfo)s.JointObjects.Single().joint).Value - angleBefore) < 1e-12 && s.Motion == null, "Done leaves the joints where they were and clears the paths");

    // changed settings are flagged as stale
    s.StartCommand(() => new MotionStudyCommand(true)); cmd = (MotionStudyCommand)s.ActiveCommand!;
    Pick(cmd, "Hinge").Index = 3; Run(cmd);
    Num(cmd, "Duration").Value = 3;
    Check(Text(cmd).Contains("Settings changed"), "changing a setting after a run warns that the results are old");
    cmd.Params.OfType<TextParam>().First().Value = Path.Combine(Path.GetTempPath(), "motion-test.csv");
    cmd.Params.OfType<ActionParam>().First(p => p.Label == "Save results").Run!();
    var csv = File.ReadAllLines(Path.Combine(Path.GetTempPath(), "motion-test.csv"));
    Check(csv.Length > 100 && csv[0].Contains("Hinge position_°") && csv[0].Contains("kinetic_J") && csv[1].Split(',').Length == csv[0].Split(',').Length, "results save to a CSV with a column for every joint and part");
    s.CancelCommand();

    // a part without a material cannot be given mass
    var s2 = new CadSession();
    var b2 = Part(s2, "Base", Occ.Box(40, 40, 10), true); var r2 = Part(s2, "Rod", Occ.Translate(Occ.Box(10, 10, 200), new Vec3(0, 0, -200)), false, null, null);
    Joint(s2, "hinge", "Hinge", JointType.Revolute, b2, r2, Vec3.Zero, new Vec3(0, 1, 0), new Vec3(1, 0, 0));
    s2.StartCommand(() => new MotionStudyCommand(true)); var c2 = (MotionStudyCommand)s2.ActiveCommand!;
    Run(c2);
    Check(Text(c2).Contains("Assign a material to Rod"), "dynamics says which part needs a material");
    s2.CancelCommand();
    s2.StartCommand(() => new MotionStudyCommand(false)); c2 = (MotionStudyCommand)s2.ActiveCommand!;
    Num(c2, "Speed", "Hinge").Value = 90; Pick(c2, "Hinge").Index = 1; Run(c2);
    Check(c2.Result != null && Text(c2).Contains("Solved"), "kinematics needs no material");
    s2.CancelCommand();
}

// ---------------------------------------------------------------- gears: coupled in the assembly, driven in CAE
{
    var s = new CadSession();
    var housing = Part(s, "Housing", Occ.Translate(Occ.Box(120, 60, 6), new Vec3(15, 0, -6)), true);
    var g1 = Part(s, "Pinion", Occ.Cylinder(10, 8), false);
    var g2 = Part(s, "Gear", Occ.Translate(Occ.Cylinder(20, 8), new Vec3(30, 0, 0)), false);
    var ja = Joint(s, "ja", "Shaft A", JointType.Revolute, housing, g1, Vec3.Zero, new Vec3(0, 0, 1), new Vec3(1, 0, 0));
    var jb = Joint(s, "jb", "Shaft B", JointType.Revolute, housing, g2, new Vec3(30, 0, 0), new Vec3(0, 0, 1), new Vec3(1, 0, 0));

    s.StartCommand(() => new CouplingCommand(false)); var gear = s.ActiveCommand!;
    Check(Pick(gear, "Driving gear").Options.Count == 2 && Pick(gear, "Driven gear").Index == 1, "Gear Pair lists the revolute joints and proposes the second as driven");
    Num(gear, "Driving gear size (teeth or pitch diameter)").Value = 20; Num(gear, "Driven gear size (same unit)").Value = 40;
    Check(Text(gear).Contains("2 : 1") && Text(gear).Contains("opposite") && Text(gear).Contains("module"), "Gear Pair shows the ratio, direction and the shaft spacing");
    s.CommitCommand();
    Check(s.Couplings.Count() == 1 && s.Couplings.Single().Ratio == 0.5 && s.Couplings.Single().Reverse, "the gear pair is stored in the assembly");

    // driving the first gear in Assemble turns the second
    s.StartCommand(() => new DriveJointsCommand()); var drive = s.ActiveCommand!;
    Num(drive, "Shaft A").Value = 90; s.CommitCommand();
    var placedB = s.Find(g2.Id)!.Component!.Placement;
    Check(Near(((JointInfo)s.JointObjects.First(j => j.joint.Key == "jb").joint).Value, -45, 1e-6), "turning the driving gear 90° turns the driven gear −45°, and the document stores that");
    Check(Near(placedB.AngleAboutZ() * 180 / Math.PI, -45, 1e-3), "and the driven gear's part really turns");
    var saved = System.Text.Json.JsonSerializer.Serialize(s.Couplings.Single() as AssemblyData);
    Check(saved.Contains("coupling"), "the coupling is part of the saved document");

    // motion study through the gear pair, with a load
    s.StartCommand(() => new MotionStudyCommand(true)); var cmd = (MotionStudyCommand)s.ActiveCommand!;
    Check(!cmd.Params.OfType<ChoiceParam>().Any(p => p.Label.StartsWith("Shaft B")) && cmd.Params.OfType<NumberParam>().Any(p => p.Section.StartsWith("Shaft B") && p.Label == "Load"), "the driven shaft has no motion of its own, only a load");
    Pick(cmd, "Shaft A").Index = 1; Num(cmd, "Speed", "Shaft A").Value = 360; Num(cmd, "Starting position", "Shaft A").Value = 0;
    Num(cmd, "Load", "Shaft B").Value = -0.02;                         // 20 mN·m resisting the driven gear's turn
    Num(cmd, "Duration").Value = 1; Num(cmd, "Output interval").Value = 0.05;
    Pick(cmd, "Gravity").Index = 0;
    Run(cmd);
    var r = cmd.Result!;
    Check(Near(r.Vel[1][3], -180, 1e-4), "the output shaft turns at half the input speed the other way");
    Check(Math.Abs(Math.Abs(r.Effort[0][5]) - 0.01) < 5e-4, $"a 20 mN·m load on the output needs 10 mN·m at the motor ({Math.Abs(r.Effort[0][5]) * 1000:0.##} mN·m, the wheels' inertia adds none at constant speed)");
    s.CancelCommand();

    // collision sweep: a post in the way of the big gear is found, a gap is not
    var post = Part(s, "Post", Occ.Translate(Occ.Box(6, 6, 8), new Vec3(30, 17, 0)), false);
    Joint(s, "jp", "Post fixed", JointType.Rigid, housing, post, new Vec3(30, 15, 0), new Vec3(0, 0, 1), new Vec3(1, 0, 0));
    s.StartCommand(() => new MotionStudyCommand(false)); cmd = (MotionStudyCommand)s.ActiveCommand!;
    Pick(cmd, "Shaft A").Index = 1; Num(cmd, "Speed", "Shaft A").Value = 90; Num(cmd, "Starting position", "Shaft A").Value = 0;
    Num(cmd, "Duration").Value = 1; Num(cmd, "Output interval").Value = 0.1;
    cmd.Params.OfType<BoolParam>().First(p => p.Label.StartsWith("Look for collisions")).Value = true;
    Run(cmd);
    Check(Text(cmd).Contains("Gear 1 and Post 1") && Text(cmd).Contains("overlap"), "the collision check finds a post standing in the gear's way");
    s.CancelCommand();
}

// ---------------------------------------------------------------- help
{
    foreach (var dyn in new[] { false, true })
    {
        var topic = Prism.Help.ToolHelp.ForCommand(new MotionStudyCommand(dyn));
        Check(topic != null && topic.Title == (dyn ? "Motion Dynamics" : "Kinematics"), $"{(dyn ? "Motion Dynamics" : "Kinematics")} has a help topic");
        var example = Prism.Help.ToolHelp.Example(topic!);
        Check(example.Before != null && example.After != null, "its before/after example can be drawn");
    }
}

Console.WriteLine($"SUCCESS: {checks} checks");
