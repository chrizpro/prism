using Prism.Core.Assembly;
using Prism.Kernel;
using Prism.Simulation;

// Kinematic and rigid-body motion checked against closed-form physics. No CAD kernel is needed: parts are hand-made meshes.
int checks = 0;
void Check(bool ok, string text) { if (!ok) throw new Exception("FAIL " + text); Console.WriteLine("PASS " + text); checks++; }
bool Near(double a, double b, double rel) => Math.Abs(a - b) <= rel * Math.Max(Math.Abs(b), 1e-12);

(float[] pos, int[] idx) Box(double x0, double x1, double y0, double y1, double z0, double z1)
{
    var p = new List<float>(); foreach (var x in new[] { x0, x1 }) foreach (var y in new[] { y0, y1 }) foreach (var z in new[] { z0, z1 }) { p.Add((float)x); p.Add((float)y); p.Add((float)z); }
    // vertex index = 4*xi + 2*yi + zi; outward-facing triangles
    int[] t = [0,2,3, 0,3,1, 4,5,7, 4,7,6, 0,1,5, 0,5,4, 2,6,7, 2,7,3, 0,4,6, 0,6,2, 1,3,7, 1,7,5];
    return (p.ToArray(), t);
}
double[] Frame(Vec3 o, Vec3 x, Vec3 z) => [o.X, o.Y, o.Z, x.X, x.Y, x.Z, z.X, z.Y, z.Z];
JointInfo Joint(string key, JointType type, string parent, string child, Vec3 at, Vec3 childAt, Vec3 axis, Vec3 xdir) =>
    new(key, key, type, parent, child, Frame(at, xdir, axis), Frame(childAt, xdir, axis), false, 0, 0, 0, false, 0, 0, 0, 0);
Xform At(Vec3 t) => Xform.Translation(t);

// ---------------------------------------------------------------- mass properties
{
    var (pos, idx) = Box(-5, 5, -10, 10, -15, 15);                              // 10 × 20 × 30 mm
    var b = MotionMass.FromMesh("b", "box", pos, idx, 2.7e-6, out double vol);
    double m = 2.7e-6 * 6000;
    Check(Near(vol, 6000, 1e-9) && Near(b.Mass, m, 1e-9), $"box mass from the mesh is {b.Mass:G5} kg (6000 mm³ of aluminium)");
    Check(b.Com.Length < 1e-6, "centre of mass of a centred box is at its centre");
    Check(Near(b.Inertia[0], m * (0.02 * 0.02 + 0.03 * 0.03) / 12, 1e-6) && Near(b.Inertia[8], m * (0.01 * 0.01 + 0.02 * 0.02) / 12, 1e-6), "box inertia matches m(b²+c²)/12 about each axis");
    var (p2, i2) = Box(95, 105, -10, 10, -15, 15);                                // same box far from the origin
    var far = MotionMass.FromMesh("f", "far", p2, i2, 2.7e-6, out _);
    Check(Near(far.Com.X, 100, 1e-6) && Near(far.Inertia[0], b.Inertia[0], 1e-5), "moving a part away from the origin changes its centre of mass but not its inertia about it");
    var flipped = MotionMass.FromMesh("i", "inside-out", pos, idx.Reverse().ToArray(), 2.7e-6, out double v3);
    Check(v3 > 0 && Near(flipped.Mass, m, 1e-9), "an inside-out mesh still has positive mass");
}

