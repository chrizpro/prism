using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Prism.Kernel;

namespace Prism.Core.Cam;

/// <summary>A cutter from the tool library: its size and sensible speeds for a material.</summary>
public sealed record CamTool(string Name, double Diameter, bool Ball, double Rpm, double Feed, double Plunge, double StepDown);

/// <summary>Starting points for common cutters. They are conservative: tune them for the machine and the material.</summary>
public static class CamToolLibrary
{
    static readonly CamTool[] Defaults =
    [
        new("3 mm flat end mill, aluminium", 3, false, 18000, 600, 200, 1),
        new("6 mm flat end mill, aluminium", 6, false, 12000, 900, 300, 2),
        new("10 mm flat end mill, aluminium", 10, false, 9000, 1000, 350, 3),
        new("6 mm flat end mill, wood / MDF", 6, false, 16000, 1800, 600, 3),
        new("6 mm flat end mill, plastic", 6, false, 14000, 1200, 400, 2),
        new("6 mm flat end mill, mild steel", 6, false, 5000, 250, 100, 0.5),
        new("3 mm ball nose, aluminium", 3, true, 18000, 700, 250, 0.5),
        new("6 mm ball nose, wood / MDF", 6, true, 16000, 1500, 500, 1),
    ];
    static string LibraryFile=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prism","cam-tools.json");
    public static CamTool[] Tools {get;private set;}=Load();
    static CamTool[] Load()
    {
        try{var tools=JsonSerializer.Deserialize<CamTool[]>(File.ReadAllText(LibraryFile));return tools is{Length:>0}&&tools.All(Valid)?tools:Defaults;}catch{return Defaults;}
    }
    static bool Valid(CamTool t)=>!string.IsNullOrWhiteSpace(t.Name)&&new[]{t.Diameter,t.Rpm,t.Feed,t.Plunge,t.StepDown}.All(v=>double.IsFinite(v)&&v>0);
    public static void SaveTool(CamTool tool)
    {
        if(!Valid(tool))throw new InvalidOperationException("Name the tool and use positive finite sizes, feeds and spindle speed.");
        var updated=Tools.Where(t=>!t.Name.Equals(tool.Name,StringComparison.OrdinalIgnoreCase)).Append(tool).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(LibraryFile)!);string temp=LibraryFile+".tmp";
        try{File.WriteAllText(temp,JsonSerializer.Serialize(updated,new JsonSerializerOptions{WriteIndented=true}));File.Move(temp,LibraryFile,true);Tools=updated;}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}

/// <summary>
/// The top surface of a model on a grid (the highest point at every X, Y), and where the tip of a flat or ball-nose cutter can sit on it.
/// This is the 3-axis view of a part: undercuts are invisible, which is exactly what a 3-axis machine sees too.
/// </summary>
public sealed class HeightMap
{
    public readonly double X0, Y0, Cell;
    public readonly int Nx, Ny;
    readonly double[] _h;          // top of the model, NaN where there is none
    double[]? _tip;                // cutter tip height, NaN where the cutter would touch nothing

    HeightMap(double x0, double y0, double cell, int nx, int ny) { X0 = x0; Y0 = y0; Cell = cell; Nx = nx; Ny = ny; _h = new double[nx * ny]; Array.Fill(_h, double.NaN); }

    public static HeightMap FromMesh(MeshData mesh, double cell, double padding=0)
    {
        var lo = mesh.Min-new Vec3(padding,padding,0); var hi = mesh.Max+new Vec3(padding,padding,0);
        // keep the grid a sensible size however big the part is
        cell = Math.Max(cell, Math.Max(hi.X - lo.X, hi.Y - lo.Y) / 600);
        int nx = (int)Math.Ceiling((hi.X - lo.X) / cell) + 1, ny = (int)Math.Ceiling((hi.Y - lo.Y) / cell) + 1;
        var map = new HeightMap(lo.X, lo.Y, cell, nx, ny);
        for (int t = 0; t < mesh.Indices.Length / 3; t++)
        {
            Vec3 a = P(mesh, mesh.Indices[3 * t]), b = P(mesh, mesh.Indices[3 * t + 1]), c = P(mesh, mesh.Indices[3 * t + 2]);
            double det = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
            if (Math.Abs(det) < 1e-12) continue;                                   // edge-on to the cutter: no top surface here
            int i0 = Math.Max(0, (int)Math.Floor((Math.Min(a.X, Math.Min(b.X, c.X)) - lo.X) / cell)), i1 = Math.Min(nx - 1, (int)Math.Ceiling((Math.Max(a.X, Math.Max(b.X, c.X)) - lo.X) / cell));
            int j0 = Math.Max(0, (int)Math.Floor((Math.Min(a.Y, Math.Min(b.Y, c.Y)) - lo.Y) / cell)), j1 = Math.Min(ny - 1, (int)Math.Ceiling((Math.Max(a.Y, Math.Max(b.Y, c.Y)) - lo.Y) / cell));
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    double px = lo.X + i * cell, py = lo.Y + j * cell;
                    double l1 = ((b.Y - c.Y) * (px - c.X) + (c.X - b.X) * (py - c.Y)) / det, l2 = ((c.Y - a.Y) * (px - c.X) + (a.X - c.X) * (py - c.Y)) / det, l3 = 1 - l1 - l2;
                    const double e = -1e-9;
                    if (l1 < e || l2 < e || l3 < e) continue;
                    double z = l1 * a.Z + l2 * b.Z + l3 * c.Z;
                    ref double h = ref map._h[j * nx + i];
                    if (double.IsNaN(h) || z > h) h = z;
                }
        }
        return map;
    }

    static Vec3 P(MeshData m, int i) => new(m.Positions[3 * i], m.Positions[3 * i + 1], m.Positions[3 * i + 2]);

    public double Top(int i, int j) => i < 0 || j < 0 || i >= Nx || j >= Ny ? double.NaN : _h[j * Nx + i];

    /// <summary>Work out where the tip of a cutter of this radius rests on the model.</summary>
    public void Dilate(double radius, bool ball)
    {
        int r = (int)Math.Ceiling(radius / Cell);
        var offsets = new System.Collections.Generic.List<(int di, int dj, double lift)>();
        for (int dj = -r; dj <= r; dj++)
            for (int di = -r; di <= r; di++)
            {
                double d2 = (di * di + dj * dj) * Cell * Cell;
                if (d2 > radius * radius) continue;
                offsets.Add((di, dj, ball ? Math.Sqrt(radius * radius - d2) - radius : 0));
            }
        _tip = new double[Nx * Ny];
        for (int j = 0; j < Ny; j++)
            for (int i = 0; i < Nx; i++)
            {
                double best = double.NaN;
                foreach (var (di, dj, lift) in offsets)
                {
                    double h = Top(i + di, j + dj);
                    if (double.IsNaN(h)) continue;
                    double z = h + lift;
                    if (double.IsNaN(best) || z > best) best = z;
                }
                _tip[j * Nx + i] = best;
            }
    }

    /// <summary>The cutter tip height at a point (nearest grid cell), or NaN where the cutter is off the model. Call Dilate first.</summary>
    public double Tip(double x, double y)
    {
        if (_tip == null) throw new InvalidOperationException("Call Dilate first.");
        int i = (int)Math.Round((x - X0) / Cell), j = (int)Math.Round((y - Y0) / Cell);
        return i < 0 || j < 0 || i >= Nx || j >= Ny ? double.NaN : _tip[j * Nx + i];
    }
}
