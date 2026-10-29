using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;
using Prism.Core;

namespace Prism.Simulation;

public enum ResultKind { VonMises, Displacement, SafetyFactor, DisplacementX, DisplacementY, DisplacementZ, MeshOnly, Scalar }

/// <summary>The study result as a colored surface of the (optionally exaggerated) deformed shape.</summary>
public sealed class ResultView
{
    public static ResultView Field(CaeOutcome outcome, int mode = 0)
    {
        var p=outcome.Prepared; var m=p.Mesh; bool thermal=outcome.Kind!=CaeKind.Modal;
        if(!thermal && (mode<0||mode>=outcome.Modes.Count)) throw new ArgumentOutOfRangeException(nameof(mode));
        double[] u=thermal?[]:outcome.Modes[mode];
        double magnitude(int n)=>Math.Sqrt(u[3*n]*u[3*n]+u[3*n+1]*u[3*n+1]+u[3*n+2]*u[3*n+2]);
        double max=thermal?1:Enumerable.Range(0,m.NodeCount).Max(magnitude);
        double scale=thermal?0:.05*(p.Body.Mesh.Max-p.Body.Mesh.Min).Length/Math.Max(max,1e-30);
        var d=new MeshData(m.NodeCount,m.BoundaryCount*4,0) {FaceCount=1,EdgeCount=0};
        var positions=new Vec3[m.NodeCount]; var values=new float[m.NodeCount]; var normals=new Vec3[m.NodeCount];
        for(int n=0;n<m.NodeCount;n++)
        {
            var point=new Vec3(m.Nodes[3*n],m.Nodes[3*n+1],m.Nodes[3*n+2]);
            if(!thermal) point+=new Vec3(u[3*n],u[3*n+1],u[3*n+2])*scale;
            positions[n]=point; values[n]=(float)(thermal?outcome.Temperatures[n]:magnitude(n)/Math.Max(max,1e-30));
            d.Positions[3*n]=(float)point.X; d.Positions[3*n+1]=(float)point.Y; d.Positions[3*n+2]=(float)point.Z;
        }
        int next=0;
        for(int b=0;b<m.BoundaryCount;b++)
        {
            int a=m.Boundary[6*b], c=m.Boundary[6*b+1], e=m.Boundary[6*b+2], ac=m.Boundary[6*b+3], ce=m.Boundary[6*b+4], ea=m.Boundary[6*b+5];
            int[][] triangles=[[a,ac,ea],[ac,c,ce],[ea,ce,e],[ac,ce,ea]];
            bool flip=Vec3.Dot(Vec3.Cross(positions[c]-positions[a],positions[e]-positions[a]),p.BoundaryNormal[b])<0;
            foreach(var t in triangles)
            {
                if(flip) (t[1],t[2])=(t[2],t[1]);
                var normal=Vec3.Cross(positions[t[1]]-positions[t[0]],positions[t[2]]-positions[t[0]]);
                foreach(int n in t){d.Indices[next++]=n; normals[n]+=normal;}
            }
        }
        for(int n=0;n<m.NodeCount;n++){var normal=normals[n].Normalized();d.Normals[3*n]=(float)normal.X;d.Normals[3*n+1]=(float)normal.Y;d.Normals[3*n+2]=(float)normal.Z;}
        d.Min=new(positions.Min(p=>p.X),positions.Min(p=>p.Y),positions.Min(p=>p.Z));d.Max=new(positions.Max(p=>p.X),positions.Max(p=>p.Y),positions.Max(p=>p.Z));
        d.Scalars=values;d.TriFace=new int[m.BoundaryCount*4];d.FaceTriStart=[0,m.BoundaryCount*4];d.EdgeSegStart=[0];
        float min=values.Min(), high=values.Max(); if(high-min<1e-6)high=min+1e-6f;
        return new ResultView { Mesh=d,BodySnapshot=p.Body,BodyId=p.Body.Id,Kind=ResultKind.Scalar,Min=min,Max=high,Title=thermal?"Temperature":$"Mode {mode+1}: {outcome.Frequencies[mode]:0.###} Hz",
            Unit=thermal?"°C":"relative magnitude",Values=values,Positions=positions,NodeOfVertex=Enumerable.Range(0,m.NodeCount).ToArray(),Scale=scale };
    }
    public required MeshData Mesh { get; init; }
    public SceneObject? BodySnapshot { get; init; }
    public Guid BodyId { get; init; }
    public ResultKind Kind { get; init; }
    public float Min { get; init; }
    public float Max { get; init; }
    public string Title { get; init; } = "";
    public string Unit { get; init; } = "";
    public double Scale { get; init; }
    public bool ShowEdges { get; set; }
    public StudyResults? Source { get; init; }
    /// <summary>Per display vertex: the FEM node it came from and its value (for the probe).</summary>
    public int[] NodeOfVertex { get; init; } = [];
    public float[] Values { get; init; } = [];
    public Vec3[] Positions { get; init; } = [];