// ---------------------------------------------------------------- a swinging rod: period and energy
{
    var (pos, idx) = Box(-5, 5, -5, 5, -200, 0);                                  // 10 × 10 × 200 mm rod hanging below the pivot
    var rod = MotionMass.FromMesh("rod", "Rod", pos, idx, 7.85e-6, out _);
    var start = new Dictionary<string, Xform> { ["base"] = Xform.Identity, ["rod"] = Xform.Identity };
    var hinge = Joint("hinge", JointType.Revolute, "base", "rod", Vec3.Zero, Vec3.Zero, new Vec3(0, 1, 0), new Vec3(1, 0, 0));
    var dofs = MotionEngine.DofsOf([hinge], []);
    dofs[0].Mode = DriveMode.Free; dofs[0].Start = 3;
    var engine = new MotionEngine(start, new HashSet<string> { "base" }, [hinge], [], [rod], dofs);
    var r = engine.Run(new MotionSettings(2.0, 0.005, true, new Vec3(0, 0, -9.80665)));
    double L = 0.2, w = 0.01, d = L / 2, ip = rod.Mass * (L * L + w * w) / 12 + rod.Mass * d * d;
    double period = 2 * Math.PI * Math.Sqrt(ip / (rod.Mass * 9.80665 * d)) * (1 + Math.Pow(3 * Math.PI / 180, 2) / 16);
    var cross = new List<double>();
    for (int i = 1; i < r.Samples; i++) if (r.Pos[0][i - 1] > 0 && r.Pos[0][i] <= 0) cross.Add(r.Time[i - 1] + r.Time[1] * r.Pos[0][i - 1] / (r.Pos[0][i - 1] - r.Pos[0][i]));
    for (int i = 1; i < r.Samples; i++) if (r.Pos[0][i - 1] < 0 && r.Pos[0][i] >= 0) cross.Add(r.Time[i - 1] + r.Time[1] * -r.Pos[0][i - 1] / (r.Pos[0][i] - r.Pos[0][i - 1]));
    cross.Sort();
    double measured = 2 * (cross[1] - cross[0]);
    Check(Near(measured, period, 0.003), $"physical pendulum period {measured:G5} s matches theory {period:G5} s");
    double e0 = r.Kinetic[0] + r.Potential[0], worst = 0;
    for (int i = 0; i < r.Samples; i++) worst = Math.Max(worst, Math.Abs(r.Kinetic[i] + r.Potential[i] - e0));
    double swing = rod.Mass * 9.80665 * d * (1 - Math.Cos(3 * Math.PI / 180));
    Check(worst < 2e-3 * swing, $"energy is conserved over two seconds (drift {worst / swing:P3} of the swing energy)");
    Check(Math.Abs(r.Pos[0].Max() - 3) < 0.02 && Math.Abs(r.Pos[0].Min() + 3) < 0.02, "the swing keeps its amplitude of 3°");
}

// ---------------------------------------------------------------- driven pendulum: motor torque from inverse dynamics
{
    var (pos, idx) = Box(-5, 5, -5, 5, -200, 0);
    var rod = MotionMass.FromMesh("rod", "Rod", pos, idx, 7.85e-6, out _);
    var start = new Dictionary<string, Xform> { ["base"] = Xform.Identity, ["rod"] = Xform.Identity };
    var hinge = Joint("hinge", JointType.Revolute, "base", "rod", Vec3.Zero, Vec3.Zero, new Vec3(0, 1, 0), new Vec3(1, 0, 0));
    var dofs = MotionEngine.DofsOf([hinge], []);
    dofs[0].Mode = DriveMode.Hold; dofs[0].Start = 90;                        // held out sideways
    var engine = new MotionEngine(start, new HashSet<string> { "base" }, [hinge], [], [rod], dofs);
    var r = engine.Run(new MotionSettings(0.1, 0.05, true, new Vec3(0, 0, -9.80665)));
    double holding = rod.Mass * 9.80665 * 0.1;
    Check(Near(Math.Abs(r.Effort[0][0]), holding, 1e-4), $"holding the rod level takes m·g·d = {holding:G5} N·m (solver says {Math.Abs(r.Effort[0][0]):G5})");
    dofs[0].Start = 0;
    var hanging = new MotionEngine(start, new HashSet<string> { "base" }, [hinge], [], [rod], dofs).Run(new MotionSettings(0.1, 0.05, true, new Vec3(0, 0, -9.80665)));
    Check(Math.Abs(hanging.Effort[0][0]) < 1e-9, "a hanging rod needs no holding torque");
    // spinning it at constant speed with gravity off: no torque
    dofs[0].Mode = DriveMode.Constant; dofs[0].Rate = 360;
    var spin = new MotionEngine(start, new HashSet<string> { "base" }, [hinge], [], [rod], dofs).Run(new MotionSettings(0.2, 0.05, true, Vec3.Zero));
    Check(spin.Effort[0].All(e => Math.Abs(e) < 1e-9), "spinning at constant speed in zero gravity needs no torque");
    // accelerating it: torque = I·α (oscillate A·ω²)
    dofs[0].Mode = DriveMode.Oscillate; dofs[0].Start = 0; dofs[0].Amplitude = 20; dofs[0].Frequency = 1;
    var shake = new MotionEngine(start, new HashSet<string> { "base" }, [hinge], [], [rod], dofs).Run(new MotionSettings(1.0, 0.01, true, Vec3.Zero));
    double ip = rod.Mass * (0.04 + 0.0001) / 12 + rod.Mass * 0.01;
    double expect = ip * (20 * Math.PI / 180) * Math.Pow(2 * Math.PI, 2);
    Check(Near(shake.Effort[0].Max(), expect, 2e-3), $"oscillating the rod takes peak torque I·A·ω² = {expect:G5} N·m (solver {shake.Effort[0].Max():G5})");
    Check(Near(shake.Vel[0].Max(), 20 * 2 * Math.PI, 1e-3), "peak joint speed of the oscillation is A·ω");
}

