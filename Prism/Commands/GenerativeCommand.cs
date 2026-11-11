using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Prism.Core;
using Prism.Kernel;
using Prism.Meshing;
using Prism.Simulation;

namespace Prism.Commands;

/// <summary>
/// Generative design (topology optimization): starting from the body as a block of possible material, with the supports and loads of the active study,
/// it removes the material that carries the least load until only the chosen share is left. The result is the lightest-looking stiff shape: a new mesh
/// object you can smooth, convert to a solid, and check with a normal study.
/// </summary>
public sealed class GenerativeCommand : Command
{
    readonly ChoiceParam _res;
    readonly NumberParam _vol, _feature, _keep;
    readonly IntParam _iters, _smooth;

    CancellationTokenSource? _cts;
    volatile bool _running;
    TriMesh? _final;
    string _status = "";
    readonly List<double> _history = [];
    float[] _previewV = [], _previewT0 = [];
    int[] _previewI = [];
    double _volumeMm3, _masterVolume;
    string? _error;

    readonly SelectionSlot _keepFaces;

    public GenerativeCommand()
    {
        _keepFaces = Slot(new SelectionSlot { Label = "Faces to keep (bolt faces, mounting pads: optional)", Filter = SelFilter.Face, Multi = true, Optional = true });
        _vol = Num("Keep this much of the volume", 30, 5, 90, 5, "%");
        _res = Choice("Detail", 1, "Coarse (fast)", "Medium", "Fine (slow)");
        _feature = Num("Smallest feature (0 = automatic)", 0, 0, 1e4, 1);
        _keep = Num("Keep material around supports and loads (0 = automatic)", 0, 0, 1e4, 1);
        _iters = Add(new IntParam { Label = "Most iterations", Min = 10, Max = 300, Value = 100 });
        _smooth = Add(new IntParam { Label = "Surface smoothing passes", Min = 0, Max = 30, Value = 8 });
        Add(new ActionParam { Label = "Generate", Run = Start });
        Add(new ActionParam { Label = "Stop (keep what exists so far)", Run = () => _cts?.Cancel() });
    }

    public override string Title => "Generative Design";
    public override string Icon => "generative";
    public override string OkText => "Create the shape";
    public override string Hint => "Uses the supports and loads of the study you set up in this workspace (Study, Fixed Support, Force...). Choose how much of the volume to keep, press Generate and watch the material settle into load paths. Stop whenever it looks right, then Create the shape. The result is a mesh: smooth it, convert it to a solid, and run a normal study on it to check the stress.";

    SceneObject Body() => S.ActiveStudy is { OtherBodies.Count: > 0 } ? throw new InvalidOperationException("Generative Design works on a one-body study for now.") : S.Find(S.ActiveStudy?.BodyId ?? Guid.Empty) ?? throw new InvalidOperationException("Start a study first (Simulate > New Study), add a support and a load, then come back.");

