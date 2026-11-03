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
    Check(cmd.Title == "Motion Study" && Text(cmd).Contains("press Run study"), "Motion Study opens on the assembly and asks for a run");
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
    cmd.Params.OfType<TextParam>().First(p => p.Label.StartsWith("Save results")).Value = Path.Combine(Path.GetTempPath(), "motion-test.csv");
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

    s.StartCommand(() => new CouplingCommand()); var gear = s.ActiveCommand!;
    Check(Pick(gear, "Leading joint").Options.Count == 2 && Pick(gear, "Following joint").Index == 1, "Couple lists the joints and proposes the second as the follower");
    Num(gear, "Leading size").Value = 20; Num(gear, "Following size").Value = 40;
    Check(Text(gear).Contains("2 : 1") && Text(gear).Contains("opposite") && Text(gear).Contains("module"), "two turning joints couple as gears: ratio, direction and shaft spacing");
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


// ---------------------------------------------------------------- springs, actuators and loads placed in the assembly
SelItem FaceFacing(CadSession s, SceneObject o, Vec3 normal)
{
    int n = o.Shape.Count(SubKind.Face);
    for (int i = 0; i < n; i++) if (s.FaceFrame(new SelItem(o.Id, SelKind.Face, i)) is { } f && Vec3.Dot(f.normal, normal) > 0.99) return new SelItem(o.Id, SelKind.Face, i);
    throw new Exception("no such face");
}
{
    var s = new CadSession();
    var floor = Part(s, "Floor", Occ.Box(300, 60, 10), true);
    var block = Part(s, "Block", Occ.Translate(Occ.Box(20, 20, 20), new Vec3(0, 0, 15)), false);
    Joint(s, "slide", "Slide", JointType.Slider, floor, block, new Vec3(0, 0, 25), new Vec3(1, 0, 0), new Vec3(0, 1, 0));
    var wall = s.AddObject(Occ.Translate(Occ.Box(10, 20, 20), new Vec3(110, 0, 15)), "Wall", ObjKind.Solid);        // a plain body: fixed in space
    var massBlock = s.BodyOf(s.Find(block.Id)!, out _);

    s.StartCommand(() => new MotionSpringCommand()); var spring = s.ActiveCommand!;
    spring.Slots[0].TryAdd(FaceFacing(s, s.Find(block.Id)!, new Vec3(1, 0, 0))); spring.Slots[1].TryAdd(FaceFacing(s, s.Find(wall.Id)!, new Vec3(-1, 0, 0)));
    spring.OnChanged();
    Check(Text(spring).Contains("a fixed point in space") && Text(spring).Contains("95"), "Spring / Actuator joins a part to a fixed body and reads the length between the picked points");
    Num(spring, "Stiffness").Value = 0.1;
    s.CommitCommand();
    var info = s.Springs.Single();
    Check(info.CompB == null && Near(info.RestLength, 95, 1e-6) && info.Stiffness == 0.1, "the spring is stored in the assembly with its free length");
    var ov = new Overlay(); s.BuildOverlay(ov);
    Check(ov.Lines.Count > 8, "springs are drawn between their ends");
    Check(System.Text.Json.JsonSerializer.Deserialize<AssemblyData>(System.Text.Json.JsonSerializer.Serialize<AssemblyData>(info)) is SpringInfo back && back.CompA == info.CompA && back.Stiffness == 0.1, "a spring is saved with the document");

    s.StartCommand(() => new MotionStudyCommand(true)); var cmd = (MotionStudyCommand)s.ActiveCommand!;
    Pick(cmd, "Slide").Index = 3; Num(cmd, "Starting position", "Slide").Value = 10;
    Num(cmd, "Duration").Value = 0.5; Num(cmd, "Output interval").Value = 0.002; Pick(cmd, "Gravity").Index = 0;
    Run(cmd);
    var r = cmd.Result!;
    double quarter = Math.PI / 2 * Math.Sqrt(massBlock.Mass / 100);
    int zero = Array.FindIndex(r.Pos[0], v => v <= 0); double tq = r.Time[zero - 1] + 0.002 * r.Pos[0][zero - 1] / (r.Pos[0][zero - 1] - r.Pos[0][zero]);
    Check(Near(tq, quarter, 0.004), $"a block released on the spring made in the assembly swings at √(k/m): quarter period {tq:0.####} s vs {quarter:0.####} s");
    Check(Text(cmd).Contains("Spring") && Near(r.ForceValue[0].Select(Math.Abs).Max(), 1.0, 0.01), "the study reports the spring's peak force (k × 10 mm = 1 N)");
    s.CancelCommand();

    // a force load
    s.StartCommand(() => new MotionLoadCommand(false)); var load = s.ActiveCommand!;
    load.Slots[0].TryAdd(FaceFacing(s, s.Find(block.Id)!, new Vec3(-1, 0, 0)));
    Pick(load, "Direction").Index = 0; Num(load, "Force").Value = 3; Pick(load, "In time").Index = 1; Num(load, "Starts at").Value = 0.05; Num(load, "Lasts").Value = 0.02;
    s.CommitCommand();
    Check(s.Loads.Single().Magnitude == 3 && s.Loads.Single().Shape == 1, "a force load is stored with its direction and pulse");
    s.SelectItems([SelItem.Of(s.Objects.First(o => o.Asm is SpringInfo))]); s.DeleteSelection();
    Check(!s.Springs.Any(), "deleting the spring object removes it from the studies");
    s.StartCommand(() => new MotionStudyCommand(true)); cmd = (MotionStudyCommand)s.ActiveCommand!;
    Pick(cmd, "Slide").Index = 3; Num(cmd, "Starting position", "Slide").Value = 0; Num(cmd, "Duration").Value = 0.2; Num(cmd, "Output interval").Value = 0.005; Pick(cmd, "Gravity").Index = 0;
    Run(cmd); r = cmd.Result!;
    Check(Near(r.Vel[0][^1] / 1000, 3 * 0.02 / massBlock.Mass, 0.01), $"a 3 N pulse for 20 ms gives the block {r.Vel[0][^1]:0.#} mm/s");
    s.CancelCommand();

    // a smooth move and a motor curve from the study panel
    s.StartCommand(() => new MotionStudyCommand(false)); cmd = (MotionStudyCommand)s.ActiveCommand!;
    Pick(cmd, "Slide").Index = 4;
    Num(cmd, "Move to", "Slide").Value = 80; Num(cmd, "Move starts at", "Slide").Value = 0.5; Num(cmd, "Move takes", "Slide").Value = 2; Num(cmd, "Duration").Value = 3;
    Run(cmd);
    Check(Near(cmd.Result!.Pos[0][^1], 80, 1e-6) && cmd.Result.Vel[0][0] == 0, "a smooth move to a position starts and stops at rest");
    s.CancelCommand();
    s.StartCommand(() => new MotionStudyCommand(true)); cmd = (MotionStudyCommand)s.ActiveCommand!;
    Pick(cmd, "Slide").Index = 3; Num(cmd, "Motor stall force", "Slide").Value = 1; Num(cmd, "Motor no-load speed", "Slide").Value = 100; Num(cmd, "Duration").Value = 1; Pick(cmd, "Gravity").Index = 0;
    Run(cmd); double tauM = massBlock.Mass * 0.1 / 1.0;
    Check(Near(cmd.Result!.Vel[0][^1], 100 * (1 - Math.Exp(-1 / tauM)), 0.01), "a linear motor with a force–speed curve reaches its no-load speed exponentially");
    s.CancelCommand();
}