// ---------------------------------------------------------------- gears: ratio, direction and reflected inertia
{
    var wheel = MotionMass.FromMesh("b", "Wheel", Box(-10, 10, -10, 10, -2, 2).pos, Box(-10, 10, -10, 10, -2, 2).idx, 7.85e-6, out _);
    var start = new Dictionary<string, Xform> { ["base"] = Xform.Identity, ["a"] = Xform.Identity, ["b"] = At(new Vec3(30, 0, 0)) };
    var ja = Joint("ja", JointType.Revolute, "base", "a", Vec3.Zero, Vec3.Zero, new Vec3(0, 0, 1), new Vec3(1, 0, 0));
    var jb = Joint("jb", JointType.Revolute, "base", "b", new Vec3(30, 0, 0), Vec3.Zero, new Vec3(0, 0, 1), new Vec3(1, 0, 0));
    var gear = new CouplingInfo("g", "Gear", CouplingKind.Gear, "ja", "jb", CouplingInfo.GearRatio(20, 40), true);
    var shaftB = new MotionBody("b", "Wheel B", wheel.Mass, wheel.Com, wheel.Inertia);
    var shaftA = new MotionBody("a", "Wheel A", 0, Vec3.Zero, new double[9]);
    var dofs = MotionEngine.DofsOf([ja, jb], [gear]);
    Check(dofs.Count == 2 && !dofs[0].Follower && dofs[1].Follower, "the driven gear is a follower: it has no motion of its own");
    dofs[0].Mode = DriveMode.Constant; dofs[0].Rate = 360;
    var k = new MotionEngine(start, new HashSet<string> { "base" }, [ja, jb], [gear], [shaftA, shaftB], dofs).Run(new MotionSettings(0.5, 0.05, false, Vec3.Zero));
    Check(Near(k.Vel[1][3], -180, 1e-4) && Near(k.Pos[1][4], -k.Pos[0][4] / 2, 1e-4), $"a 20:40 pair turns the driven gear half as fast the opposite way ({k.Vel[1][3]:0.###} °/s)");
    Check(Near(k.AngSpeed[1][3], 180 * Math.PI / 180, 1e-3), "the driven wheel's body really turns at that speed");
    // torque through the pair: a load of 2 N·m on the driven shaft needs 1 N·m at the driving shaft (massless wheels)
    var plain = MotionEngine.DofsOf([ja, jb], [gear]); plain[0].Mode = DriveMode.Constant; plain[0].Rate = 360; plain[1].Effort = -2;
    var massless = new MotionBody("b", "Wheel B", 0, Vec3.Zero, new double[9]);
    var t = new MotionEngine(start, new HashSet<string> { "base" }, [ja, jb], [gear], [shaftA, massless], plain).Run(new MotionSettings(0.2, 0.05, true, Vec3.Zero));
    Check(Near(Math.Abs(t.Effort[0][2]), 1.0, 1e-6), $"2 N·m of load on the 2:1 reduction needs {Math.Abs(t.Effort[0][2]):G4} N·m at the input");
    Check(Near(Math.Abs(t.Power[0][2]), 2 * Math.Abs(t.Vel[1][2] * Math.PI / 180), 1e-4), "input power equals output power for ideal gears");
    // reflected inertia
    var load = MotionEngine.DofsOf([ja, jb], [gear]); load[0].Mode = DriveMode.Oscillate; load[0].Amplitude = 30; load[0].Frequency = 1;
    var s = new MotionEngine(start, new HashSet<string> { "base" }, [ja, jb], [gear], [shaftA, shaftB], load).Run(new MotionSettings(1.0, 0.01, true, Vec3.Zero));
    double izz = wheel.Inertia[8], want = 0.25 * izz * (30 * Math.PI / 180) * Math.Pow(2 * Math.PI, 2);
    Check(Near(s.Effort[0].Max(), want, 3e-3), $"the driven wheel's inertia appears at the input reduced by the ratio squared ({s.Effort[0].Max():G4} vs {want:G4} N·m)");
    // a free driving gear spun up by a motor torque drives the other: free dynamics through the coupling
    var motor = MotionEngine.DofsOf([ja, jb], [gear]); motor[0].Mode = DriveMode.Free; motor[0].Effort = 0.001;
    var f = new MotionEngine(start, new HashSet<string> { "base" }, [ja, jb], [gear], [shaftA, shaftB], motor).Run(new MotionSettings(0.5, 0.01, true, Vec3.Zero));
    double alpha = 0.001 / (0.25 * izz);
    Check(Near(f.Vel[0][^1], alpha * 0.5 * 180 / Math.PI, 2e-3), $"a motor torque of 1 mN·m spins the pair up at {alpha:G4} rad/s²");
}