    void Start()
    {
        if (_running) return;
        StudyDef st;
        try { st = S.ActiveStudy ?? throw new InvalidOperationException("Start a study first (Simulate > New Study), add a support and a load, then come back."); Body(); }
        catch (Exception e) { S.Say(e.Message, true); return; }
        _cts = new CancellationTokenSource(); var ct = _cts.Token;
        var progress = S.Work.Begin("Generative design", _cts.Cancel);
        progress.Report("Meshing…");
        _running = true; _final = null; _error = null; _history.Clear(); _previewV = []; _previewI = [];
        int maxIt = _iters.Value; double volFrac = _vol.Value / 100; int smooth = _smooth.Value;
        double divisions = _res.Index switch { 0 => 22, 2 => 46, _ => 32 };
        double featureUser = _feature.Value, keepUser = _keep.Value;
        var keepFaces = _keepFaces.Items.Where(i => i.Kind == SelKind.Face && i.Obj == st.BodyId).Select(i => i.Index).ToArray();
        Refresh("Meshing…");
        Task.Run(() =>
        {
            try
            {
                var body = Body();
                double diag = body.Mesh.Max.DistanceTo(body.Mesh.Min);
                double h = Math.Max(diag / divisions, 0.05);
                double saved = st.ElementSize; StudyRunner.Prepared prep;
                try { st.ElementSize = h; prep = StudyRunner.Mesh(S, st); } finally { st.ElementSize = saved; }
                var notes = new List<string>(); var bc = StudyRunner.Conditions(prep, st, notes);
                // faces to keep: the optimizer protects material around loaded faces, so register them as loads with a negligible force
                var tracTri = bc.TracTri; var tracVec = bc.TracVec;
                if (keepFaces.Length > 0)
                {
                    var extra = StudyRunner.BoundaryTrianglesOn(prep, keepFaces).Except(tracTri).ToArray();
                    if (extra.Length > 0)
                    {
                        var all = tracTri.Concat(extra).ToArray(); var vec = new double[all.Length * 3];
                        Array.Copy(tracVec, vec, tracVec.Length);
                        for (int k = tracTri.Length; k < all.Length; k++) { var nrm = prep.BoundaryNormal[all[k]] * -1e-6; vec[3 * k] = nrm.X; vec[3 * k + 1] = nrm.Y; vec[3 * k + 2] = nrm.Z; }
                        var order = Enumerable.Range(0, all.Length).OrderBy(k => all[k]).ToArray();
                        tracTri = order.Select(k => all[k]).ToArray();
                        tracVec = order.SelectMany(k => new[] { vec[3 * k], vec[3 * k + 1], vec[3 * k + 2] }).ToArray();
                    }
                }
                var mat = body.Material ?? throw new InvalidOperationException("Assign a material before running generative analysis.");
                MaterialRules.Structural(mat);
                double rmin = featureUser > 0 ? featureUser : h * 2.2, keep = keepUser > 0 ? keepUser : h * 1.6;
                using var run = prep.Mesh.StartTopology(mat.Youngs * 1000.0, mat.Poisson, mat.Density * 1e-9, bc.FixNode, bc.FixMask, tracTri, tracVec, bc.Gravity, volFrac, 3.0, rmin, keep);
                double total = run.Volume.Sum();
                for (int it = 0; it < maxIt && !ct.IsCancellationRequested; it++)
                {
                    run.Step();
                    _history.Add(run.Compliance);
                    if (it % 4 == 3 || it == 0)
                    {
                        var iso = IsoSurface.Extract(prep.Mesh, run.Density, run.Volume, prep.BoundaryNormal);
                        _previewV = iso.V; _previewI = iso.T;
                    }
                    progress.Report($"Iteration {run.Iteration}/{maxIt}", (double)run.Iteration / maxIt);
                    Refresh($"Iteration {run.Iteration}: stiffness cost {run.Compliance:0.###} N·mm, material {run.VolumeFraction * 100:0.#} %, change {run.Change:0.000}  ({run.StepSeconds:0.0} s per step)");
                    if (run.Iteration >= 50 && run.Change < 0.004) break;
                }
                // the final shape
                // cutting the density field at a fixed level loses the grey (half-removed) material and smoothing shrinks thin members, so the skin comes out
                // much lighter than the volume asked for. Find the density level whose smoothed shape weighs what the optimizer says it should.
                double target = run.Volume.Zip(run.Density, (v, d) => v * d).Sum();
                progress.Report("Building the final surface…");
                TriMesh Shape(double level)
                {
                    var m = IsoSurface.Extract(prep.Mesh, run.Density, run.Volume, prep.BoundaryNormal, level);
                    m = MeshOps.Weld(m, 1e-5);
                    m = MeshOps.Clean(m).mesh;
                    m = MeshOps.RemoveSmallShells(m, 24, 0.002).mesh;
                    if (smooth > 0) m = MeshOps.Smooth(m, smooth, keepBoundary: false);
                    return m;
                }
                double loLevel = 0.05, hiLevel = 0.5; var tm = Shape(hiLevel); double best = double.MaxValue;
                for (int k = 0; k < 7 && !ct.IsCancellationRequested; k++)
                {
                    double level = k == 0 ? hiLevel : (loLevel + hiLevel) / 2;
                    var cand = k == 0 ? tm : Shape(level);
                    var cr = MeshAnalysis.Report(cand);
                    double vol = cr.Closed ? cr.Volume : 0, err = Math.Abs(vol - target);
                    if (vol > 0 && err < best) { best = err; tm = cand; }
                    if (vol > target) loLevel = level; else hiLevel = level;       // too heavy: raise the level; too light: lower it
                    if (vol > 0 && err < target * 0.03) break;
                }
                var rep = MeshAnalysis.Report(tm);
                _volumeMm3 = rep.Volume; _masterVolume = body.Shape.Props().Volume;
                // the shape must stay inside the design space it was cut from
                Vec3 lo = body.Mesh.Min, hi = body.Mesh.Max;
                tm = tm.Transformed(p => new Vec3(Math.Clamp(p.X, lo.X, hi.X), Math.Clamp(p.Y, lo.Y, hi.Y), Math.Clamp(p.Z, lo.Z, hi.Z)));
                _final = tm; _previewV = []; _previewI = [];
                Refresh($"Done after {run.Iteration} iterations: {rep.Triangles:#,0} triangles, {(rep.Closed ? "closed surface" : "surface has gaps (try more smoothing or a finer detail)")}.");
            }
            catch (Exception e) { _error = e.Message; Refresh("Stopped: " + e.Message); }
            finally { progress.Dispose(); _running = false; Refresh(null); }
        });
    }

