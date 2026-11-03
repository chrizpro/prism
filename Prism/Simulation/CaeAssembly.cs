using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Prism.Core;
using Prism.Kernel;
using Prism.Meshing;

namespace Prism.Simulation;

/// <summary>One body of a multi-body mesh: where its nodes, elements and boundary triangles sit in the merged mesh.</summary>
public sealed record MeshPart(SceneObject Body, int NodeStart, int NodeCount, int TetStart, int TetCount, int BoundaryStart, int BoundaryCount, int FaceOffset);

/// <summary>Two touching bodies, bonded: the boundary triangles of each that lie on the other.</summary>
public sealed record BondedFaces(int First, int Second, int[] FirstTriangles, int[] SecondTriangles);

/// <summary>Several bodies of different materials as one study. Each body is meshed on its own, and touching faces are bonded by the solver, so the meshes need not match.</summary>
public static class CaeAssembly
{
    /// <summary>The bodies meshed and merged. Touching faces are found here; bodies that overlap or do not reach the others are refused.</summary>
    public static StudyRunner.Prepared Mesh(IReadOnlyList<SceneObject> bodies, double elementSize)
    {
        double maxh = elementSize > 0 ? elementSize : bodies.Min(StudyRunner.AutoSize);
        for (int a = 0; a < bodies.Count; a++)
            for (int b = a + 1; b < bodies.Count; b++)
            {
                double smaller = Math.Min(bodies[a].Shape.Props().Volume, bodies[b].Shape.Props().Volume), common = 0;
                try { common = Occ.Common(bodies[a].Shape, bodies[b].Shape).Props().Volume; } catch (KernelException) { }
                if (common > smaller * 1e-4) throw new InvalidOperationException($"{bodies[a].Name} and {bodies[b].Name} overlap. Bodies in a study must touch, not intersect.");
            }
        var meshes = bodies.Select(b => StudyRunner.Mesh(b, maxh)).ToList();
        try
        {
            var parts = new List<MeshPart>(); int nodes = 0, tets = 0, tris = 0, faces = 0;
            for (int i = 0; i < meshes.Count; i++)
            {
                var m = meshes[i].Mesh;
                parts.Add(new MeshPart(bodies[i], nodes, m.NodeCount, tets, m.TetCount, tris, m.BoundaryCount, faces));
                nodes += m.NodeCount; tets += m.TetCount; tris += m.BoundaryCount; faces += bodies[i].Shape.Count(SubKind.Face);
            }
            var bonds = FindBonds(meshes, parts, maxh);
            var reached = new HashSet<int> { 0 };
            for (bool grew = true; grew;)
            {
                grew = false;
                foreach (var b in bonds) if (reached.Contains(b.First) != reached.Contains(b.Second)) { reached.Add(b.First); reached.Add(b.Second); grew = true; }
            }
            if (reached.Count < bodies.Count)
                throw new InvalidOperationException($"{bodies[Enumerable.Range(0, bodies.Count).First(i => !reached.Contains(i))].Name} does not touch the other bodies, so nothing joins it to them.");
            var merged = FemMesh.Merge(meshes.Select(p => p.Mesh).ToList());
            var face = meshes.SelectMany((p, i) => p.FaceOfBoundary.Select(f => f < 0 ? -1 : f + parts[i].FaceOffset)).ToArray();
            return new StudyRunner.Prepared(merged, face, meshes.SelectMany(p => p.BoundaryNormal).ToArray(), meshes.SelectMany(p => p.BoundaryArea).ToArray(), bodies[0], maxh) { Parts = parts, Bonds = bonds };
        }
        finally { foreach (var m in meshes) m.Mesh.Dispose(); }
    }

    static List<BondedFaces> FindBonds(List<StudyRunner.Prepared> meshes, List<MeshPart> parts, double maxh)
    {
        var bonds = new List<BondedFaces>();
        double reach = Math.Max(maxh * 0.02, 1e-3);
        for (int a = 0; a < meshes.Count; a++)
            for (int b = a + 1; b < meshes.Count; b++)
            {
                int[] On(int from, int to)
                {
                    var src = meshes[from]; var bvh = meshes[to].Surface!.Bvh; var res = new List<int>();
                    for (int t = 0; t < src.Mesh.BoundaryCount; t++)
                    {
                        Vec3 P(int k) { int n = src.Mesh.Boundary[6 * t + k]; return new(src.Mesh.Nodes[3 * n], src.Mesh.Nodes[3 * n + 1], src.Mesh.Nodes[3 * n + 2]); }
                        var centre = (P(0) + P(1) + P(2)) / 3;
                        if (bvh.ClosestPoint(centre) is { } hit && (hit.Point - centre).Length < reach && Vec3.Dot(meshes[to].Surface!.FaceNormal(hit.Tri), src.BoundaryNormal[t]) < -0.5) res.Add(t + parts[from].BoundaryStart);
                    }
                    return [.. res];
                }
                var first = On(a, b); var second = On(b, a);
                if (first.Length > 0 && second.Length > 0) bonds.Add(new BondedFaces(a, b, first, second));
            }
        return bonds;
    }