// ---------------------------------------------------------------- rack and pinion
{
    var start = new Dictionary<string, Xform> { ["base"] = Xform.Identity, ["pin"] = Xform.Identity, ["rack"] = Xform.Identity };
    var jp = Joint("jp", JointType.Revolute, "base", "pin", Vec3.Zero, Vec3.Zero, new Vec3(0, 0, 1), new Vec3(1, 0, 0));
    var js = Joint("js", JointType.Slider, "base", "rack", new Vec3(0, 10, 0), Vec3.Zero, new Vec3(1, 0, 0), new Vec3(0, 1, 0));
    var rack = new CouplingInfo("r", "Rack", CouplingKind.RackPinion, "jp", "js", CouplingInfo.RackRatio(20), false);
    var dofs = MotionEngine.DofsOf([jp, js], [rack]); dofs[0].Mode = DriveMode.Constant; dofs[0].Rate = 360;
    var r = new MotionEngine(start, new HashSet<string> { "base" }, [jp, js], [rack], [], dofs).Run(new MotionSettings(1.0, 0.25, false, Vec3.Zero));
    Check(Near(r.Pos[1][^1], Math.PI * 20, 1e-9), $"one turn of a 20 mm pinion moves the rack {r.Pos[1][^1]:0.###} mm (π × diameter)");
    Check(Near(r.Vel[1][2], Math.PI * 20, 1e-6), "the rack's speed is the pitch circle speed");
}

// ---------------------------------------------------------------- a crank: path and speed of a point on the end of an arm
{
    var arm = new MotionBody("arm", "Arm", 0, new Vec3(100, 0, 0), new double[9]);
    var start = new Dictionary<string, Xform> { ["base"] = Xform.Identity, ["arm"] = Xform.Identity };
    var j = Joint("j", JointType.Revolute, "base", "arm", Vec3.Zero, Vec3.Zero, new Vec3(0, 0, 1), new Vec3(1, 0, 0));
    var dofs = MotionEngine.DofsOf([j], []); dofs[0].Mode = DriveMode.Constant; dofs[0].Rate = 90;
    var r = new MotionEngine(start, new HashSet<string> { "base" }, [j], [], [arm], dofs).Run(new MotionSettings(4.0, 0.5, false, Vec3.Zero));
    Check(Near(r.Speed[0][3], 0.1 * Math.PI / 2, 1e-4), $"a point 100 mm out on an arm turning 90°/s moves at {r.Speed[0][3]:G5} m/s");
    Check(Near(r.Accel[0][3], 0.1 * Math.Pow(Math.PI / 2, 2), 1e-3), $"and accelerates toward the axis at {r.Accel[0][3]:G5} m/s²");
    Check((r.Path[0][4] - new Vec3(-100, 0, 0)).Length < 1e-6 && (r.Path[0][8] - new Vec3(100, 0, 0)).Length < 1e-6, "the path comes back round after a full turn");
}

// ---------------------------------------------------------------- limits and errors
{
    var (pos, idx) = Box(-5, 5, -5, 5, -200, 0);
    var rod = MotionMass.FromMesh("rod", "Rod", pos, idx, 7.85e-6, out _);
    var start = new Dictionary<string, Xform> { ["base"] = Xform.Identity, ["rod"] = Xform.Identity };
    var hinge = Joint("hinge", JointType.Revolute, "base", "rod", Vec3.Zero, Vec3.Zero, new Vec3(0, 1, 0), new Vec3(1, 0, 0)) with { HasLimits = true, Min = -20, Max = 20 };
    var dofs = MotionEngine.DofsOf([hinge], []); dofs[0].Mode = DriveMode.Free; dofs[0].Start = 15; dofs[0].Rate = 120;
    var r = new MotionEngine(start, new HashSet<string> { "base" }, [hinge], [], [rod], dofs).Run(new MotionSettings(2.0, 0.01, true, new Vec3(0, 0, -9.80665)));
    Check(r.Pos[0].Max() <= 20 + 1e-9 && r.Pos[0].Min() >= -20 - 1e-9 && r.Events.Any(e => e.Contains("limit")), "a free joint stops at its limit and says so");
    var weightless = new MotionBody("rod", "Rod", 0, Vec3.Zero, new double[9]);
    bool failed = false;
    try { new MotionEngine(start, new HashSet<string> { "base" }, [hinge], [], [weightless], MotionEngine.DofsOf([hinge], []).Select(d => { d.Mode = DriveMode.Free; return d; })).Run(new MotionSettings(1, 0.1, true, Vec3.Zero)); }
    catch (InvalidOperationException) { failed = true; }
    Check(failed, "a free joint with nothing that has mass is reported instead of dividing by zero");
}