// ---------------------------------------------------------------- robot arm following a curve
{
    var s = new CadSession();
    var baseP = Part(s, "Base", Occ.Box(60, 60, 10), true);
    var l1 = Part(s, "Link 1", Occ.Translate(Occ.Box(300, 20, 20), new Vec3(150, 0, 15)), false);
    var l2 = Part(s, "Link 2", Occ.Translate(Occ.Box(200, 20, 20), new Vec3(400, 0, 15)), false);
    Joint(s, "j1", "Shoulder", JointType.Revolute, baseP, l1, new Vec3(0, 0, 0), new Vec3(0, 0, 1), new Vec3(1, 0, 0));
    Joint(s, "j2", "Elbow", JointType.Revolute, l1, l2, new Vec3(300, 0, 0), new Vec3(0, 0, 1), new Vec3(1, 0, 0));
    var tipFace = FaceFacing(s, s.Find(l2.Id)!, new Vec3(1, 0, 0));
    var line = s.AddObject(Occ.Line(new Vec3(420, 80, 25), new Vec3(420, -80, 25)), "Path", ObjKind.Curve);

    MotionStudyCommand RobotRun(Vec3 from, Vec3 to, double payload, bool torques = true)
    {
        var pathObj = s.AddObject(Occ.Line(from, to), "Path", ObjKind.Curve);
        s.StartCommand(() => new MotionStudyCommand(MotionKind.Robot)); var c = (MotionStudyCommand)s.ActiveCommand!;
        c.Slots[0].TryAdd(tipFace); c.Slots[1].TryAdd(SelItem.Of(s.Find(pathObj.Id)!));
        Num(c, "Starting position", "Shoulder").Value = 20; Num(c, "Starting position", "Elbow").Value = -40;
        Num(c, "Tool speed").Value = 150; Num(c, "Acceleration along the path").Value = 600; Num(c, "Payload carried at the tool").Value = payload;
        c.Params.OfType<BoolParam>().First(p => p.Label.StartsWith("Include masses and forces")).Value = torques;
        Pick(c, "Gravity").Index = 2;                                                              // −Y: the arm stands in the XY plane
        Run(c); return c;
    }
    var empty = RobotRun(new Vec3(420, 80, 25), new Vec3(420, -80, 25), 0);
    var rr = empty.Result!;
    int ti = Array.FindIndex(rr.Bodies, b => b.Name == "Tool");
    Check(Text(empty).Contains("Reached all"), "Robot Path plans joint motion that reaches every point of the picked curve");
    Check((rr.Path[ti][^1] - new Vec3(420, -80, 25)).Length < 0.01, $"the tool ends on the end of the curve (miss {(rr.Path[ti][^1] - new Vec3(420, -80, 25)).Length:G2} mm)");
    Check(rr.Speed[ti].Max() * 1000 < 156 && rr.Speed[ti].Max() * 1000 > 140, $"at the speed asked for ({rr.Speed[ti].Max() * 1000:0.#} mm/s)");
    double peakEmpty = rr.Effort[0].Select(Math.Abs).Max();
    Check(peakEmpty > 0 && Text(empty).Contains("drive torque"), $"with joint torques on, the shoulder torque is reported ({peakEmpty:0.###} N·m)");
    empty.Params.OfType<NumberParam>().First(p => p.Label.StartsWith("Show the motion")).Value = 50;
    Check(empty.TryBuild()!.Replace.Any(x => x.Old.Id == l2.Id), "the arm can be scrubbed along the path");
    s.CancelCommand();
    var loaded = RobotRun(new Vec3(420, 80, 25), new Vec3(420, -80, 25), 3.0);
    Check(loaded.Result!.Effort[0].Select(Math.Abs).Max() > peakEmpty + 3.0 * MotionStudyCommand.G * 0.25 && Text(loaded).Contains("Payload"), "a 3 kg payload adds at least m·g·(reach) to the shoulder torque");
    s.CancelCommand();
    var out_ = RobotRun(new Vec3(900, 0, 25), new Vec3(900, 50, 25), 0, false);
    Check(Text(out_).Contains("could not be reached"), "a path beyond the arm's reach is reported");
    s.CancelCommand();
    var kin = RobotRun(new Vec3(420, 80, 25), new Vec3(420, -80, 25), 0, false);
    Check(!kin.Result!.Dynamic && kin.Result.Effort[0].All(e => e == 0), "without joint torques the arm needs no materials or loads, only reach");
    s.CancelCommand();
}

