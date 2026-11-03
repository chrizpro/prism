using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Prism.Simulation;

namespace Prism.Core;
public sealed class CaeJob
{
    public bool Running { get; set; } = true;
    public string Status { get; set; } = "Preparing…";
    public string? Error { get; set; }
    public CaeOutcome? Outcome { get; set; }
    public CancellationTokenSource Cancellation { get; } = new();
}
public sealed partial class CadSession
{
    public CaeJob? Cae { get; private set; }
    public event Action? CaeChanged;
    public void StopCae() { if(Cae is {Running:true} job){job.Cancellation.Cancel();job.Status="Stopping…";CaeChanged?.Invoke();} }
    public void StartCae(SceneObject body,CaeSettings settings)
    {
        if(Cae is {Running:true}) throw new InvalidOperationException("Stop the current CAE run before starting another.");
        Cae?.Outcome?.Prepared.Mesh.Dispose();
        ShowResult(null);
        var others=(settings.OtherBodies??[]).Select(id=>Find(id)??throw new InvalidOperationException("A body in this run was deleted.")).ToList();
        var job=new CaeJob();Cae=job; CaeChanged?.Invoke();
        string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prism","CAE",DateTime.Now.ToString("yyyyMMdd_HHmmss")+"_"+Guid.NewGuid().ToString("N")[..6]);
        _=Run();
        async Task Run()
        {
            try
            {
                var outcome=await Task.Run(()=>CaeSolver.Solve(body,settings,folder,job.Cancellation.Token,
                    status=>Dispatcher.UIThread.Post(()=>{job.Status=status;if(ReferenceEquals(Cae,job))CaeChanged?.Invoke();}),others));
                job.Cancellation.Token.ThrowIfCancellationRequested();
                job.Outcome=outcome;job.Status="Complete — results in "+folder;
                if(ReferenceEquals(Cae,job) && ReferenceEquals(Find(body.Id),body) && others.All(o=>ReferenceEquals(Find(o.Id),o))) ShowResult(ResultView.Field(outcome));
                else job.Status="Complete; source body changed, so results were not applied. Files: "+folder;
            }
            catch(OperationCanceledException){job.Status="Canceled";}
            catch(Exception ex){job.Error=ex.Message;job.Status="Failed: "+ex.Message;}
            finally{job.Running=false;if(ReferenceEquals(Cae,job)){CaeChanged?.Invoke();RaiseStudy();}}
        }
    }
}