    /// <summary>The element-face number CalculiX uses for each corner triple of a 10-node tetrahedron.</summary>
    static readonly int[][] TetFaces = [[0, 1, 2], [0, 3, 1], [1, 3, 2], [2, 3, 0]];

    /// <summary>The element and CalculiX face number of every boundary triangle on the given faces, with its area (mm²).</summary>
    public static List<(int elem, int face, double area)> ElementFaces(StudyRunner.Prepared p, IEnumerable<int> faces)
    {
        var m = p.Mesh; var wanted = faces.ToHashSet(); var corner = new Dictionary<(int, int, int), (int tet, int face)>();
        static (int, int, int) Key(int x, int y, int z) { var s = new[] { x, y, z }; Array.Sort(s); return (s[0], s[1], s[2]); }
        for (int e = 0; e < m.TetCount; e++)
            for (int f = 0; f < 4; f++) corner[Key(m.Tets[10 * e + TetFaces[f][0]], m.Tets[10 * e + TetFaces[f][1]], m.Tets[10 * e + TetFaces[f][2]])] = (e, f);
        var res = new List<(int, int, double)>();
        for (int t = 0; t < m.BoundaryCount; t++)
            if (wanted.Contains(p.FaceOfBoundary[t]) && corner.TryGetValue(Key(m.Boundary[6 * t], m.Boundary[6 * t + 1], m.Boundary[6 * t + 2]), out var hit)) res.Add((hit.tet, hit.face, p.BoundaryArea[t]));
        return res;
    }

    /// <summary>The surfaces and tie for each bonded pair. The first body's nodes follow the second body's faces. Nodes with a prescribed value stay out, since the solver cannot both fix and tie one.</summary>
    public static void WriteBonds(StringBuilder b, StudyRunner.Prepared p, ISet<int> prescribed)
    {
        var m = p.Mesh; int index = 0;
        foreach (var bond in p.Bonds)
        {
            index++;
            var slaves = bond.FirstTriangles.SelectMany(t => Enumerable.Range(0, 6).Select(k => m.Boundary[6 * t + k])).Distinct().Where(n => !prescribed.Contains(n)).Order().ToList();
            if (slaves.Count == 0) continue;
            b.AppendLine($"*SURFACE,NAME=SIDEA{index},TYPE=NODE");
            foreach (int n in slaves) b.AppendLine((n + 1).ToString(CultureInfo.InvariantCulture));
            var part = p.Parts![bond.Second];
            var corner = new Dictionary<(int, int, int), (int tet, int face)>();
            (int, int, int) Key(int x, int y, int z) { var s = new[] { x, y, z }; Array.Sort(s); return (s[0], s[1], s[2]); }
            for (int e = part.TetStart; e < part.TetStart + part.TetCount; e++)
                for (int f = 0; f < 4; f++) corner[Key(m.Tets[10 * e + TetFaces[f][0]], m.Tets[10 * e + TetFaces[f][1]], m.Tets[10 * e + TetFaces[f][2]])] = (e, f);
            b.AppendLine($"*SURFACE,NAME=SIDEB{index},TYPE=ELEMENT");
            foreach (int t in bond.SecondTriangles)
                if (corner.TryGetValue(Key(m.Boundary[6 * t], m.Boundary[6 * t + 1], m.Boundary[6 * t + 2]), out var hit)) b.AppendLine($"{hit.tet + 1},S{hit.face + 1}");
            b.AppendLine($"*TIE,NAME=BOND{index},POSITION TOLERANCE={(p.Maxh * 0.05).ToString("G6", CultureInfo.InvariantCulture)}\nSIDEA{index},SIDEB{index}");
        }
    }

