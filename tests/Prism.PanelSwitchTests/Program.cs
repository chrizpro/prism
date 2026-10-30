using System.Runtime.InteropServices;
using Prism.Commands;
using Prism.Core;
using Prism.Core.Form;
using Prism.Kernel;

Native.Initialize();int checks=0;
void Check(bool valid,string message){if(!valid)throw new Exception(message);Console.WriteLine("PASS "+message);checks++;}
var session=new CadSession{ProtectPendingCommand=true};
session.StartCommand(()=>new IncompleteCommand());var empty=session.ActiveCommand!;
session.StartCommand(()=>new BoxCommand());Check(session.ActiveCommand is BoxCommand&&session.PendingNotice==null,"an untouched empty panel switches immediately");
var box=(BoxCommand)session.ActiveCommand!;var width=box.Params.OfType<NumberParam>().First();width.Value=71;
session.StartCommand(()=>new BoxCommand());Check(ReferenceEquals(session.ActiveCommand,box)&&width.Value==71,"reselecting the current tool retains its options and preview");
session.StartCommand(()=>new CylinderCommand());Check(session.PendingNotice!=null&&ReferenceEquals(session.ActiveCommand,box),"a different tool waits for unfinished work");
session.Key("Escape");Check(session.PendingNotice==null&&ReferenceEquals(session.ActiveCommand,box)&&width.Value==71,"Escape dismisses the switch request and keeps current work");
session.StartCommand(()=>new CylinderCommand());session.ResolvePending(true);
Check(session.ActiveCommand is CylinderCommand&&session.Objects.Count==1,"Finish and switch commits once and opens the requested tool");
session.CancelCommand();session.StartCommand(()=>new IncompleteCommand());var incomplete=(IncompleteCommand)session.ActiveCommand!;incomplete.Amount.Value=5;
Check(session.Preview.Count==0&&incomplete.HasInputChanges,"changed options are tracked even without a valid preview");
session.StartCommand(()=>new BoxCommand());Check(session.PendingNotice!=null&&ReferenceEquals(session.ActiveCommand,incomplete),"incomplete edited options are protected when switching");
session.ResolvePending(true);Check(session.PendingNotice!=null&&ReferenceEquals(session.ActiveCommand,incomplete),"failed validation leaves both the current panel and switch request intact");
session.ResolvePending(false);Check(session.ActiveCommand is BoxCommand&&session.PendingNotice==null,"Discard and switch works after failed validation");

session.CancelCommand();session.StartCommand(()=>new ParentCommand());var parent=(ParentCommand)session.ActiveCommand!;
parent.Open("First");parent.Amount.Value=3;parent.Open("Second");
Check(parent.DisplayTitle=="First"&&session.PendingTitle=="First"&&session.PendingNotice!.Contains("Parent will stay open"),"a sibling options request protects only the active temporary panel");
session.ResolvePending(true);Check(ReferenceEquals(session.ActiveCommand,parent)&&parent.DisplayTitle=="Second"&&parent.Applied==3,"applying a temporary panel opens its sibling while retaining the parent editor");
parent.Amount.Value=9;parent.Open("Third");session.ResolvePending(false);
Check(parent.DisplayTitle=="Third"&&parent.Amount.Value==3&&parent.Applied==3,"discarding a temporary panel preserves earlier parent work");
session.CancelCommandPanel();Check(!parent.HasPanelStep&&ReferenceEquals(session.ActiveCommand,parent),"canceling the temporary page returns to the parent");
parent.Open("Third");session.StartCommand(()=>new BoxCommand());
Check(session.PendingTitle=="Parent"&&session.PendingNotice!.Contains("whole Parent session")&&parent.DisplayTitle=="Third","a top-level switch explicitly includes the parent editor and temporary page");
session.KeepPending();Check(parent.HasPanelStep&&ReferenceEquals(session.ActiveCommand,parent),"Keep editing retains the nested options state");
session.StartCommand(()=>new CylinderCommand());session.ResolvePending(true);
Check(session.ActiveCommand is CylinderCommand&&parent.Accepted,"finishing a top-level switch accepts the temporary page and finishes its parent");