// ---------------------------------------------------------------- a piston: closed loop made in the assembly, reactions, and a load for a Static study
{
    var s = new CadSession();
    var floor = Part(s, "Block", Occ.Translate(Occ.Box(400, 100, 10), new Vec3(150, 0, -10)), true);
    var crank = Part(s, "Crank", Occ.Translate(Occ.Box(50, 10, 20), new Vec3(25, 0, 0)), false);
    var rod = Part(s, "Rod", Occ.Translate(Occ.Box(150, 10, 20), new Vec3(125, 0, 0)), false);
    var piston = Part(s, "Piston", Occ.Translate(Occ.Box(30, 30, 20), new Vec3(200, 0, 0)), false);
    Joint(s, "j1", "Main bearing", JointType.Revolute, floor, crank, new Vec3(0, 0, 10), new Vec3(0, 0, 1), new Vec3(1, 0, 0));
    Joint(s, "j2", "Crank pin", JointType.Revolute, crank, rod, new Vec3(50, 0, 10), new Vec3(0, 0, 1), new Vec3(1, 0, 0));
    Joint(s, "j3", "Wrist pin", JointType.Revolute, rod, piston, new Vec3(200, 0, 10), new Vec3(0, 0, 1), new Vec3(1, 0, 0));
    Joint(s, "j4", "Cylinder wall", JointType.Slider, floor, piston, new Vec3(200, 0, 10), new Vec3(1, 0, 0), new Vec3(0, 1, 0));

    s.StartCommand(() => new MotionStudyCommand(true)); var cmd = (MotionStudyCommand)s.ActiveCommand!;
    Pick(cmd, "Main bearing").Index = 1; Num(cmd, "Speed", "Main bearing").Value = 1800;
    Num(cmd, "Duration").Value = 0.2; Num(cmd, "Output interval").Value = 0.005; Pick(cmd, "Gravity").Index = 0;
    Run(cmd);
    var r = cmd.Result!;
    Check(r != null && Text(cmd).Contains("set by the loop") && Text(cmd).Contains("Reaction at"), "a piston made from ordinary joints runs: the loop is closed and the reactions reported");
    int pi = Array.FindIndex(r!.Bodies, b => b.Name == "Piston 1");
    double worst = 0; for (int i = 0; i < r.Samples; i++) { double th = 2 * Math.PI * 5 * r.Time[i]; worst = Math.Max(worst, Math.Abs((r.Path[pi][i].X) - (50 * Math.Cos(th) + Math.Sqrt(150 * 150 - 2500 * Math.Sin(th) * Math.Sin(th))))); }
    Check(worst < 1e-6, $"the real piston follows r·cosθ + √(L² − r²sin²θ) (worst miss {worst:G2} mm)");
    Check(r.Effort[0].Select(Math.Abs).Max() > 0 && Text(cmd).Contains("drive torque"), "the motor torque to turn the crank is reported");
    int wall = Array.IndexOf(r.ReactionJoints, "Cylinder wall"); double peak = r.ReactionForce[wall].Max(f => f.Length);
    Check(peak > 0.1 && r.ReactionForce[wall].All(f => Math.Abs(f.X) < 1e-6 * peak + 1e-9 && Math.Abs(f.Z) < 1e-3 * peak + 1e-6), $"the cylinder wall pushes the piston sideways only, up to {peak:0.###} N");
    cmd.Params.OfType<NumberParam>().First(p => p.Label.StartsWith("Show the motion")).Value = 50;
    var overlay = new Overlay(); s.BuildOverlay(overlay);
    Check(overlay.Labels.Any(l => l.Text.EndsWith(" N")), "arrows at the joints show the forces they hold at the moment shown");
    var posed = cmd.TryBuild()!;
    Check(posed.Replace.Any(x => x.Old.Id == piston.Id) && posed.Replace.Any(x => x.Old.Id == rod.Id), "the whole linkage, including the parts the loop moves, is posed for that moment");
    cmd.Params.OfType<TextParam>().First(p => p.Label.StartsWith("Save results")).Value = Path.Combine(Path.GetTempPath(), "piston.csv"); cmd.Params.OfType<ActionParam>().First(p => p.Label == "Save results").Run!();
    Check(File.ReadAllLines(Path.Combine(Path.GetTempPath(), "piston.csv"))[0].Contains("Cylinder wall reaction fy_N"), "the reactions are in the CSV");
    s.CancelCommand();
    Check(s.LastMotion == r, "the study is remembered after its panel closes");

    // the cylinder wall force becomes a Static study load on the piston
    s.StartCommand(() => new MotionLoadToStaticCommand()); var bridge = s.ActiveCommand!;
    Pick(bridge, "Joint").Index = Array.IndexOf(Pick(bridge, "Joint").Options.ToArray(), "Cylinder wall");
    bridge.Slots[0].TryAdd(FaceFacing(s, s.Find(piston.Id)!, new Vec3(0, 1, 0)));
    bridge.OnChanged();
    Check(Text(bridge).Contains("Cylinder wall") && Text(bridge).Contains("Force"), "Load from Motion shows the force it will apply");
    s.CommitCommand();
    var study = s.Studies.Single();
    var load = study.Loads.Single();
    Check(Math.Abs(load.Value - peak) < 1e-9 * peak + 1e-12 && load.Faces.Count == 1 && Near(load.Direction.Length, 1, 1e-9) && Math.Abs(Math.Abs(load.Direction.Y) - 1) < 1e-6, $"a Static study “{study.Name}” holds the peak side load ({load.Value:0.###} N) on the piston's face");
    int at = Array.IndexOf(r.ReactionForce[wall].Select(f => f.Length).ToArray(), peak);
    Check(Math.Abs(((JointInfo)s.JointObjects.First(j => j.joint.Key == "j1").joint).Value - r.Pos[0][at]) < 1e-6, "and the assembly is moved to the moment of that peak, so the part sits as it did");
}