// ---------------------------------------------------------------- couplings: chains, loops, saving
{
    JointInfo Rev(string k) => Joint(k, JointType.Revolute, "base", "p" + k, Vec3.Zero, Vec3.Zero, new Vec3(0, 0, 1), new Vec3(1, 0, 0));
    var a = Rev("a") with { Value = 90 }; var b = Rev("b"); var c = Rev("c");
    var ab = new CouplingInfo("1", "A-B", CouplingKind.Gear, "a", "b", 0.5, true);
    var bc = new CouplingInfo("2", "B-C", CouplingKind.Gear, "b", "c", 2, false);
    var notes = new List<string>();
    var set = CouplingSolver.Apply([c, b, a], [bc, ab], notes).ToDictionary(j => j.Key);
    Check(Near(set["b"].Value, -45, 1e-9) && Near(set["c"].Value, -90, 1e-9), "a chain of gear pairs is followed in order, whatever order they are listed in");
    var loop = CouplingSolver.Apply([a, b], [ab, new CouplingInfo("3", "B-A", CouplingKind.Gear, "b", "a", 2, true)], notes);
    Check(notes.Any(n => n.Contains("loop")) || loop.Count == 2, "a coupling that closes a loop is not enforced and is reported");
    var two = new List<string>(); CouplingSolver.Apply([a, b, c], [ab, new CouplingInfo("4", "C-B", CouplingKind.Gear, "c", "b", 1, false)], two);
    Check(two.Any(n => n.Contains("already follows")), "a joint cannot follow two leaders");
    var starts = new Dictionary<string, Xform> { ["base"] = Xform.Identity, ["pa"] = Xform.Identity, ["pb"] = Xform.Identity };
    var placed = AssemblyKinematics.Solve(starts, new HashSet<string> { "base" }, [a, b], [ab], []);
    Check(Near(placed["pb"].AngleAboutZ() * 180 / Math.PI, -45, 1e-6) && Near(placed["pa"].AngleAboutZ() * 180 / Math.PI, 90, 1e-6), "the assembly solver turns the driven gear when the driving gear is turned");
    var json = System.Text.Json.JsonSerializer.Serialize<AssemblyData>(ab);
    var back = System.Text.Json.JsonSerializer.Deserialize<AssemblyData>(json) as CouplingInfo;
    Check(back != null && back.LeaderKey == "a" && back.Reverse && Near(back.Ratio, 0.5, 1e-12) && json.Contains("coupling"), "a gear coupling is saved with the document and reads back");
    Check(Near(CouplingInfo.GearRatio(20, 40), 0.5, 1e-12) && Near(CouplingInfo.RackRatio(20) * 360, Math.PI * 20, 1e-12), "ratios come from tooth counts and pinion diameter");
}

// ---------------------------------------------------------------- a driven joint asked past its limit holds there
{
    var arm = new MotionBody("arm", "Arm", 0, new Vec3(100, 0, 0), new double[9]);
    var start = new Dictionary<string, Xform> { ["base"] = Xform.Identity, ["arm"] = Xform.Identity };
    var j = Joint("j", JointType.Revolute, "base", "arm", Vec3.Zero, Vec3.Zero, new Vec3(0, 0, 1), new Vec3(1, 0, 0)) with { HasLimits = true, Min = -30, Max = 30 };
    var dofs = MotionEngine.DofsOf([j], []); dofs[0].Mode = DriveMode.Constant; dofs[0].Rate = 60;
    var r = new MotionEngine(start, new HashSet<string> { "base" }, [j], [], [arm], dofs).Run(new MotionSettings(2.0, 0.1, false, Vec3.Zero));
    Check(Near(r.Pos[0][^1], 30, 1e-9) && r.Vel[0][^1] == 0 && r.Events.Any(e => e.Contains("past its limit")), "a motor asked to go past a joint's limit holds there and says so");
    Check(Math.Abs(Math.Atan2(r.Path[0][^1].Y, r.Path[0][^1].X) * 180 / Math.PI - 30) < 1e-6, "and the part really stops at the limit");
}