    /// <summary>Stress at the nodes (six components), read from a CalculiX .frd result file.</summary>
    public static double[] ReadStress(string frd, int nodes)
    {
        var res = new double[nodes * 6]; bool inside = false;
        foreach (var line in File.ReadLines(frd))
        {
            if (line.StartsWith(" -4  STRESS", StringComparison.Ordinal)) { inside = true; continue; }
            if (!inside) continue;
            if (line.StartsWith(" -3", StringComparison.Ordinal)) break;
            if (!line.StartsWith(" -1", StringComparison.Ordinal)) continue;
            int n = int.Parse(line.AsSpan(3, 10), NumberStyles.Integer, CultureInfo.InvariantCulture) - 1;
            for (int c = 0; c < 6; c++) res[6 * n + c] = double.Parse(line.AsSpan(13 + 12 * c, 12), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        return res;
    }

    /// <summary>A static study of several bodies, solved with CalculiX because each body has its own material.</summary>
    public static FemMesh.StaticResult SolveStatic(StudyRunner.Prepared p, StudyRunner.Boundary bc, string folder, CancellationToken token = default)
    {
        static string F(double x) => x.ToString("G12", CultureInfo.InvariantCulture);
        var m = p.Mesh; var parts = p.Parts!;
        if (m.NodeCount > 100000 || m.TetCount > 200000) throw new InvalidOperationException("Use a coarser mesh for this study (limit: 100,000 nodes / 200,000 elements).");
        var b = new StringBuilder();
        CaeSolver.WriteBodies(b, p, false);
        WriteBonds(b, p, bc.FixNode.ToHashSet());
        b.AppendLine("*NSET,NSET=SUPPORT"); foreach (int n in bc.FixNode) b.AppendLine((n + 1).ToString(CultureInfo.InvariantCulture));
        b.AppendLine("*BOUNDARY");
        for (int i = 0; i < bc.FixNode.Length; i++) for (int d = 0; d < 3; d++) if ((bc.FixMask[i] >> d & 1) != 0) b.AppendLine($"{bc.FixNode[i] + 1},{d + 1},{d + 1}");
        b.AppendLine("*STEP\n*STATIC");
        // a uniform traction on a 6-node triangle loads only its mid-edge nodes, a third of the force on each
        var load = new Dictionary<int, Vec3>(); var net = Vec3.Zero;
        for (int i = 0; i < bc.TracTri.Length; i++)
        {
            int t = bc.TracTri[i]; var force = new Vec3(bc.TracVec[3 * i], bc.TracVec[3 * i + 1], bc.TracVec[3 * i + 2]) * p.BoundaryArea[t];
            net += force;
            for (int k = 3; k < 6; k++) { int n = m.Boundary[6 * t + k]; load[n] = load.GetValueOrDefault(n) + force / 3; }
        }
        if (load.Count > 0)
        {
            b.AppendLine("*CLOAD");
            foreach (var (n, f) in load.OrderBy(kv => kv.Key)) { b.AppendLine($"{n + 1},1,{F(f.X)}"); b.AppendLine($"{n + 1},2,{F(f.Y)}"); b.AppendLine($"{n + 1},3,{F(f.Z)}"); }
        }
        if (bc.Gravity is { } g)
        {
            var gv = new Vec3(g[0], g[1], g[2]); var dir = gv.Normalized();
            b.AppendLine("*DLOAD");
            for (int i = 0; i < parts.Count; i++)
            {
                b.AppendLine($"BODY{i},GRAV,{F(gv.Length)},{F(dir.X)},{F(dir.Y)},{F(dir.Z)}");
                net += gv * (parts[i].Body.Shape.Props().Volume * parts[i].Body.Material!.Density * 1e-9);
            }
        }
        b.AppendLine("*NODE PRINT,NSET=ALLNODES\nU\n*NODE PRINT,NSET=SUPPORT,TOTALS=ONLY\nRF\n*NODE FILE\nU\n*EL FILE\nS\n*END STEP");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "analysis.inp"), b.ToString());
        double seconds = CaeSolver.Run(folder, token);

        var u = new double[m.NodeCount * 3]; var reaction = Vec3.Zero;
        var lines = File.ReadAllLines(Path.Combine(folder, "analysis.dat"));
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("displacements (vx,vy,vz) for set ALLNODES", StringComparison.OrdinalIgnoreCase))
            {
                int j = i + 1, read = 0;
                for (; j < lines.Length; j++)
                {
                    var row = lines[j].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (row.Length == 0 && read == 0) continue;
                    if (row.Length != 4 || !int.TryParse(row[0], out int node)) break;
                    for (int c = 0; c < 3; c++) u[3 * (node - 1) + c] = double.Parse(row[c + 1], CultureInfo.InvariantCulture);
                    read++;
                }
                if (read != m.NodeCount) throw new InvalidDataException("Incomplete displacements from CalculiX.");
                i = j - 1;
            }
            else if (line.StartsWith("total force (fx,fy,fz) for set SUPPORT", StringComparison.OrdinalIgnoreCase))
            {
                for (int j = i + 1; j < lines.Length; j++)
                {
                    var row = lines[j].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (row.Length == 3) { reaction = new Vec3(double.Parse(row[0], CultureInfo.InvariantCulture), double.Parse(row[1], CultureInfo.InvariantCulture), double.Parse(row[2], CultureInfo.InvariantCulture)); break; }
                }
            }
        }
        var s = ReadStress(Path.Combine(folder, "analysis.frd"), m.NodeCount);
        var vm = new double[m.NodeCount];
        for (int n = 0; n < m.NodeCount; n++)
        {
            double a = s[6 * n], c = s[6 * n + 1], d = s[6 * n + 2], xy = s[6 * n + 3], yz = s[6 * n + 4], zx = s[6 * n + 5];
            vm[n] = Math.Sqrt(0.5 * ((a - c) * (a - c) + (c - d) * (c - d) + (d - a) * (d - a)) + 3 * (xy * xy + yz * yz + zx * zx));
        }
        return new FemMesh.StaticResult(u, s, vm, 0, reaction.Length, reaction, net.Length) { SolveSeconds = seconds };
    }
}