    void Refresh(string? status)
    {
        if (status != null) _status = status;
        Dispatcher.UIThread.Post(() => { try { OnChanged(); S.Invalidate(); } catch { } });
    }

    protected override CommandOutput? Build()
    {
        Results.Clear();
        if (_status.Length > 0) Results.Add(new ResultLine("Status", _status, _error != null ? 2 : 0));
        if (_history.Count > 1)
        {
            double first = _history[0], last = _history[^1];
            Results.Add(new ResultLine("Iterations", _history.Count.ToString()));
            Results.Add(new ResultLine("Stiffness cost", $"{last:0.###} N·mm (lower is stiffer)", 1));
        }
        if (_final == null) return null;
        var rep = MeshAnalysis.Report(_final);
        Results.Add(new ResultLine("Result volume", $"{rep.Volume:#,0} mm³  ({(_masterVolume > 0 ? rep.Volume / _masterVolume * 100 : 0):0.#} % of the block)"));
        var body = S.ActiveStudy != null ? S.Find(S.ActiveStudy.BodyId) : null;
        if (body?.Material is { } mat) Results.Add(new ResultLine("Mass", $"{rep.Volume * mat.Density / 1000.0:#,0.##} g in {mat.Name}", 1));
        var o = new CommandOutput { Label = "Generative design", Icon = "generative" };
        o.Add.Add(new(null, "Generative design", ObjKind.Mesh, Tri: _final));
        return o;
    }

    public override void BuildOverlay(CadSession s, Overlay o)
    {
        // the material as it settles
        var v = _previewV; var t = _previewI;
        if (v.Length == 0 || t.Length == 0) return;
        int step = Math.Max(1, t.Length / 3 / 12000);
        for (int i = 0; i + 2 < t.Length; i += 3 * step)
        {
            Vec3 P(int k) => new(v[3 * t[i + k]], v[3 * t[i + k] + 1], v[3 * t[i + k] + 2]);
            o.AddTri(P(0), P(1), P(2), 0xAA4C94FF);
        }
    }

    public override void OnCancel() { _cts?.Cancel(); }
    public override void OnEnd() { _cts?.Cancel(); }
}