// ---------------------------------------------------------------- a mechanism modeled and jointed in one file
{
    var s = new CadSession();
    var a = s.AddObject(Occ.Box(40, 40, 10), "Base", ObjKind.Solid);
    var b = s.AddObject(Occ.Translate(Occ.Box(10, 10, 100), new Vec3(0, 0, 60)), "Arm", ObjKind.Solid);
    var c = s.AddObject(Occ.Translate(Occ.Box(10, 10, 10), new Vec3(100, 0, 0)), "Spare", ObjKind.Solid);

    s.StartCommand(() => new JointCommand(JointType.Revolute)); var joint = s.ActiveCommand!;
    joint.Slots[0].TryAdd(FaceFacing(s, s.Find(a.Id)!, new Vec3(0, 0, 1))); joint.Slots[1].TryAdd(FaceFacing(s, s.Find(b.Id)!, new Vec3(0, 0, -1)));
    joint.OnChanged();
    Check(s.Find(a.Id)!.Component != null && s.Find(b.Id)!.Component != null, "picking plain bodies with a joint tool makes them parts");
    s.CancelCommand();
    Check(s.Components.Count() == 0, "cancelling the joint puts the bodies back as they were");

    s.StartCommand(() => new JointCommand(JointType.Revolute)); joint = s.ActiveCommand!;
    joint.Slots[0].TryAdd(FaceFacing(s, s.Find(a.Id)!, new Vec3(0, 0, 1))); joint.Slots[1].TryAdd(FaceFacing(s, s.Find(b.Id)!, new Vec3(0, 0, -1)));
    joint.OnChanged(); s.CommitCommand();
    Check(s.JointObjects.Count() == 1 && s.Find(a.Id)!.Component!.Grounded && !s.Find(b.Id)!.Component!.Grounded, "the first body picked is grounded and the joint is made");

    s.StartCommand(() => new MakePartCommand()); var make = s.ActiveCommand!;
    make.Slots[0].Items.Clear(); make.Slots[0].TryAdd(SelItem.Of(s.Find(c.Id)!)); make.OnChanged(); s.CommitCommand();
    Check(s.Find(c.Id)!.Component is { Grounded: false, SourcePath: null } && s.Components.Count() == 3, "Make Part turns a body into a part where it stands");
    s.Undo();
    Check(s.Find(c.Id)!.Component == null, "undo takes Make Part back");
    Check((s.Find(b.Id)!.Mesh.Max.Z) > 100, "the arm keeps its place");
}

