using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Prism.Core.Assembly;

namespace Prism.Core.Cam;

/// <summary>What a single move of a toolpath is doing.</summary>
public enum MoveKind : byte { Rapid = 0, Cut = 1, Plunge = 2, OuterWall = 3, InnerWall = 4, Skin = 5, Infill = 6, Brim = 7 }

/// <summary>3D-printing settings that travel with a sliced toolpath (they decide the extrusion in the G-code).</summary>
public sealed record PrintParams(
    double LayerHeight, double FirstLayer, double LineWidth, double FilamentDia, double Flow,
    double PrintSpeed, double FirstLayerSpeed, double TravelSpeed, double Retract, double RetractSpeed,
    int NozzleTemp, int BedTemp, int FanPercent, double BedX, double BedY, double OffsetX, double OffsetY, double OffsetZ);

/// <summary>
/// A computed toolpath: a list of moves (x, y, z and a kind each). Machining operations and printing layers both use it.
/// The moves are in model coordinates; the post step turns them into G-code for a particular machine.
/// </summary>
public sealed record ToolpathInfo(string Operation, string Tool, double ToolDia, double Feed, double Plunge, double Rpm, double SafeZ,
    double[] Moves, int[] LayerStarts, bool IsPrint, bool Transient, PrintParams? Print = null, double[]? Feeds = null,
    Guid? SetupBody = null, string? SourceSignature = null, Dictionary<Guid,string>? Sources = null, string? SetupSignature = null) : AssemblyData
{
    [JsonIgnore] public int MoveCount => Moves.Length / 4;
    [JsonIgnore] public int LayerCount => LayerStarts.Length;

    public (double x, double y, double z, MoveKind kind) Move(int i) => (Moves[4 * i], Moves[4 * i + 1], Moves[4 * i + 2], (MoveKind)(int)Moves[4 * i + 3]);
    public double FeedAt(int i)
    {
        if(Feeds is { } fs&&fs.Length==MoveCount)return double.IsFinite(fs[i])&&fs[i]>0?fs[i]:throw new InvalidOperationException("Invalid feed in toolpath.");
        var k=Move(i).kind;
        if(IsPrint&&Print is { } p)return Math.Max(1,(k==MoveKind.Rapid?p.TravelSpeed:Move(i).z<=p.FirstLayer+1e-6?p.FirstLayerSpeed:p.PrintSpeed*(k==MoveKind.OuterWall?.6:k==MoveKind.Infill?1.2:1))*60);
        return k==MoveKind.Rapid?5000:k==MoveKind.Plunge?Math.Max(Plunge,1):Math.Max(Feed,1);
    }

    /// <summary>Path length of cutting (or extruding) moves and of rapids, in mm.</summary>
    public (double work, double rapid) Lengths()
    {
        double w = 0, r = 0;
        for (int i = 1; i < MoveCount; i++)
        {
            double dx = Moves[4 * i] - Moves[4 * (i - 1)], dy = Moves[4 * i + 1] - Moves[4 * (i - 1) + 1], dz = Moves[4 * i + 2] - Moves[4 * (i - 1) + 2];
            double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if ((MoveKind)(int)Moves[4 * i + 3] == MoveKind.Rapid) r += d; else w += d;
        }
        return (w, r);
    }

    /// <summary>Rough machine time in minutes (feed rates only; no acceleration).</summary>
    public double EstimateMinutes()
    {
        double t = 0;
        for (int i = 1; i < MoveCount; i++)
        {
            double dx = Moves[4 * i] - Moves[4 * (i - 1)], dy = Moves[4 * i + 1] - Moves[4 * (i - 1) + 1], dz = Moves[4 * i + 2] - Moves[4 * (i - 1) + 2];
            double d = Math.Sqrt(dx * dx + dy * dy + dz * dz); var k = (MoveKind)(int)Moves[4 * i + 3];
            double speed=FeedAt(i);
            t += d / Math.Max(speed, 1);
        }
        return t;
    }
}
