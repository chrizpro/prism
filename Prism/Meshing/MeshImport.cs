using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Prism.Kernel;

namespace Prism.Meshing;

public enum ImportUnits { Auto, Millimeter, Centimeter, Meter, Inch }
public enum ImportUpAxis { Z, Y, X }
/// <summary>How an imported file is cut into bodies. Auto splits 3MF files by object and leaves every other format as one body.</summary>
public enum ImportSplit { Auto, None, Groups, Components }

/// <summary>Choices made when a mesh file is imported. The defaults reproduce a plain import.</summary>
public sealed record MeshImportOptions
{
    /// <summary>Unit of the numbers in the file. Auto uses the unit a 3MF declares, otherwise millimetres.</summary>
    public ImportUnits Units { get; init; } = ImportUnits.Auto;
    /// <summary>Z = already Z-up (kept); Y = rotate Y-up files to Z-up; X = rotate X-up files to Z-up.</summary>
    public ImportUpAxis UpAxis { get; init; } = ImportUpAxis.Z;
    /// <summary>Corners closer than this (mm, after unit conversion) are merged. 0 merges only identical corners.</summary>
    public double WeldTolerance { get; init; }
    /// <summary>Merge near-duplicate vertices, drop junk triangles, fix orientation and close small holes.</summary>
    public bool AutoRepair { get; init; }
    public ImportSplit Split { get; init; } = ImportSplit.Auto;

    public static MeshImportOptions Default { get; } = new();
}

public sealed class ImportedPart
{
    public required string Name { get; init; }
    public required TriMesh Mesh { get; init; }
    /// <summary>One colour for the whole part when all its triangles share it (ARGB); otherwise null.</summary>
    public uint? Color { get; init; }
}

public sealed class MeshImportResult
{
    public required string Name { get; init; }
    public List<ImportedPart> Parts { get; } = [];
    /// <summary>What happened and what to look at: file warnings, repairs, the size check.</summary>
    public List<string> Messages { get; } = [];
    public double ScaleToMm { get; set; } = 1;
    public string UnitUsed { get; set; } = "mm";
    /// <summary>Overall size in millimetres after units and rotation.</summary>
    public Vec3 SizeMm { get; set; }
    /// <summary>A "did you mean inches?" style hint when the size looks wrong; null when it looks plausible.</summary>
    public string? SizeWarning { get; set; }
    public int TriCount => Parts.Sum(p => p.Mesh.TriCount);
}

/// <summary>Turns a parsed <see cref="MeshFile"/> into bodies: units, up axis, welding, repair and splitting. Everything here is pure C# and runs off the UI thread.</summary>
public static class MeshImporter
{
    static readonly (ImportUnits unit, string label, double mm)[] UnitTable =
    [
        (ImportUnits.Millimeter, "millimetres", 1), (ImportUnits.Centimeter, "centimetres", 10), (ImportUnits.Meter, "metres", 1000), (ImportUnits.Inch, "inches", 25.4),
    ];

    /// <summary>Millimetres per unit for the chosen option, using the file's own unit for Auto.</summary>
    public static double ScaleFor(ImportUnits u, double? fileUnitToMm, out string label)
    {
        if (u == ImportUnits.Auto)
        {
            double s = fileUnitToMm ?? 1;
            var hit = UnitTable.FirstOrDefault(x => Math.Abs(x.mm - s) < 1e-9);
            label = hit.label ?? (s == 0.001 ? "microns" : s == 304.8 ? "feet" : "millimetres");
            return s;
        }
        var t = UnitTable.First(x => x.unit == u);
        label = t.label; return t.mm;
    }