// ---------------------------------------------------------------- one study: masses optional, free joints, tables, tool path
{
    var s = new CadSession();
    var floor = Part(s, "Floor", Occ.Box(300, 60, 10), true, null, null);
    var block = Part(s, "Follower", Occ.Translate(Occ.Box(20, 20, 20), new Vec3(0, 0, 15)), false, null, null);
    Joint(s, "slide", "Slide", JointType.Slider, floor, block, new Vec3(0, 0, 25), new Vec3(1, 0, 0), new Vec3(0, 1, 0));

    s.StartCommand(() => new MotionStudyCommand()); var cmd = (MotionStudyCommand)s.ActiveCommand!;
    var forces = cmd.Params.OfType<BoolParam>().First(p => p.Label.StartsWith("Include masses and forces"));
    Check(!forces.Value && !Pick(cmd, "Gravity").IsVisible, "with no materials the study starts without masses, and hides gravity");
    Pick(cmd, "Slide").Index = 3; cmd.OnChanged();
    Check(Pick(cmd, "Gravity").IsVisible, "a free joint brings the forces in");
    Run(cmd);
    Check(Text(cmd).Contains("Assign a material to") && Text(cmd).Contains("Follower"), "which then asks for the material it needs");

    // a cam's lift as a table that repeats
    Pick(cmd, "Slide").Index = 5; cmd.OnChanged();
    var table = cmd.Params.OfType<TextParam>().First(p => p.Section == "Slide");
    table.Value = "time, lift\n0, 0\n0.25, 10\n0.5, 20\n0.75, 10\n1, 0";
    cmd.Params.OfType<BoolParam>().First(p => p.Label.StartsWith("Repeat")).Value = true;
    Num(cmd, "Duration").Value = 3; Num(cmd, "Output interval").Value = 0.05;
    Run(cmd);
    var r = cmd.Result!;
    Check(Near(r.Pos[0][r.SampleAt(1.5)], 20, 0.02) && Math.Abs(r.Pos[0][r.SampleAt(2.0)]) < 0.2 && Near(r.Pos[0][r.SampleAt(2.5)], 20, 0.02), "a table with a header repeats over the study, like a cam");
    Check(r.Vel[0].Select(Math.Abs).Max() > 30 && !r.Dynamic, "its speed comes from the table, with no masses needed");
    table.Value = "0, 0; 1, 10";
    Run(cmd);
    Check(Text(cmd).Contains("must end at the position it starts"), "a table that cannot repeat says so");
    table.Value = "0, 0; 0, 10"; cmd.Params.OfType<BoolParam>().First(p => p.Label.StartsWith("Repeat")).Value = false;
    Run(cmd);
    Check(Text(cmd).Contains("must increase"), "times have to increase");
    s.CancelCommand();

    // a table from a file, on a joint with a start of its own
    string file = Path.Combine(Path.GetTempPath(), "motion-table.csv"); File.WriteAllLines(file, ["t,x", "0,5", "2,25"]);
    s.StartCommand(() => new MotionStudyCommand()); cmd = (MotionStudyCommand)s.ActiveCommand!;
    Pick(cmd, "Slide").Index = 5; cmd.Params.OfType<TextParam>().First(p => p.Section == "Slide").Value = file; Num(cmd, "Duration").Value = 2;
    Run(cmd);
    Check(Near(cmd.Result!.Pos[0][0], 5, 1e-6) && Near(cmd.Result.Pos[0][^1], 25, 1e-6), "a table can come from a file, and the joint starts where it does");
    s.CancelCommand();

    // the tool path is a drive on the joints that should follow it, in the same study
    var arm = new CadSession();
    var baseP = Part(arm, "Base", Occ.Box(60, 60, 10), true, null, null);
    var l1 = Part(arm, "Link 1", Occ.Translate(Occ.Box(300, 20, 20), new Vec3(150, 0, 15)), false, null, null);
    var l2 = Part(arm, "Link 2", Occ.Translate(Occ.Box(200, 20, 20), new Vec3(400, 0, 15)), false, null, null);
    Joint(arm, "j1", "Shoulder", JointType.Revolute, baseP, l1, Vec3.Zero, new Vec3(0, 0, 1), new Vec3(1, 0, 0));
    Joint(arm, "j2", "Elbow", JointType.Revolute, l1, l2, new Vec3(300, 0, 0), new Vec3(0, 0, 1), new Vec3(1, 0, 0));
    var path = arm.AddObject(Occ.Line(new Vec3(420, 80, 25), new Vec3(420, -80, 25)), "Path", ObjKind.Curve);
    arm.StartCommand(() => new MotionStudyCommand()); cmd = (MotionStudyCommand)arm.ActiveCommand!;
    Check(!cmd.Slots[0].IsVisible && Num(cmd, "Duration").IsVisible, "tool and path picks stay hidden until a joint follows the path");
    Pick(cmd, "Shoulder").Index = 6; Pick(cmd, "Elbow").Index = 6; cmd.OnChanged();
    Check(cmd.Slots[0].IsVisible && cmd.Slots[1].IsVisible && !Num(cmd, "Duration").IsVisible, "choosing Follow the tool path shows them");
    cmd.Slots[0].TryAdd(FaceFacing(arm, arm.Find(l2.Id)!, new Vec3(1, 0, 0))); cmd.Slots[1].TryAdd(SelItem.Of(arm.Find(path.Id)!));
    Num(cmd, "Starting position", "Shoulder").Value = 20; Num(cmd, "Starting position", "Elbow").Value = -40;
    Run(cmd);
    Check(Text(cmd).Contains("Reached all"), "the same study plans the joints for the path");
    arm.CancelCommand();
}