// ---------------------------------------------------------------- drives: smooth ramp, planned table, motor curve
{
    var arm = new MotionBody("arm", "Arm", 0, new Vec3(100, 0, 0), new double[9]);
    var start = new Dictionary<string, Xform> { ["base"] = Xform.Identity, ["arm"] = Xform.Identity };
    var j = Joint("j", JointType.Revolute, "base", "arm", Vec3.Zero, Vec3.Zero, new Vec3(0, 0, 1), new Vec3(1, 0, 0));
    var dofs = MotionEngine.DofsOf([j], []); dofs[0].Mode = DriveMode.Ramp; dofs[0].Target = 90; dofs[0].RampStart = 0.5; dofs[0].RampTime = 1;
    var r = new MotionEngine(start, new HashSet<string> { "base" }, [j], [], [arm], dofs).Run(new MotionSettings(2.5, 0.01, false, Vec3.Zero));
    Check(r.Pos[0][0] == 0 && Near(r.Pos[0][^1], 90, 1e-9) && r.Vel[0][^1] == 0 && Math.Abs(r.Vel[0][40]) < 1e-9, "a ramp waits, moves to its target and stops there");
    Check(Near(r.Vel[0].Max(), 1.875 * 90, 2e-3) && Near(r.Pos[0][100], 45, 1e-6), "a ramp is a minimum-jerk move: half way at half time, peak speed 1.875 × distance ÷ time");
    var ts = Enumerable.Range(0, 51).Select(i => i * 0.1).ToArray(); var spline = new CubicSpline(ts, ts.Select(Math.Sin).ToArray());
    double worstV = 0, worstA = 0; for (double t = 0.5; t < 4.5; t += 0.013) { var (q, v, a) = spline.At(t); worstV = Math.Max(worstV, Math.Abs(v - Math.Cos(t))); worstA = Math.Max(worstA, Math.Abs(a + Math.Sin(t))); }
    Check(worstV < 1e-4 && worstA < 2e-3, "a spline through samples gives smooth speed and acceleration between them");

    // a motor with a torque–speed curve spins a flywheel up exponentially toward its no-load speed
    var (pos, idx) = Box(-20, 20, -20, 20, -2, 2); var wheel = MotionMass.FromMesh("arm", "Wheel", pos, idx, 7.85e-6, out _);
    var spin = MotionEngine.DofsOf([j], []); spin[0].Mode = DriveMode.Free; spin[0].MotorStall = 0.002; spin[0].MotorNoLoad = 600;
    var m = new MotionEngine(start, new HashSet<string> { "base" }, [j], [], [wheel], spin).Run(new MotionSettings(1.0, 0.01, true, Vec3.Zero));
    double tau = wheel.Inertia[8] * (600 * Math.PI / 180) / 0.002;
    Check(Near(m.Vel[0][^1], 600 * (1 - Math.Exp(-1 / tau)), 2e-3), $"a motor's speed rises along its torque–speed curve (time constant {tau:0.###} s)");
}

