using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Simulation;

namespace Prism.Core;

/// <summary>Simulation state of the open document.</summary>
public sealed partial class CadSession
{
    public List<StudyDef> Studies { get; } = [];
    public StudyDef? ActiveStudy { get; set; }
    public List<NamedFaceSet> FaceSets { get; } = [];
    public List<SavedCaeCase> CaeCases { get; } = [];
    /// <summary>The result currently painted on the model (replaces the body in the viewport), or null.</summary>
    public ResultView? Result { get; private set; }
    public event Action? StudyChanged;

    public void RaiseStudy() { StudyChanged?.Invoke(); Invalidate(); }

    public void ShowResult(ResultView? v) { Result = v; RaiseStudy(); }

    public StudyDef NewStudy(SceneObject body)
    {
        int n = Studies.Count + 1;
        var st = new StudyDef { Name = $"Static {n}", BodyId = body.Id, ShapeSignature=StudySetup.Signature(body) };
        Studies.Add(st); ActiveStudy = st; Result = null;
        Dirty=true;
        RaiseStudy();
        return st;
    }

    void ResetSimulation() { StopCae(); Cae=null; Studies.Clear(); FaceSets.Clear(); CaeCases.Clear(); CamSetup=null; CamSetups.Clear(); StopCamPlayback(); ActiveStudy = null; Result = null; Motion = null; StudyChanged?.Invoke(); }
}