// ---------------------------------------------------------------- Couple: a screw, a rack and a lever from one tool
{
    var s = new CadSession();
    var frame = Part(s, "Frame", Occ.Box(300, 60, 10), true);
    var screw = Part(s, "Screw", Occ.Translate(Occ.Cylinder(5, 8), new Vec3(0, 0, 20)), false);
    var nut = Part(s, "Nut", Occ.Translate(Occ.Box(20, 20, 20), new Vec3(60, 0, 30)), false);
    var carriage = Part(s, "Carriage", Occ.Translate(Occ.Box(20, 20, 20), new Vec3(120, 0, 30)), false);
    Joint(s, "js", "Screw turn", JointType.Revolute, frame, screw, new Vec3(0, 0, 20), new Vec3(0, 0, 1), new Vec3(1, 0, 0));
    Joint(s, "jn", "Nut slide", JointType.Slider, frame, nut, new Vec3(60, 0, 30), new Vec3(1, 0, 0), new Vec3(0, 1, 0));
    Joint(s, "jc", "Carriage slide", JointType.Slider, frame, carriage, new Vec3(120, 0, 30), new Vec3(1, 0, 0), new Vec3(0, 1, 0));

    s.StartCommand(() => new CouplingCommand()); var c = s.ActiveCommand!;
    Pick(c, "Following joint").Index = 1; c.OnChanged();
    Check(!Num(c, "Leading size").IsVisible && Num(c, "Diameter or lead").IsVisible, "a turning and a sliding joint ask for travel, not gear sizes");
    Pick(c, "Travel set by").Index = 1; Num(c, "Diameter or lead").Value = 5;
    Check(Text(c).Contains("5 mm per turn"), "a screw's lead is its travel per turn");
    s.CommitCommand();
    Check(s.Couplings.Single().Kind == CouplingKind.RackPinion && Near(s.Couplings.Single().Follow(360), 5, 1e-9), "one turn moves the nut one lead");

    s.StartCommand(() => new CouplingCommand()); c = s.ActiveCommand!;
    Pick(c, "Leading joint").Index = 1; Pick(c, "Following joint").Index = 2; c.OnChanged();
    Check(Num(c, "Following travel per mm").IsVisible, "two sliding joints take a plain ratio, like a lever");
    Num(c, "Following travel per mm").Value = 2; s.CommitCommand();

    s.StartCommand(() => new MotionStudyCommand(false)); var cmd = (MotionStudyCommand)s.ActiveCommand!;
    Pick(cmd, "Screw turn").Index = 1; Num(cmd, "Speed", "Screw turn").Value = 720; Num(cmd, "Duration").Value = 1; Num(cmd, "Output interval").Value = 0.1;
    Run(cmd);
    Check(Near(cmd.Result!.Pos[1][^1], 10, 1e-6) && Near(cmd.Result.Pos[2][^1], 20, 1e-6), "couplings chain: 2 turns of the screw move the nut 10 mm and the carriage 20 mm");
    s.CancelCommand();

    s.StartCommand(() => new MotionLoadCommand()); var load = s.ActiveCommand!;
    load.Slots[0].TryAdd(SelItem.Of(s.Find(carriage.Id)!));
    Pick(load, "Load").Index = 1; load.OnChanged();
    Check(!Num(load, "Force").IsVisible && Num(load, "Torque").IsVisible, "Force / Torque is one tool");
    Num(load, "Torque").Value = 2; s.CommitCommand();
    Check(s.Loads.Single().IsTorque && s.Loads.Single().Magnitude == 2, "the torque is stored as one");
}