    /// <summary>
    /// A hint when the model would come out implausibly large or small, such as "This will be 25,400 mm across - did you mean inches?".
    /// <paramref name="rawExtent"/> is the largest side in file units; null when the size looks fine.
    /// </summary>
    public static string? SizeHint(double rawExtent, double scaleToMm, string unitLabel)
    {
        if (!(rawExtent > 0) || !double.IsFinite(rawExtent)) return null;
        double mm = rawExtent * scaleToMm;
        const double Big = 3000, Small = 0.5;
        if (mm <= Big && mm >= Small) return null;
        // the alternative unit that gives a plausible size (5 mm to 3 m) and is closest to the current reading
        var alt = UnitTable.Where(x => Math.Abs(x.mm - scaleToMm) > 1e-9 && rawExtent * x.mm is >= 5 and <= Big)
            .OrderBy(x => Math.Abs(Math.Log(x.mm / scaleToMm))).Select(x => ((ImportUnits, string, double)?)x).FirstOrDefault();
        var size = mm.ToString("#,0.##", CultureInfo.InvariantCulture);
        if (alt is { } a) return $"Read as {unitLabel}, this will be {size} mm across - did you mean {a.Item2}? ({(rawExtent * a.Item3).ToString("#,0.##", CultureInfo.InvariantCulture)} mm)";
        return mm > Big ? $"Read as {unitLabel}, this will be {size} mm across - check the units." : $"Read as {unitLabel}, this will be only {size} mm across - check the units.";
    }

    static Vec3 Up(Vec3 p, ImportUpAxis axis) => axis switch
    {
        ImportUpAxis.Y => new Vec3(p.X, -p.Z, p.Y),     // +90 degrees about X: Y-up to Z-up
        ImportUpAxis.X => new Vec3(-p.Z, p.Y, p.X),     // -90 degrees about Y: X-up to Z-up
        _ => p,
    };

    /// <summary>Parse and process in one step (synchronous; see <see cref="ImportAsync"/> for the background version).</summary>
    public static MeshImportResult Import(string path, MeshImportOptions? options = null, IProgress<ImportProgress>? progress = null, CancellationToken ct = default)
    {
        var file = MeshIO.Read(path, progress, ct);
        return Process(file, options, progress, ct);
    }

    /// <summary>Reads and processes the file on a background thread.</summary>
    public static Task<MeshImportResult> ImportAsync(string path, MeshImportOptions? options = null, IProgress<ImportProgress>? progress = null, CancellationToken ct = default)
        => Task.Run(() => Import(path, options, progress, ct), ct);

    /// <summary>Largest side of the file's geometry in file units (before any scaling).</summary>
    public static double RawExtent(MeshFile f) => f.Mesh.TriCount == 0 ? 0 : Math.Max(f.Mesh.Size.X, Math.Max(f.Mesh.Size.Y, f.Mesh.Size.Z));

