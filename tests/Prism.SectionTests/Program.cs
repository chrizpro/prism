using System.Runtime.InteropServices;
using Prism.Core;using Prism.Commands;using Prism.Kernel;
Native.SetDllDirectory(Path.GetFullPath("native/bin"));int checks=0;void Check(bool yes,string message){if(!yes)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
var s=new CadSession();var cube=s.AddObject(Occ.Box(40,40,40),"Cube",ObjKind.Solid);s.StartCommand(()=>new SectionCommand());var cmd=(SectionCommand)s.ActiveCommand!;
Check(s.Clip!=null&&Math.Abs(s.Clip.Origin.Z-20)<.001,"default section passes through the middle of a new cube");
Check(cmd.Results.Any(r=>r.Label=="Bodies cut"&&r.Value=="1")&&cmd.Results.Any(r=>r.Label=="Section area"&&r.Value.Contains("1,600")),"section reports a real 40 by 40 cross section");
var mode=cmd.Params.OfType<ChoiceParam>().Single(p=>p.Label=="Section plane");mode.Index=3;
var slot=cmd.Slots.Single(x=>x.Label.StartsWith("Custom"));Check(slot.IsVisible&&s.Clip==null,"custom-plane mode requests a plane and clears the old clip");
Console.WriteLine($"active={cmd.ActiveSlot?.Label}, matches={s.Matches(new SelItem(Guid.Empty,SelKind.Origin,1),slot.Filter)}, pick={cmd.Pick(new SelItem(Guid.Empty,SelKind.Origin,1),false)}");Check(cmd.ActiveSlot==slot&&slot.Items.Count==1,"switching to custom activates plane picking");Check(s.Clip!=null&&Math.Abs(Math.Abs(s.Clip.Normal.Y)-1)<1e-8,"origin XZ plane controls the clipping normal");
cmd.Params.OfType<NumberParam>().Single(p=>p.Label=="Offset").Value=7;Check(Math.Abs(s.Clip!.Origin.Y+7)<.001,"custom offset follows the plane normal");
cmd.Params.OfType<BoolParam>().Single(p=>p.Label=="Flip side").Value=true;Check(s.Clip!.Normal.Y>0,"flip reverses the custom clipping direction");
slot.Items.Clear();slot.Items.Add(new(cube.Id,SelKind.Face,0));Check(s.Clip!=null&&cmd.Error==null,"a planar model face can be chosen as a custom plane");
var tilted=new Plane3(new(0,0,20),Vec3.UnitX,new Vec3(0,1,1).Normalized());var reference=s.AddObject(Occ.Line(tilted.Origin,tilted.Origin+tilted.U),"Tilted plane",ObjKind.Plane,plane:tilted);slot.Items.Clear();slot.Items.Add(SelItem.Of(reference));Check(s.Clip!=null&&Math.Abs(Vec3.Dot(s.Clip.Normal,tilted.Normal)+1)<1e-8,"an arbitrary tilted construction plane controls the clipping orientation");
s.CancelCommand();Check(s.Clip==null,"cancel clears the section view");
var empty=new CadSession();empty.StartCommand(()=>new SectionCommand());Check(empty.ActiveCommand!.Results.Any(r=>r.Label=="Section"),"an empty scene reports no cut");
Console.WriteLine($"SUCCESS: {checks} checks");
static class Native{[DllImport("kernel32",CharSet=CharSet.Unicode)]public static extern bool SetDllDirectory(string path);}