// ---------------------------------------------------------------- dragging a closed loop by hand
{
    var s = new CadSession();
    var floor = Part(s, "Block", Occ.Translate(Occ.Box(400, 100, 10), new Vec3(150, 0, -10)), true);
    var crank = Part(s, "Crank", Occ.Translate(Occ.Box(50, 10, 20), new Vec3(25, 0, 0)), false);
    var rod = Part(s, "Rod", Occ.Translate(Occ.Box(150, 10, 20), new Vec3(125, 0, 0)), false);
    var piston = Part(s, "Piston", Occ.Translate(Occ.Box(30, 30, 20), new Vec3(200, 0, 0)), false);
    Joint(s, "j1", "Main bearing", JointType.Revolute, floor, crank, new Vec3(0, 0, 10), new Vec3(0, 0, 1), new Vec3(1, 0, 0));
    Joint(s, "j2", "Crank pin", JointType.Revolute, crank, rod, new Vec3(50, 0, 10), new Vec3(0, 0, 1), new Vec3(1, 0, 0));
    Joint(s, "j3", "Wrist pin", JointType.Revolute, rod, piston, new Vec3(200, 0, 10), new Vec3(0, 0, 1), new Vec3(1, 0, 0));
    Joint(s, "j4", "Cylinder wall", JointType.Slider, floor, piston, new Vec3(200, 0, 10), new Vec3(1, 0, 0), new Vec3(0, 1, 0));

    s.StartCommand(() => new DriveJointsCommand()); var drive = s.ActiveCommand!;
    Num(drive, "Main bearing").Value = 90;
    Check(Near(Num(drive, "Cylinder wall").Value, Math.Sqrt(150 * 150 - 50 * 50) - 200, 1e-6), "turning the crank by hand slides the piston to r·cosθ + √(L² − r²sin²θ)");
    Check(!Text(drive).Contains("not enforced"), "and the loop is closed, not skipped");
    s.CommitCommand();
    var j4 = s.JointObjects.First(j => j.joint.Key == "j4").joint;
    Check(Near(j4.Value, Math.Sqrt(150 * 150 - 50 * 50) - 200, 1e-6) && Near(s.Find(piston.Id)!.Component!.Placement.T.X, j4.Value, 1e-6), "Keep positions stores the closed linkage");

    s.StartCommand(() => new DriveJointsCommand()); drive = s.ActiveCommand!;
    Num(drive, "Cylinder wall").Value = -500;
    Check(Text(drive).Contains("Cannot go there") && Near(Num(drive, "Main bearing").Value, 90, 1e-6), "a slide past what the linkage can reach is refused, and the crank stays put");
    s.CancelCommand();
}

// ---------------------------------------------------------------- help
{
    var topic = Prism.Help.ToolHelp.ForCommand(new MotionStudyCommand());
    Check(topic != null && topic.Title == "Motion Study", "Motion Study has a help topic");
    var example = Prism.Help.ToolHelp.Example(topic!);
    Check(example.Before != null && example.After != null, "its before/after example can be drawn");
}

Console.WriteLine($"SUCCESS: {checks} checks");
