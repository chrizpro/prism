using System.Linq;
using Prism.Core;
namespace Prism.Commands;
public sealed class SelectStudyCommand : Command
{
    ChoiceParam _study=null!;
    public override string Title=>"Select Static Study";
    public override string Icon=>"sim-new";
    public override string Hint=>"Choose a saved static setup. Results are recalculated after reopening a document.";
    protected override void OnBegin(){_study=Choice("Static study",0,S.Studies.Select(st=>st.Name).DefaultIfEmpty("No static studies").ToArray());}
    protected override CommandOutput? Build(){if(S.Studies.Count==0)return null;var study=S.Studies[_study.Index];return new(){NoOp=true,After=s=>{s.ActiveStudy=study;s.Dirty=true;s.ShowResult(null);}};}
}
