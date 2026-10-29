using System.Runtime.InteropServices;
using Prism.Commands;
using Prism.Core;
using Prism.Kernel;

NativeSearch.Initialize(); int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
void Set(Command command, string label, object value)
{
    var p = command.Params.Single(p => p.Label == label);
    switch (p) { case NumberParam n: n.Value = Convert.ToDouble(value); break; case BoolParam b: b.Value = (bool)value; break; case TextParam t: t.Value = (string)value; break; case ChoiceParam c: c.Index = (int)value; break; }
}
var s = new CadSession();
var boardCommand = new PcbBoardCommand(); s.StartCommand(boardCommand); s.CommitCommand();
var board = s.Objects.Single(); var props = board.Shape.Props();
Check(board.Shape.IsValid && board.Shape.HasSolids, "board is a valid solid");
Check(Math.Abs(props.Volume - (80 * 50 - 4 * Math.PI * 1.6 * 1.6) * 1.6) < .001, "four mounting holes remove the expected volume");
Check(Math.Abs(props.Size.Z - 1.6) < 1e-6 && board.Material?.Id == "gfrp", "board uses the requested thickness and FR4 material");
var holeCommand = new PcbHoleCommand(); s.StartCommand(holeCommand);
holeCommand.Slots[0].Items.Add(SelItem.Of(board)); holeCommand.OnChanged(); s.CommitCommand();
var drilled = s.Find(board.Id)!;
Check(drilled.Shape.IsValid && Math.Abs(props.Volume - drilled.Shape.Props().Volume - Math.PI * 1.6 * 1.6 * 1.6) < .001, "individual mounting hole cuts through the board");
Check(drilled.Material?.Id == "gfrp", "drilling retains the board material");
s.Undo(); Check(Math.Abs(s.Find(board.Id)!.Shape.Props().Volume - props.Volume) < .001, "undo restores the board before drilling");
s.Redo(); Check(s.Find(board.Id)!.Shape.Props().Volume < props.Volume, "redo restores the hole");
var misses = new PcbHoleCommand(); s.StartCommand(misses); misses.ConfigureInputs(() => { misses.Slots[0].Items.Add(SelItem.Of(s.Find(board.Id)!)); Set(misses,"Hole center X",500); }); misses.OnChanged();
Check(misses.TryBuild() == null && misses.Error != null, "a hole outside the board is rejected"); s.CancelCommand();
var bad = new PcbBoardCommand(); s.StartCommand(bad); Set(bad,"Hole center inset from edges",.5);
Check(bad.TryBuild() == null && bad.Error != null, "mounting holes that break the board edge are rejected"); s.CancelCommand();
var top = new ComponentEnvelopeCommand(); s.StartCommand(top); top.ConfigureInputs(() => { Set(top,"Reference designator","U1"); Set(top,"Center X",10); Set(top,"Body width (X)",12); Set(top,"Body length (Y)",6); Set(top,"Rotation about Z",90); }); top.OnChanged(); s.CommitCommand();
var chip = s.Objects.Single(o => o.Name.StartsWith("U1")); var cp = chip.Shape.Props();
Check(chip.Shape.IsValid && Math.Abs(cp.Min.Z - 1.6) < 1e-6, "top envelope starts on the board top face");
Check(Math.Abs(cp.Size.X - 6) < 1e-5 && Math.Abs(cp.Size.Y - 12) < 1e-5, "component rotation swaps footprint extents");
var bottom = new ComponentEnvelopeCommand(); s.StartCommand(bottom); bottom.ConfigureInputs(() => { Set(bottom,"Reference designator","U2"); Set(bottom,"Board side",1); Set(bottom,"Board face elevation (Z)",0); Set(bottom,"Clearance allowance",1); }); bottom.OnChanged(); s.CommitCommand();
var bp = s.Objects.Single(o => o.Name.StartsWith("U2")).Shape.Props();
Check(Math.Abs(bp.Max.Z) < 1e-6 && Math.Abs(bp.Min.Z + 4) < 1e-6 && Math.Abs(bp.Size.X - 12) < 1e-6, "bottom keepout grows away from the board with XY clearance");
var outline = s.AddObject(Occ.Polyline([new(0,0,0),new(20,0,0),new(20,10,0),new(0,10,0)],true),"Board outline",ObjKind.Curve);
var custom = new PcbOutlineCommand(); s.StartCommand(custom); custom.Slots[0].Items.Add(SelItem.Of(outline)); custom.OnChanged(); s.CommitCommand();
Check(s.Objects.Count(o=>o.Name.StartsWith("PCB Board")) == 2 && s.Objects.Last().Shape.IsValid, "a closed planar outline makes a custom board");
Check(Math.Abs(s.Objects.Last().Shape.Props().Volume - 320) < 1e-5 && s.Find(outline.Id) != null, "custom board thickness is correct and source outline is retained");
var folder = Path.GetFullPath("docs/ecad-studies"); Directory.CreateDirectory(folder);
s.Update(outline.Id,o=>o.WithVisible(false)); s.Save(Path.Combine(folder,"ecad-mechanical-demo.prism")); s.ExportStep(Path.Combine(folder,"ecad-mechanical-demo.step"));
var reopened = new CadSession(); reopened.Open(s.FilePath!);
Check(reopened.Objects.Count == s.Objects.Count && reopened.Objects.Where(o=>o.Kind==ObjKind.Solid).All(o=>o.Shape.IsValid), "ECAD geometry and components survive save/reopen");
Check(reopened.Objects.First(o=>o.Name.StartsWith("PCB Board")).Material?.Id == "gfrp", "board material survives save/reopen");
using var step = Occ.ImportStep(Path.Combine(folder,"ecad-mechanical-demo.step")); Check(step.IsValid && step.Count(SubKind.Solid)==4,"STEP exchange retains all four physical solids");
Console.WriteLine($"SUCCESS: {checks} checks");
static class NativeSearch
{
    [DllImport("kernel32",CharSet=CharSet.Unicode)] static extern bool SetDllDirectory(string path);
    public static void Initialize()=>SetDllDirectory(Path.GetFullPath("native/bin"));
}