session.CancelCommand();var formObject=session.AddObject(null,"Form",ObjKind.Form,form:FormOps.Box(30,30,30,2,2,2));
var original=formObject.Form!;session.SelectItems([SelItem.Of(formObject)]);session.StartCommand(()=>new EditFormCommand());var edit=(EditFormCommand)session.ActiveCommand!;
edit.Work!.V[0]+=new Vec3(1,0,0);edit.SetSelectionMode(FormMode.Edge);var edge=edit.Work.Edges().First();long key=SubdMesh.Key(edge.a,edge.b);edit.Edges.Add(key);edit.OnChanged();edit.OpenCrease();
edit.Params.OfType<NumberParam>().Single(p=>p.Label=="Crease sharpness").Value=.7;
session.StartCommand(()=>new BoxCommand());session.ResolvePending(false);
Check(session.ActiveCommand is BoxCommand&&ReferenceEquals(session.Find(formObject.Id)!.Form,original),"Discard and switch discards the entire uncommitted Edit Form session, including Crease");
session.CancelCommand();session.SelectItems([SelItem.Of(formObject)]);session.StartCommand(()=>new EditFormCommand());edit=(EditFormCommand)session.ActiveCommand!;
edit.Work!.V[0]+=new Vec3(1,0,0);edit.SetSelectionMode(FormMode.Edge);edit.Edges.Add(key);edit.OnChanged();edit.OpenCrease();edit.Params.OfType<NumberParam>().Single(p=>p.Label=="Crease sharpness").Value=.8;
session.StartCommand(()=>new BoxCommand());session.ResolvePending(true);var saved=session.Find(formObject.Id)!.Form!;
Check(session.ActiveCommand is BoxCommand&&saved.V[0]==original.V[0]+new Vec3(1,0,0)&&Math.Abs(saved.Crease[key]-.8)<1e-9,"Finish and switch keeps both cage edits and the current Crease preview");

session.CancelCommand();var targetNode=session.Current;session.StartCommand(()=>new BoxCommand());box=(BoxCommand)session.ActiveCommand!;
session.EditFeature(session.HistoryPath().Past.First(n=>n.Recipe?.Title=="Box"));
Check(ReferenceEquals(session.ActiveCommand,box)&&session.PendingNotice!=null,"opening a historical feature uses the same switch protection");
session.KeepPending();session.CancelCommand();session.StartCommand(()=>new BoxCommand());session.StartCommand(()=>new CylinderCommand());session.CancelCommandPanel();
Check(session.ActiveCommand==null&&session.PendingNotice==null,"explicit panel cancellation clears the queued switch instead of launching it later");
Console.WriteLine($"SUCCESS: {checks} checks");

sealed class IncompleteCommand:Command
{
    public NumberParam Amount{get;}
    public IncompleteCommand(){Amount=Num("Amount",0);}
    public override string Title=>"Incomplete";
    protected override CommandOutput? Build()=>null;
}
sealed class ParentCommand:Command
{
    public NumberParam Amount{get;}
    public double Applied{get;private set;}
    public bool Accepted{get;private set;}
    public ParentCommand(){Amount=Num("Amount",0);}
    public override string Title=>"Parent";
    public void Open(string title)
    {
        if(DeferPanelStep(title,()=>Open(title)))return;
        double initial=Applied;
        OpenPanelStep(title,"Temporary options",[Amount],()=>Applied=Amount.Value,()=>Amount.Value=initial);
    }
    protected override CommandOutput? Build()=>new(){NoOp=true,After=_=>Accepted=true};
}
static class Native{[DllImport("kernel32",CharSet=CharSet.Unicode)]static extern bool SetDllDirectory(string path);public static void Initialize()=>SetDllDirectory(Path.GetFullPath("native/bin"));}
