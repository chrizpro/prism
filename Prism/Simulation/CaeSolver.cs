using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Prism.Core;
using Prism.Kernel;

namespace Prism.Simulation;

public enum CaeKind { SteadyThermal, TransientThermal, Modal }
/// <param name="OtherBodies">More bodies, bonded where they touch. Faces then count on through each body's faces, in this order.</param>
/// <param name="HeatPower">Total heat put into HeatFaces (W).</param>
/// <param name="FilmCoefficient">Convection on CoolFaces (W/m²·K) toward Ambient (°C). Emissivity adds radiation to the same surroundings.</param>
public sealed record CaeSettings(CaeKind Kind, int[] FirstFaces, int[] SecondFaces, double FirstTemperature = 100,
    double SecondTemperature = 20, double InitialTemperature = 20, double Duration = 60, int Modes = 6, double ElementSize = 0, Guid[]? OtherBodies = null,
    int[]? HeatFaces = null, double HeatPower = 0, int[]? CoolFaces = null, double FilmCoefficient = 0, double Ambient = 20, double Emissivity = 0);
public sealed record CaeOutcome(StudyRunner.Prepared Prepared, CaeKind Kind, double[] Temperatures, double[] Frequencies,
    IReadOnlyList<double[]> Modes, string Folder);

/// <summary>Separate-process CalculiX adapter. The existing CAD mesher supplies quadratic tetrahedra.</summary>
public static class CaeSolver
{
    public static string DefaultExecutable => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Prism", "Solvers", "CalculiX", "calculix_2.23_4win", "ccx_static.exe");
    public static string Executable => Environment.GetEnvironmentVariable("PRISM_CCX") ?? DefaultExecutable;
    // CalculiX's numeric input fields are limited to 20 characters, including exponent/sign.
    static string F(double x) => x.ToString("G12", CultureInfo.InvariantCulture);
    public static HashSet<int> FaceNodes(StudyRunner.Prepared p, IEnumerable<int> faces)
    {
        var set = faces.ToHashSet(); var nodes = new HashSet<int>();
        for (int b = 0; b < p.Mesh.BoundaryCount; b++) if (set.Contains(p.FaceOfBoundary[b]))
            for (int i = 0; i < 6; i++) nodes.Add(p.Mesh.Boundary[b * 6 + i]);
        return nodes;
    }
    /// <summary>Nodes, elements and materials: one element set and one material per body.</summary>
    internal static void WriteBodies(StringBuilder b, StudyRunner.Prepared p, bool thermal)
    {
        var m = p.Mesh; var parts = p.Parts ?? [new MeshPart(p.Body, 0, m.NodeCount, 0, m.TetCount, 0, m.BoundaryCount, 0)];
        b.Append("*HEADING\nPrism CAE: mm, N, s (structural); mm, J, kg, s, Celsius (thermal)\n*NODE,NSET=ALLNODES\n");
        for (int n = 0; n < m.NodeCount; n++) b.AppendLine($"{n+1},{F(m.Nodes[3*n])},{F(m.Nodes[3*n+1])},{F(m.Nodes[3*n+2])}");
        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i]; var material = part.Body.Material ?? throw new InvalidOperationException($"Assign a material to {part.Body.Name} before running CAE.");
            if(thermal)MaterialRules.Thermal(material);else MaterialRules.Structural(material);
            if (!(material.Density > 0) || (thermal && (!(material.Conductivity > 0) || !(material.SpecificHeat > 0))) ||
                (!thermal && (!(material.Youngs > 0) || material.Poisson <= -1 || material.Poisson >= .5)))
                throw new InvalidOperationException($"The material of {part.Body.Name} lacks valid properties for this analysis.");
            b.AppendLine($"*ELEMENT,TYPE={(thermal ? "DC3D10" : "C3D10")},ELSET=BODY{i}");
            for (int e = part.TetStart; e < part.TetStart + part.TetCount; e++) b.AppendLine($"{e+1}," + string.Join(",", m.Tets.Skip(e*10).Take(10).Select(n=>n+1)));
            b.AppendLine($"*MATERIAL,NAME=MAT{i}");
            if (thermal) b.AppendLine($"*CONDUCTIVITY\n{F(material.Conductivity/1000)}\n*DENSITY\n{F(material.Density*1e-6)}\n*SPECIFIC HEAT\n{F(material.SpecificHeat)}");
            else b.AppendLine($"*ELASTIC\n{F(material.Youngs*1000)},{F(material.Poisson)}\n*DENSITY\n{F(material.Density*1e-9)}");
            b.AppendLine($"*SOLID SECTION,ELSET=BODY{i},MATERIAL=MAT{i}");
        }
    }

    public static string Input(StudyRunner.Prepared p, CaeSettings settings)
    {
        var m = p.Mesh; bool thermal = settings.Kind != CaeKind.Modal;
        if (m.NodeCount > 100000 || m.TetCount > 200000) throw new InvalidOperationException("Use a coarser mesh for this CAE run (limit: 100,000 nodes / 200,000 elements).");
        var first = FaceNodes(p, settings.FirstFaces); var second = FaceNodes(p, settings.SecondFaces);
        var heated = settings.HeatFaces ?? []; var cooled = settings.CoolFaces ?? [];
        bool cooling = thermal && cooled.Length > 0 && (settings.FilmCoefficient > 0 || settings.Emissivity > 0);
        if (!thermal && first.Count == 0) throw new InvalidOperationException("Pick boundary faces of the selected body.");
        if (thermal && first.Count == 0 && second.Count == 0 && !cooling) throw new InvalidOperationException("Heat needs somewhere to go: hold a face at a temperature, or cool one.");
        if (thermal && heated.Length > 0 && !(settings.HeatPower > 0)) throw new InvalidOperationException("Enter the heat power for the heated faces.");
        if (thermal && first.Overlaps(second)) throw new InvalidOperationException("Temperature boundaries share mesh nodes. Choose separated faces or combine them into one temperature boundary.");
        if (settings.Modes < 1 || settings.Modes > 20 || !(settings.Duration > 0)) throw new InvalidOperationException("Use 1–20 modes and a positive duration.");
        var b = new StringBuilder();
        WriteBodies(b, p, thermal);
        CaeAssembly.WriteBonds(b, p, first.Concat(second).ToHashSet());
        if (thermal)
        {
            if (settings.Emissivity > 0) b.AppendLine("*PHYSICAL CONSTANTS,ABSOLUTE ZERO=-273.15,STEFAN BOLTZMANN=5.670374E-14");
            b.AppendLine($"*INITIAL CONDITIONS,TYPE=TEMPERATURE\nALLNODES,{F(settings.Kind == CaeKind.SteadyThermal && first.Count + second.Count == 0 ? settings.Ambient : settings.InitialTemperature)}");
            b.AppendLine($"*AMPLITUDE,NAME=HOLD,TIME=TOTAL TIME\n0,1,{F(settings.Duration)},1");
            b.AppendLine(settings.Kind == CaeKind.SteadyThermal ? "*STEP\n*HEAT TRANSFER,STEADY STATE\n1,1" :
                $"*STEP,INC=10000\n*HEAT TRANSFER\n{F(settings.Duration/20)},{F(settings.Duration)},{F(settings.Duration/100000)},{F(settings.Duration/10)}");
            if (first.Count + second.Count > 0)
            {
                b.AppendLine("*BOUNDARY,AMPLITUDE=HOLD");
                foreach (int n in first.Order()) b.AppendLine($"{n+1},11,11,{F(settings.FirstTemperature)}");
                foreach (int n in second.Order()) b.AppendLine($"{n+1},11,11,{F(settings.SecondTemperature)}");
            }
            if (heated.Length > 0)
            {
                var tris = CaeAssembly.ElementFaces(p, heated); double area = tris.Sum(t => t.area);
                b.AppendLine("*DFLUX");
                foreach (var (elem, face, _) in tris) b.AppendLine($"{elem+1},S{face+1},{F(settings.HeatPower/area)}");
            }
            if (cooling)
            {
                var tris = CaeAssembly.ElementFaces(p, cooled);
                if (settings.FilmCoefficient > 0)
                {
                    b.AppendLine("*FILM");
                    foreach (var (elem, face, _) in tris) b.AppendLine($"{elem+1},F{face+1},{F(settings.Ambient)},{F(settings.FilmCoefficient*1e-6)}");
                }
                if (settings.Emissivity > 0)
                {
                    b.AppendLine("*RADIATE");
                    foreach (var (elem, face, _) in tris) b.AppendLine($"{elem+1},R{face+1},{F(settings.Ambient)},{F(settings.Emissivity)}");
                }
            }
            b.AppendLine("*NODE PRINT,NSET=ALLNODES\nNT\n*NODE FILE\nNT");
        }
        else
        {
            b.AppendLine("*BOUNDARY"); foreach (int n in first.Order()) b.AppendLine($"{n+1},1,3");
            b.AppendLine($"*STEP\n*FREQUENCY\n{settings.Modes}\n*NODE PRINT,NSET=ALLNODES\nU\n*NODE FILE\nU");
        }
        b.AppendLine("*END STEP"); return b.ToString();
    }
    /// <summary>Runs analysis.inp in the folder and checks that it finished. Returns the seconds it took.</summary>
    internal static double Run(string folder, CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        var info = new ProcessStartInfo(Executable) { WorkingDirectory=folder, UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true };
        info.ArgumentList.Add("-i"); info.ArgumentList.Add("analysis"); info.Environment["OMP_NUM_THREADS"]="2";
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start CalculiX.");
        using var cancel = token.Register(()=> { try { if(!process.HasExited) process.Kill(true); } catch(InvalidOperationException) { } });
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(600000)) { process.Kill(true); throw new TimeoutException("CalculiX exceeded the 10 minute run limit. Try a coarser mesh."); }
        token.ThrowIfCancellationRequested(); var log=stdout.GetAwaiter().GetResult()+"\n"+stderr.GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(folder,"solver.log"),log);
        if(process.ExitCode!=0 || log.Contains("*ERROR",StringComparison.OrdinalIgnoreCase) || !log.Contains("Job finished",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("CalculiX did not complete successfully. See solver.log in " + folder);
        return clock.Elapsed.TotalSeconds;
    }

    public static CaeOutcome Solve(SceneObject body, CaeSettings settings, string folder, CancellationToken token = default, Action<string>? progress = null, IReadOnlyList<SceneObject>? others = null)
    {
        if (!File.Exists(Executable)) throw new FileNotFoundException("CalculiX is not installed. Run tools/Install-CaeSolver.ps1 or set PRISM_CCX to ccx.exe.");
        var bodies = new List<SceneObject> { body }; bodies.AddRange(others ?? []);
        foreach (var part in bodies)
        {
            var material=part.Material ?? throw new InvalidOperationException($"Assign a material to {part.Name} before running CAE.");
            if(settings.Kind==CaeKind.Modal)MaterialRules.Structural(material);else MaterialRules.Thermal(material);
        }
        progress?.Invoke("Meshing…"); token.ThrowIfCancellationRequested();
        var p = bodies.Count == 1 ? StudyRunner.Mesh(body, settings.ElementSize) : CaeAssembly.Mesh(bodies, settings.ElementSize);
        try
        {
            var input = Input(p, settings); token.ThrowIfCancellationRequested(); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"analysis.inp"), input);
            progress?.Invoke($"Solving {p.Mesh.TetCount:N0} quadratic tetrahedra with CalculiX…");
            Run(folder, token);
            var outcome=Parse(p,settings.Kind,File.ReadAllText(Path.Combine(folder,"analysis.dat")),folder);
            if(settings.Kind==CaeKind.Modal && outcome.Frequencies.Length==0) throw new InvalidOperationException("No natural frequencies were returned. Check the constraints and mesh.");
            File.WriteAllText(Path.Combine(folder,"summary.json"),System.Text.Json.JsonSerializer.Serialize(new {kind=settings.Kind.ToString(), material=string.Join(" + ",bodies.Select(x=>x.Material!.Name).Distinct()), physicalProperties=bodies.Select(x=>x.Material).ToArray(),
                nodes=p.Mesh.NodeCount,elements=p.Mesh.TetCount,temperatures=outcome.Temperatures.Length>0?new[]{outcome.Temperatures.Min(),outcome.Temperatures.Max()}:[],frequenciesHz=outcome.Frequencies}));
            if(outcome.Temperatures.Length>0) File.WriteAllLines(Path.Combine(folder,"temperatures.csv"),new[]{"node,x_mm,y_mm,z_mm,temperature_C"}.Concat(Enumerable.Range(0,p.Mesh.NodeCount).Select(n=>$"{n+1},{F(p.Mesh.Nodes[3*n])},{F(p.Mesh.Nodes[3*n+1])},{F(p.Mesh.Nodes[3*n+2])},{F(outcome.Temperatures[n])}")));
            return outcome;
        }
        catch { p.Mesh.Dispose(); throw; }
    }
    public static CaeOutcome Parse(StudyRunner.Prepared p,CaeKind kind,string text,string folder)
    {
        double[] temperatures=[]; var modes=new List<double[]>(); var frequencies=new List<double>();
        string[] lines=text.Split('\n'); bool eigen=false;
        for(int i=0;i<lines.Length;i++)
        {
            string line=lines[i].Trim();
            if(line.Contains("E I G E N V A L U E   O U T P U T")) eigen=true;
            if(eigen && line.Contains("P A R T I C I P A T I O N")) eigen=false;
            var cells=line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
            if(eigen && cells.Length==5 && int.TryParse(cells[0],out _) && double.TryParse(cells[3],NumberStyles.Float,CultureInfo.InvariantCulture,out double hz))
            { if(hz>0 && double.IsFinite(hz)) frequencies.Add(hz); }
            bool temp=line.StartsWith("temperatures for set ALLNODES",StringComparison.OrdinalIgnoreCase);
            bool disp=line.StartsWith("displacements (vx,vy,vz) for set ALLNODES",StringComparison.OrdinalIgnoreCase);
            if(!temp&&!disp) continue;
            int count=0,dim=temp?1:3; var values=new double[p.Mesh.NodeCount*dim]; var seen=new HashSet<int>();
            for(int j=i+1;j<lines.Length;j++)
            {
                var row=lines[j].Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
                if(row.Length==0 && count==0) continue;
                if(row.Length!=dim+1 || !int.TryParse(row[0],out int node)) break;
                if(node<1 || node>p.Mesh.NodeCount || !seen.Add(node)) throw new InvalidDataException("Unexpected node ids in CalculiX output.");
                for(int c=0;c<dim;c++) { double v=double.Parse(row[c+1],CultureInfo.InvariantCulture); if(!double.IsFinite(v)) throw new InvalidDataException("Non-finite solver result."); values[(node-1)*dim+c]=v; }
                count++; i=j;
            }
            if(count!=p.Mesh.NodeCount) throw new InvalidDataException("Incomplete nodal results from CalculiX.");
            if(temp) temperatures=values; else modes.Add(values);
        }
        if(kind!=CaeKind.Modal && temperatures.Length==0) throw new InvalidDataException("No temperature results were returned.");
        if(kind==CaeKind.Modal && modes.Count!=frequencies.Count) throw new InvalidDataException("Mode shapes and frequencies did not match.");
        return new(p,kind,temperatures,frequencies.ToArray(),modes,folder);
    }
}
