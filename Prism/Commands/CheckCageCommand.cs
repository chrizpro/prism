using Prism.Core;
using Prism.Core.Form;

namespace Prism.Commands;

public sealed class CheckCageCommand : AnalysisCommand
{
    readonly SelectionSlot _forms;
    public CheckCageCommand() => _forms = Slot(new SelectionSlot { Label = "Freeform bodies", Filter = SelFilter.Form, Multi = true });
    public override string Title => "Check Cage";
    public override string Icon => "check";
    public override string Hint => "Check cage topology before conversion. Red edges mark winding or manifold problems. Open borders are allowed; extraordinary vertices need care when converting. This does not test geometric self-intersection.";
    protected override void Analyze()
    {
        foreach (var obj in ObjectsOf(_forms))
        {
            if (obj.Form is not { } mesh) continue;
            var r = CageReport.Inspect(mesh);
            Row(obj.Name, r.Valid ? "Topology checks passed" : "Repair cage topology", r.Valid ? 0 : 2);
            Row("Invalid / degenerate faces", $"{r.InvalidFaces} / {r.DegenerateFaces}");
            Row("Non-finite / isolated points", $"{r.NonFiniteVertices} / {r.IsolatedVertices}");
            Row("Open boundary edges", r.BoundaryEdges.ToString());
            Row("Non-manifold edges / vertices", $"{r.NonManifoldEdges} / {r.DisconnectedVertexFans}");
            Row("Winding conflicts", r.WindingConflicts.ToString());
            Row("Connected components", r.Components.ToString());
            Row("Extraordinary interior points", r.ExtraordinaryVertices.ToString());
        }
    }
    public override void BuildOverlay(CadSession s, Overlay overlay)
    {
        foreach (var obj in ObjectsOf(_forms)) if (obj.Form is { } mesh)
            foreach (long edge in CageReport.Inspect(mesh).ProblemEdges)
            { var (a, b) = SubdMesh.Unkey(edge); overlay.AddLine(mesh.V[a], mesh.V[b], 0xFFE5484D, 3); }
    }
}