// ---------------------------------------------------------------- force elements: springs, actuators, loads, payloads
{
    var (pos, idx) = Box(-10, 10, -10, 10, -10, 10); var block = MotionMass.FromMesh("blk", "Block", pos, idx, 7.85e-6, out _);
    var start = new Dictionary<string, Xform> { ["base"] = Xform.Identity, ["blk"] = Xform.Identity };
    var slide = Joint("s", JointType.Slider, "base", "blk", Vec3.Zero, Vec3.Zero, new Vec3(1, 0, 0), new Vec3(0, 1, 0));
    MotionResult RunWith(MotionForce f, double x0, double duration, Action<MotionDof>? tweak = null, double dt = 0.002)
    {
        var d = MotionEngine.DofsOf([slide], []); d[0].Mode = DriveMode.Free; d[0].Start = x0; tweak?.Invoke(d[0]);
        return new MotionEngine(start, new HashSet<string> { "base" }, [slide], [], [block], d, [f]).Run(new MotionSettings(duration, dt, true, Vec3.Zero));
    }
    var spring = new SpringForce("Spring", "blk", Vec3.Zero, null, new Vec3(-100, 0, 0), 0.1, 0, 100, 0);        // 100 N/m to a wall 100 mm away
    var sr = RunWith(spring, 10, 0.5);
    double omega = Math.Sqrt(100 / block.Mass), period = 2 * Math.PI / omega;
    int zero = Array.FindIndex(sr.Pos[0], v => v <= 0);
    double quarter = sr.Time[zero - 1] + 0.002 * sr.Pos[0][zero - 1] / (sr.Pos[0][zero - 1] - sr.Pos[0][zero]);
    Check(Near(quarter, period / 4, 0.003), $"a block on a 100 N/m spring has the natural frequency √(k/m): quarter period {quarter:0.####} s vs {period / 4:0.####} s");
    double e0 = sr.Kinetic[0] + 0.5 * 100 * 0.01 * 0.01; double worst = 0;
    for (int i = 0; i < sr.Samples; i++) { double x = sr.Pos[0][i] * 1e-3; worst = Math.Max(worst, Math.Abs(sr.Kinetic[i] + 0.5 * 100 * x * x - e0)); }
    Check(worst < 1e-3 * e0, "kinetic plus spring energy stays constant");
    Check(Near(sr.ForceValue[0][0], 0.1 * 10, 1e-6), "the spring reports the force it carries (1 N for 10 mm at 0.1 N/mm)");
    var damped = RunWith(spring with { Damping = 0.0004 }, 10, 1.0);
    double decay = Math.Abs(damped.Pos[0].Skip(300).Max()) / 10;
    Check(decay < 0.3 && decay > 0.001, "a damper takes energy out of the oscillation");

    var cylinder = new SpringForce("Cylinder", "blk", Vec3.Zero, null, new Vec3(-100, 0, 0), 0, 0, 100, 5);        // pushes with 5 N
    var cr = RunWith(cylinder, 0, 0.1);
    Check(Near(cr.Acc[0][5], 5 / block.Mass * 1000, 1e-3) && Near(cr.ForceValue[0][5], -5, 1e-9), "a cylinder's push accelerates the block by F/m");
    var load = RunWith(new PointForce("Push", "blk", Vec3.Zero, new Vec3(1, 0, 0), false, 2, LoadProfile.Always), 0, 0.1);
    Check(Near(load.Acc[0][5], 2 / block.Mass * 1000, 1e-3), "a force on a part accelerates it by F/m");
    var pulse = RunWith(new PointForce("Kick", "blk", Vec3.Zero, new Vec3(1, 0, 0), false, 2, new LoadProfile(LoadShape.Pulse, 0.05, 0.02)), 0, 0.2, null, 0.005);
    double dv = pulse.Vel[0][^1] / 1000, expected = 2 * 0.02 / block.Mass;
    Check(Near(dv, expected, 1e-3) && Math.Abs(pulse.Vel[0][5]) < 1e-9, "a pulse of force gives the block the impulse F·t");

    // torque and payload on a hinge
    var hinge = Joint("h", JointType.Revolute, "base", "blk", Vec3.Zero, Vec3.Zero, new Vec3(0, 0, 1), new Vec3(1, 0, 0));
    var massless = new MotionBody("blk", "Arm", 0, Vec3.Zero, new double[9]);
    var hold = MotionEngine.DofsOf([hinge], []);
    var payload = new MotionPoint("Payload", "blk", new Vec3(300, 0, 0), 2.0);
    var held = new MotionEngine(start, new HashSet<string> { "base" }, [hinge], [], [massless], hold, null, [payload]).Run(new MotionSettings(0.1, 0.05, true, new Vec3(0, -9.80665, 0)));
    Check(Near(Math.Abs(held.Effort[0][0]), 2.0 * 9.80665 * 0.3, 1e-6), $"a 2 kg payload 300 mm out needs {Math.Abs(held.Effort[0][0]):0.###} N·m to hold");
    var spun = MotionEngine.DofsOf([hinge], []); spun[0].Mode = DriveMode.Free;
    var tq = new MotionEngine(start, new HashSet<string> { "base" }, [hinge], [], [massless], spun, [new BodyTorque("T", "blk", new Vec3(0, 0, 1), false, 0.6, LoadProfile.Always)], [payload]).Run(new MotionSettings(0.1, 0.05, true, Vec3.Zero));
    Check(Near(tq.Acc[0][1], 0.6 / (2.0 * 0.09) * 180 / Math.PI, 1e-4), "a torque spins a payload up at T/(m r²)");
}

