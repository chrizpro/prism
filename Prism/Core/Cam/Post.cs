using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Prism.Kernel;

namespace Prism.Core.Cam;

public enum Controller { GRBL, Mach3, LinuxCNC }

/// <summary>Turns toolpaths into G-code text for a machine.</summary>
public static class Post
{
    static readonly CultureInfo C = CultureInfo.InvariantCulture;
    static string N(double v) => v.ToString("0.###", C);

    /// <summary>CNC program: every machining toolpath in order. origin is subtracted from the model coordinates (the work zero).</summary>
    public static string Cnc(IEnumerable<ToolpathInfo> paths, Controller controller, Vec3 origin, string title)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"({title})");
        sb.AppendLine($"(Posted by Prism for {controller}, {DateTime.Now:yyyy-MM-dd HH:mm})");
        sb.AppendLine("G21 G90 G17 G94" + (controller == Controller.GRBL ? " G54" : " G54"));
        string? lastTool = null; int toolNo = 0;
        double fx = double.NaN;
        foreach (var tp in paths.Where(p => !p.IsPrint))
        {
            sb.AppendLine($"({tp.Operation})");
            if (tp.Tool != lastTool)
            {
                toolNo++; lastTool = tp.Tool;
                sb.AppendLine("M5");
                if (controller == Controller.GRBL) { sb.AppendLine($"(Fit tool {toolNo}: {tp.Tool}, then press cycle start)"); sb.AppendLine("M0"); }
                else sb.AppendLine($"T{toolNo} M6 ({tp.Tool})");
            }
            sb.AppendLine($"S{N(tp.Rpm)} M3");
            sb.AppendLine("G4 P2");
            double cx = double.NaN, cy = double.NaN, cz = double.NaN; fx = double.NaN;
            for (int i = 0; i < tp.MoveCount; i++)
            {
                var (x, y, z, k) = tp.Move(i);
                x -= origin.X; y -= origin.Y; z -= origin.Z;
                var line = new StringBuilder();
                if (k == MoveKind.Rapid)
                {
                    if (i == 0) { sb.AppendLine($"G0 Z{N(tp.SafeZ - origin.Z)}"); }
                    line.Append("G0");
                    if (!Same(x, cx)) line.Append($" X{N(x)}");
                    if (!Same(y, cy)) line.Append($" Y{N(y)}");
                    if (!Same(z, cz)) line.Append($" Z{N(z)}");
                    if (line.Length > 2) sb.AppendLine(line.ToString());
                }
                else
                {
                    double f = k == MoveKind.Plunge ? tp.Plunge : tp.Feed;
                    line.Append("G1");
                    bool mv = false;
                    if (!Same(x, cx)) { line.Append($" X{N(x)}"); mv = true; }
                    if (!Same(y, cy)) { line.Append($" Y{N(y)}"); mv = true; }
                    if (!Same(z, cz)) { line.Append($" Z{N(z)}"); mv = true; }
                    if (!Same(f, fx)) { line.Append($" F{N(f)}"); fx = f; }
                    if (mv) sb.AppendLine(line.ToString());
                }
                cx = x; cy = y; cz = z;
            }
            sb.AppendLine($"G0 Z{N(tp.SafeZ - origin.Z)}");
        }
        sb.AppendLine("M5");
        sb.AppendLine(controller == Controller.GRBL ? "M2" : "M30");
        return sb.ToString();
    }

    /// <summary>Lathe program: X is written as a diameter and Z is measured from the front face of the stock. Needs a controller with lathe support.</summary>
    public static string Lathe(IEnumerable<ToolpathInfo> paths, Controller controller, string title)
    {
        if (controller == Controller.GRBL) throw new InvalidOperationException("GRBL cannot run a lathe. Choose Mach3 or LinuxCNC for turning programs.");
        var list = paths.ToList();
        double front = list.SelectMany(p => Enumerable.Range(0, p.MoveCount).Where(i => (MoveKind)(int)p.Moves[4 * i + 3] != MoveKind.Rapid).Select(i => p.Moves[4 * i + 2])).DefaultIfEmpty(0).Max();
        var sb = new StringBuilder();
        sb.AppendLine($"({title})");
        sb.AppendLine($"(Posted by Prism for {controller} lathe, {DateTime.Now:yyyy-MM-dd HH:mm})");
        sb.AppendLine("(Z zero is the front face of the stock; X is a diameter)");
        sb.AppendLine("G21 G90 G18 G94 G7 G54");
        string? lastTool = null; int toolNo = 0;
        foreach (var tp in list)
        {
            sb.AppendLine($"({tp.Operation})");
            if (tp.Tool != lastTool) { toolNo++; lastTool = tp.Tool; sb.AppendLine("M5"); sb.AppendLine($"T{toolNo} M6 ({tp.Tool})"); }
            sb.AppendLine($"G97 S{N(tp.Rpm)} M3");
            double cx = double.NaN, cz = double.NaN, fx = double.NaN;
            for (int i = 0; i < tp.MoveCount; i++)
            {
                var (x, _, z, k) = tp.Move(i); double dx = 2 * x, dz = z - front;
                var line = new StringBuilder(k == MoveKind.Rapid ? "G0" : "G1"); bool moved = false;
                if (!Same(dx, cx)) { line.Append($" X{N(dx)}"); moved = true; }
                if (!Same(dz, cz)) { line.Append($" Z{N(dz)}"); moved = true; }
                if (k != MoveKind.Rapid)
                {
                    double f = k == MoveKind.Plunge ? tp.Plunge : tp.Feed;
                    if (!Same(f, fx)) { line.Append($" F{N(f)}"); fx = f; }
                }
                if (moved) sb.AppendLine(line.ToString());
                cx = dx; cz = dz;
            }
        }
        sb.AppendLine("M5");
        sb.AppendLine("M30");
        return sb.ToString();
    }

    static bool Same(double a, double b) => !double.IsNaN(a) && !double.IsNaN(b) && Math.Abs(a - b) < 5e-4;

    /// <summary>FDM printer program (Marlin / RepRap flavour: relative extrusion, retraction on long travels).</summary>
    public static string Print(ToolpathInfo tp, string title)
    {
        var p = tp.Print ?? throw new InvalidOperationException("This toolpath was not made for printing.");
        var sb = new StringBuilder();
        var (work, _) = tp.Lengths();
        sb.AppendLine($"; {title}");
        sb.AppendLine($"; Sliced by Prism {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"; layers: {tp.LayerCount}   layer height: {N(p.LayerHeight)}   line width: {N(p.LineWidth)}");
        sb.AppendLine($"; estimated time: {tp.EstimateMinutes():0.#} min");
        double fil = FilamentLength(tp, p);
        sb.AppendLine($"; filament: {fil / 1000:0.00} m   (about {fil * Math.PI * Math.Pow(p.FilamentDia / 2, 2) / 1000 * 1.24:0.#} g of PLA)");
        sb.AppendLine("G21\nG90\nM83");
        sb.AppendLine($"M140 S{p.BedTemp}\nM104 S{p.NozzleTemp}\nG28\nM190 S{p.BedTemp}\nM109 S{p.NozzleTemp}\nG92 E0");
        // a short prime line at the front of the bed
        double ox = 5, oy = 5;
        double ePrime = 60 * p.FirstLayer * p.LineWidth / (Math.PI * Math.Pow(p.FilamentDia / 2, 2)) * 1.1;
        sb.AppendLine($"G1 X{N(ox)} Y{N(oy)} Z{N(p.FirstLayer)} F3000\nG1 X{N(ox + 60)} E{N(ePrime)} F{N(p.FirstLayerSpeed * 60)}\nG92 E0");
        double cx = double.NaN, cy = double.NaN, cz = double.NaN; bool retracted = false;
        for (int layer = 0; layer < tp.LayerCount; layer++)
        {
            int a = tp.LayerStarts[layer], b = layer + 1 < tp.LayerCount ? tp.LayerStarts[layer + 1] : tp.MoveCount;
            double h = layer == 0 ? p.FirstLayer : p.LayerHeight;
            sb.AppendLine($";LAYER:{layer}");
            if (layer == 2 && p.FanPercent > 0) sb.AppendLine($"M106 S{(int)Math.Round(p.FanPercent * 2.55)}");
            for (int i = a; i < b; i++)
            {
                var (x, y, z, k) = tp.Move(i);
                x += p.OffsetX; y += p.OffsetY; z += p.OffsetZ;
                if (k == MoveKind.Rapid)
                {
                    double d = double.IsNaN(cx) ? 0 : Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (d > 1.5 && p.Retract > 0 && !retracted) { sb.AppendLine($"G1 E{N(-p.Retract)} F{N(p.RetractSpeed * 60)}"); retracted = true; }
                    var line = new StringBuilder("G0"); bool moved = false;
                    if (!Same(x, cx)) { line.Append($" X{N(x)}"); moved = true; }
                    if (!Same(y, cy)) { line.Append($" Y{N(y)}"); moved = true; }
                    if (!Same(z, cz)) { line.Append($" Z{N(z)}"); moved = true; }
                    line.Append($" F{N(p.TravelSpeed * 60)}");
                    if (moved) sb.AppendLine(line.ToString());
                }
                else
                {
                    if (retracted) { sb.AppendLine($"G1 E{N(p.Retract)} F{N(p.RetractSpeed * 60)}"); retracted = false; }
                    double len = double.IsNaN(cx) ? 0 : Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    double e = len * h * p.LineWidth / (Math.PI * Math.Pow(p.FilamentDia / 2, 2)) * p.Flow;
                    double speed = layer == 0 ? p.FirstLayerSpeed : p.PrintSpeed * (k == MoveKind.OuterWall ? 0.6 : k == MoveKind.Infill ? 1.2 : 1.0);
                    sb.AppendLine($"G1 X{N(x)} Y{N(y)} E{e.ToString("0.#####", C)} F{N(speed * 60)}");
                }
                cx = x; cy = y; cz = z;
            }
        }
        sb.AppendLine("M104 S0\nM140 S0\nM107\nG91\nG1 Z10 F600\nG90\nG28 X Y\nM84");
        return sb.ToString();
    }

    public static double FilamentLength(ToolpathInfo tp, PrintParams p)
    {
        double total = 0;
        for (int layer = 0; layer < tp.LayerCount; layer++)
        {
            int a = tp.LayerStarts[layer], b = layer + 1 < tp.LayerCount ? tp.LayerStarts[layer + 1] : tp.MoveCount;
            double h = layer == 0 ? p.FirstLayer : p.LayerHeight;
            for (int i = a + 1; i < b; i++)
            {
                if ((MoveKind)(int)tp.Moves[4 * i + 3] == MoveKind.Rapid) continue;
                double dx = tp.Moves[4 * i] - tp.Moves[4 * (i - 1)], dy = tp.Moves[4 * i + 1] - tp.Moves[4 * (i - 1) + 1];
                total += Math.Sqrt(dx * dx + dy * dy) * h * p.LineWidth / (Math.PI * Math.Pow(p.FilamentDia / 2, 2)) * p.Flow;
            }
        }
        return total;
    }
}