    /// <summary>Scale that shows the biggest movement as about 5% of the model size (never below 1x, i.e. never shrinking the truth).</summary>
    public static double AutoScale(StudyResults r, double modelSize) => r.MaxDisplacement <= 1e-12 ? 1 : Math.Clamp(0.05 * modelSize / r.MaxDisplacement, 1, 5000);

    public static ResultView Build(StudyResults r, Guid body, ResultKind kind, double scale, double modelSize)
    {
        if(kind==ResultKind.SafetyFactor && !double.IsFinite(r.Yield))throw new InvalidOperationException("A yield-based factor of safety is unavailable for this material.");
        var m = r.Mesh; var sol = r.Solution;
        var map = new Dictionary<int, int>(); var nodes = new List<int>();
        int V(int node) { if (!map.TryGetValue(node, out int id)) { id = nodes.Count; map[node] = id; nodes.Add(node); } return id; }
        var tris = new List<int>();
        for (int b = 0; b < m.BoundaryCount; b++)
        {
            int[] n = new int[6]; for (int k = 0; k < 6; k++) n[k] = V(m.Boundary[6 * b + k]);
            // four sub-triangles of the quadratic triangle: corners a,b,c and mid-edge nodes ab,bc,ca
            int[][] sub = [[n[0], n[3], n[5]], [n[3], n[1], n[4]], [n[5], n[4], n[2]], [n[3], n[4], n[5]]];
            // keep the winding facing outward
            Vec3 P(int node) => new(m.Nodes[3 * node], m.Nodes[3 * node + 1], m.Nodes[3 * node + 2]);
            var a = P(m.Boundary[6 * b]); var bb = P(m.Boundary[6 * b + 1]); var c = P(m.Boundary[6 * b + 2]);
            bool flip = r.BoundaryNormal.Length > b && Vec3.Dot(Vec3.Cross(bb - a, c - a), r.BoundaryNormal[b]) < 0;
            foreach (var t in sub) { if (flip) tris.AddRange([t[0], t[2], t[1]]); else tris.AddRange(t); }
        }
        int nv = nodes.Count;
        var pos = new Vec3[nv]; var val = new float[nv];
        for (int i = 0; i < nv; i++)
        {
            int node = nodes[i];
            var p = new Vec3(m.Nodes[3 * node], m.Nodes[3 * node + 1], m.Nodes[3 * node + 2]);
            var u = new Vec3(sol.Displacement[3 * node], sol.Displacement[3 * node + 1], sol.Displacement[3 * node + 2]);
            pos[i] = kind == ResultKind.MeshOnly ? p : p + u * scale;
            val[i] = kind switch
            {
                ResultKind.VonMises => (float)sol.VonMises[node],
                ResultKind.Displacement => (float)u.Length,
                ResultKind.DisplacementX => (float)u.X, ResultKind.DisplacementY => (float)u.Y, ResultKind.DisplacementZ => (float)u.Z,
                ResultKind.SafetyFactor => (float)r.SafetyFactor[node],
                _ => 0f,
            };
        }
        var d = new MeshData(nv, tris.Count / 3, 0) { FaceCount = 1, EdgeCount = 0 };
        var nrm = new Vec3[nv];
        for (int t = 0; t < tris.Count; t += 3)
        {
            var c = Vec3.Cross(pos[tris[t + 1]] - pos[tris[t]], pos[tris[t + 2]] - pos[tris[t]]);
            for (int k = 0; k < 3; k++) nrm[tris[t + k]] += c;
        }
        double x0 = double.MaxValue, y0 = x0, z0 = x0, x1 = double.MinValue, y1 = x1, z1 = x1;
        for (int i = 0; i < nv; i++)
        {
            var n = nrm[i].Normalized();
            d.Positions[3 * i] = (float)pos[i].X; d.Positions[3 * i + 1] = (float)pos[i].Y; d.Positions[3 * i + 2] = (float)pos[i].Z;
            d.Normals[3 * i] = (float)n.X; d.Normals[3 * i + 1] = (float)n.Y; d.Normals[3 * i + 2] = (float)n.Z;
            x0 = Math.Min(x0, pos[i].X); x1 = Math.Max(x1, pos[i].X); y0 = Math.Min(y0, pos[i].Y); y1 = Math.Max(y1, pos[i].Y); z0 = Math.Min(z0, pos[i].Z); z1 = Math.Max(z1, pos[i].Z);
        }
        d.Min = new(x0, y0, z0); d.Max = new(x1, y1, z1);
        for (int i = 0; i < tris.Count; i++) d.Indices[i] = tris[i];
        d.TriFace = new int[tris.Count / 3]; d.FaceTriStart = [0, tris.Count / 3]; d.EdgeSegStart = [0];
        d.Scalars = val;

        // the cage of corner edges, for the "show mesh" option
        var segs = new List<float>();
        var seen = new HashSet<long>();
        for (int b = 0; b < m.BoundaryCount; b++)
            for (int k = 0; k < 3; k++)
            {
                int a = m.Boundary[6 * b + k], c = m.Boundary[6 * b + (k + 1) % 3];
                long key = a < c ? ((long)a << 32) | (uint)c : ((long)c << 32) | (uint)a;
                if (!seen.Add(key)) continue;
                var ia = map[a]; var ic = map[c];
                segs.AddRange([(float)pos[ia].X, (float)pos[ia].Y, (float)pos[ia].Z, (float)pos[ic].X, (float)pos[ic].Y, (float)pos[ic].Z]);
            }
        d.EdgePoints = [.. segs]; d.SegEdge = new int[segs.Count / 6]; d.EdgeSegStart = [0, segs.Count / 6]; d.EdgeCount = 1;

        float mn = 0, mx = 1; string title = "", unit = "";
        switch (kind)
        {
            case ResultKind.VonMises: mn = 0; mx = (float)Math.Max(r.MaxVonMises, 1e-6); title = "von Mises stress"; unit = "MPa"; break;
            case ResultKind.Displacement: mn = 0; mx = (float)Math.Max(r.MaxDisplacement, 1e-9); title = "Displacement"; unit = "mm"; break;
            case ResultKind.DisplacementX: case ResultKind.DisplacementY: case ResultKind.DisplacementZ:
                mn = val.Min(); mx = val.Max(); if (mx - mn < 1e-12) mx = mn + 1e-6f; title = "Displacement " + kind.ToString()[^1]; unit = "mm"; break;
            case ResultKind.SafetyFactor: mx = (float)Math.Min(r.MinSafety, 5); mn = 5; title = "Factor of safety"; unit = ""; if (r.MinSafety >= 5) mx = 4.99f; break;
            default: title = "Mesh"; break;
        }
        return new ResultView { Mesh = d, BodySnapshot=r.SourceBody, BodyId = body, Kind = kind, Min = mn, Max = mx, Title = title, Unit = unit, Scale = scale, Source = r, NodeOfVertex = [.. nodes], Values = val, Positions = pos };
    }
}