// ---------------------------------------------------------------- a two-link arm following a line: inverse kinematics and joint torques
{
    var start = new Dictionary<string, Xform> { ["base"] = Xform.Identity, ["l1"] = Xform.Identity, ["l2"] = At(new Vec3(300, 0, 0)) };
    var j1 = Joint("j1", JointType.Revolute, "base", "l1", Vec3.Zero, Vec3.Zero, new Vec3(0, 0, 1), new Vec3(1, 0, 0));
    var j2 = Joint("j2", JointType.Revolute, "l1", "l2", new Vec3(300, 0, 0), Vec3.Zero, new Vec3(0, 0, 1), new Vec3(1, 0, 0));
    var l1 = new MotionBody("l1", "Link 1", 0, new Vec3(150, 0, 0), new double[9]); var l2 = new MotionBody("l2", "Link 2", 0, new Vec3(100, 0, 0), new double[9]);
    var tool = new MotionPoint("Tool", "l2", new Vec3(200, 0, 0), 0);
    MotionEngine Arm(IEnumerable<MotionDof> dofs, MotionPoint[]? pts = null) => new(start, new HashSet<string> { "base" }, [j1, j2], [], [l1, l2], dofs, null, pts ?? [tool]);
    var dofs0 = MotionEngine.DofsOf([j1, j2], []); dofs0[0].Start = 20; dofs0[1].Start = -40;
    var eng = Arm(dofs0);
    var ik = eng.InverseKinematics(eng.StartVector(), [0, 1], "l2", new Vec3(200, 0, 0), new Vec3(350, 150, 0), null);
    var (tip, _) = eng.ToolPose(ik.Q, "l2", new Vec3(200, 0, 0));
    Check(ik.Converged && (tip - new Vec3(350, 150, 0)).Length < 1e-4, $"inverse kinematics puts the tool on the target (error {ik.PositionError:G2} mm)");
    var far = eng.InverseKinematics(eng.StartVector(), [0, 1], "l2", new Vec3(200, 0, 0), new Vec3(700, 0, 0), null);
    Check(!far.Converged && far.PositionError > 190, "a target beyond the arm's reach is reported as unreachable");
    var lim = MotionEngine.DofsOf([j1, j2], []); lim[1] = new MotionDof { JointKey = "j2", JointName = "j2", Name = "j2", Angular = true, Limited = true, Lo = 0, Hi = 10, Start = 5 }; lim[0].Start = 20;
    var limited = Arm(lim).InverseKinematics(Arm(lim).StartVector(), [0, 1], "l2", new Vec3(200, 0, 0), new Vec3(350, -150, 0), null);
    Check(!limited.Converged, "a joint limit that blocks the target is respected, not violated");

    var path = new List<Vec3> { new(380, 100, 0), new(380, -100, 0) };
    var plan = RobotPath.Follow(eng, [0, 1], "l2", new Vec3(200, 0, 0), path, 150, 600, 0.02, false);
    Check(plan.Unreached == 0 && plan.MaxPositionError < 1e-3 && plan.PathLength > 200, $"a planned path along a line reaches every point (worst error {plan.MaxPositionError:G2} mm over {plan.PathLength:0.#} mm)");
    var (tot, dist) = RobotPath.TimeLaw(200, 150, 600);
    Check(Near(dist(tot), 200, 1e-9) && dist(0) == 0 && Near(tot, 200 / 150.0 + 150.0 / 600, 1e-9), "the speed profile accelerates, cruises and brakes to land exactly on the end");

    var drive = MotionEngine.DofsOf([j1, j2], []); drive[0].Start = 20; drive[1].Start = -40;
    for (int a = 0; a < 2; a++) drive[a].SetTable(plan.Times, plan.JointValues[a]);
    var played = Arm(drive).Run(new MotionSettings(plan.Times[^1], plan.Times[1] / 3, false, Vec3.Zero));
    int toolIdx = Array.FindIndex(played.Bodies, b => b.Name == "Tool");
    int ti = Array.FindIndex(played.Bodies, b => b.Name == "Tool");
    var tp = played.Path[ti];
    int onLine = Array.FindIndex(tp, p => Math.Abs(p.X - 380) < 0.05 && p.Y < 62);
    Check(onLine > 0 && tp.Skip(onLine).All(p => Math.Abs(p.X - 380) < 0.1), "once past the rounded corner the tool stays on the straight line between planned samples");
    Check((tp[^1] - new Vec3(380, -100, 0)).Length < 1e-3 && (tp[0] - eng.ToolPose(eng.StartVector(), "l2", new Vec3(200, 0, 0)).Point).Length < 1e-6, "it starts where the arm is and ends on the last path point");
    var sp = played.Speed[ti].Select(v => v * 1000).ToArray(); int cruiseFrom = sp.ToList().FindIndex(v => v > 140), cruiseTo = sp.ToList().FindLastIndex(v => v > 140);
    Check(sp.Max() < 156 && sp.Skip(cruiseFrom).Take(cruiseTo - cruiseFrom).Min() > 140, $"the tool moves at the planned speed through the rounded corner ({sp.Skip(cruiseFrom).Take(cruiseTo - cruiseFrom).Min():0}–{sp.Max():0} mm/s)");

    // static torques with a payload on the arm
    var hold = MotionEngine.DofsOf([j1, j2], []); hold[0].Start = 0; hold[1].Start = 0;
    var weight = new MotionPoint("Load", "l2", new Vec3(200, 0, 0), 1.5);
    var held = Arm(hold, [weight]).Run(new MotionSettings(0.1, 0.05, true, new Vec3(0, -9.80665, 0)));
    Check(Near(Math.Abs(held.Effort[0][0]), 1.5 * 9.80665 * 0.5, 1e-6) && Near(Math.Abs(held.Effort[1][0]), 1.5 * 9.80665 * 0.2, 1e-6), "an arm stretched out holding 1.5 kg needs m·g·reach at the shoulder and m·g·(reach from the elbow) at the elbow");
}

Console.WriteLine($"{checks} checks passed");