    public static MeshImportResult Process(MeshFile file, MeshImportOptions? options = null, IProgress<ImportProgress>? progress = null, CancellationToken ct = default)
    {
        var o = options ?? MeshImportOptions.Default;
        var res = new MeshImportResult { Name = file.Name };
        res.Messages.AddRange(file.Warnings);
        void Step(double f, string s) { ct.ThrowIfCancellationRequested(); progress?.Report(new ImportProgress(f, s)); }

        double scale = ScaleFor(o.Units, file.UnitToMm, out string unitLabel);
        res.ScaleToMm = scale; res.UnitUsed = unitLabel;
        if (o.Units != ImportUnits.Auto && file.UnitToMm is { } declared && Math.Abs(declared - scale) > 1e-9)
            res.Messages.Add($"The file declares a different unit than the one chosen; the numbers are read as {unitLabel}.");
        res.SizeWarning = SizeHint(RawExtent(file), scale, unitLabel);

        // units and up axis keep triangle order, so part membership stays valid
        Step(0.05, "Applying units and orientation…");
        var mesh = file.Mesh;
        if (scale != 1 || o.UpAxis != ImportUpAxis.Z)
            mesh = mesh.Transformed(p => Up(p * scale, o.UpAxis));
        if (mesh.TriCount > 0)
        {
            var s = mesh.Size; res.SizeMm = s;
            if (res.SizeWarning != null) res.Messages.Add(res.SizeWarning);
        }
        double diag = mesh.TriCount == 0 ? 0 : mesh.Size.Length;
        double weldTol = o.WeldTolerance;

        var split = o.Split == ImportSplit.Auto ? (file.Format == "3MF" ? ImportSplit.Groups : ImportSplit.None) : o.Split;
        if (split == ImportSplit.Groups && (file.PartOfTri == null || file.PartCount < 2)) split = ImportSplit.None;

        // (part name, triangles) groups
        var groups = new List<(string name, TriMesh mesh)>();
        if (split == ImportSplit.Groups)
        {
            Step(0.15, "Separating parts…");
            var buckets = new List<int>[file.PartCount];
            for (int i = 0; i < buckets.Length; i++) buckets[i] = [];
            for (int t = 0; t < file.PartOfTri!.Length; t++) buckets[file.PartOfTri[t]].Add(t);
            for (int i = 0; i < buckets.Length; i++) if (buckets[i].Count > 0) groups.Add((file.PartNames[i], mesh.Subset(buckets[i])));
        }
        else if (split == ImportSplit.Components)
        {
            Step(0.15, "Finding separate pieces…");
            if (weldTol > 0 || o.AutoRepair) mesh = MeshOps.Weld(mesh, weldTol > 0 ? weldTol : Math.Max(diag * 1e-6, 1e-9));
            weldTol = 0;
            var label = MeshOps.Components(mesh, out int n);
            var buckets = new List<int>[n];
            for (int i = 0; i < n; i++) buckets[i] = [];
            for (int t = 0; t < label.Length; t++) buckets[label[t]].Add(t);
            // largest piece first, so Part 1 is the main body
            var order = Enumerable.Range(0, n).OrderByDescending(i => buckets[i].Count).ToList();
            for (int k = 0; k < order.Count; k++) groups.Add(($"{file.Name} part {k + 1}", mesh.Subset(buckets[order[k]])));
        }
        else groups.Add((file.PartNames.Count == 1 ? file.PartNames[0] : file.Name, mesh));

        int pi = 0;
        foreach (var (name, gm) in groups)
        {
            Step(0.3 + 0.65 * pi / groups.Count, groups.Count > 1 ? $"Processing part {pi + 1} of {groups.Count}…" : "Processing mesh…");
            pi++;
            var m = gm;
            if (weldTol > 0) m = MeshOps.Weld(m, weldTol);
            if (o.AutoRepair) m = Repair(m, res, groups.Count > 1 ? name : null, ct);
            if (m.TriCount == 0) continue;
            uint? uniform = null;
            if (m.FaceColors is { Length: > 0 } fc && fc.All(c => c == fc[0])) { uniform = fc[0]; m = m.WithColors(m.VertexColors, null); }
            res.Parts.Add(new ImportedPart { Name = name, Mesh = m, Color = uniform });
        }
        if (res.Parts.Count == 0) res.Messages.Add("That file contains no triangles.");
        if (res.Parts.Any(p => p.Mesh.HasColors) || res.Parts.Any(p => p.Color != null)) res.Messages.Add("Colours from the file were kept.");
        Step(1, "Done");
        return res;
    }

    /// <summary>The same steps as Mesh &gt; Repair with its defaults (merge, clean, orient, fill holes up to 250 edges).</summary>
    static TriMesh Repair(TriMesh m, MeshImportResult res, string? prefix, CancellationToken ct)
    {
        if (m.TriCount == 0) return m;
        string p = prefix == null ? "" : prefix + ": ";
        int before = m.TriCount;
        m = MeshOps.Weld(m, Math.Max(m.Size.Length * 1e-6, 1e-9)); ct.ThrowIfCancellationRequested();
        m = MeshOps.Clean(m).mesh; ct.ThrowIfCancellationRequested();
        var o = MeshOps.FixOrientation(m); m = o.mesh; ct.ThrowIfCancellationRequested();
        var h = MeshOps.FillHoles(m, 250); m = h.mesh;
        if (h.filled > 0) m = MeshOps.FixOrientation(m).mesh;
        var r = MeshAnalysis.Report(m);
        res.Messages.Add($"{p}Repaired: {before:#,0} to {m.TriCount:#,0} triangles, {o.flipped:#,0} re-oriented, {h.filled} hole(s) filled" + (r.Printable ? ", watertight." : "; still has problems."));
        return m;
    }
}
