using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Prism.Kernel;

namespace Prism.Core;

/// <summary>One line of a body's recognised-features list: what it says, an icon, and the faces it stands for (selected when clicked).</summary>
public sealed record FeatureLine(string Text, string Icon, IReadOnlyList<int> Faces);

/// <summary>
/// The recognised features of a body, grouped by type and size the way a person would list them: "4 × Ø6.6 through (M6 clearance)",
/// "50 slots 5 × 15 mm in staggered rows…", "Fillets R1 (64 faces)". Shown under each solid in the Browser.
/// </summary>
public static class FeatureSummary
{
    static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public static List<FeatureLine> For(SceneObject body, IEnumerable<SceneObject>? document = null)
    {
        var lines = new List<FeatureLine>();
        var feats = FeatureFinder.Features(body);
        string Count(int n, string one) => n == 1 ? one : $"{n} × {one}";
        foreach (var g in feats.Where(f => f.Kind != FeatureKind.Face).GroupBy(f => f.Key))
        {
            var f = g.First(); int n = g.Count(); var faces = g.SelectMany(x => x.Faces).ToList();
            string? pattern = n > 1 ? FeatureFinder.PatternText(body, f) : null;
            string text = f.Kind switch
            {
                FeatureKind.Hole when FeatureFinder.HoleOf(body, f) is { } h =>
                    Count(n, $"Ø{N(2 * f.Round!.Radius)} {(h.Through ? "through" : $"× {N(h.Depth)} deep")}" + (h.Standard != null ? $" ({h.Standard})" : "")),
                FeatureKind.Boss when f.Round is { } r => Count(n, $"Ø{N(2 * r.Radius)} boss, {N(r.T1 - r.T0)} tall"),
                FeatureKind.Thread when FeatureFinder.ThreadOf(body, f) is { } t => Count(n, $"{t.Size ?? $"Ø{N(t.Major)} × {N(t.Pitch)}"} threaded hole, {N(t.Length)} long"),
                FeatureKind.Chamfer when FeatureFinder.ChamferOf(body, f) is { } c =>
                    $"Chamfer{(n > 1 ? "s" : "")} {(c.EqualSetbacks ? $"{N(c.SetbackA)} mm × 45°" : $"{N(Math.Min(c.SetbackA, c.SetbackB))} × {N(Math.Max(c.SetbackA, c.SetbackB))} mm")} ({n} face{(n == 1 ? "" : "s")})",
                FeatureKind.Fillet => $"Fillets {RadiusText(body, faces)} ({faces.Count} face{(faces.Count == 1 ? "" : "s")})",
                FeatureKind.Text when FeatureFinder.TextOf(body, f) is { } t =>
                    $"Text: {t.Letters} letters, {N(t.Height)} mm tall, {(t.Engraved ? "engraved" : "embossed")} {N(t.Depth)} mm",
                FeatureKind.Rib when FeatureFinder.RibOf(body, f) is { } r => Count(n, $"rib {N(r.Thickness)} mm thick, {N(r.Height)} tall"),
                _ => Count(n, f.Name.ToLowerInvariant()),
            };
            if (pattern != null && f.Kind is not (FeatureKind.Fillet or FeatureKind.Chamfer)) text += " — " + pattern[(pattern.IndexOf(" in ", StringComparison.Ordinal) + 1)..];
            lines.Add(new FeatureLine(char.ToUpperInvariant(text[0]) + text[1..], IconOf(f.Kind), faces));
        }
        // fillet chains of the same radius read as one line
        var fillets = lines.Where(l => l.Text.StartsWith("Fillets ")).GroupBy(l => l.Text[..l.Text.IndexOf(" (", StringComparison.Ordinal)]).ToList();
        foreach (var grp in fillets.Where(gr => gr.Count() > 1))
        {
            var all = grp.SelectMany(l => l.Faces).ToList();
            foreach (var l in grp.ToList()) lines.Remove(l);
            lines.Add(new FeatureLine($"{grp.Key} ({all.Count} faces)", "fillet", all));
        }
        if (document != null)
            foreach (var g in FastenerFinder.Find(document).Where(x => x.Holes.Any(h => h.Body.Id == body.Id)).GroupBy(x => x.Describe))
            {
                var faces = g.SelectMany(x => x.Holes.Where(h => h.Body.Id == body.Id).SelectMany(h => h.Hole.Faces)).Distinct().ToList();
                lines.Add(new FeatureLine($"Fasteners: {Count(g.Count(), g.Key)}", "pcb-hole", faces));
            }
        int Order(string icon) => icon switch { "movehole" => 0, "hole" => 1, "pcb-hole" => 2, "slot" => 3, "cylinder" => 4, "extrude" => 5, "text" => 6, "rib" => 7, "chamfer" => 8, "fillet" => 9, _ => 10 };
        return lines.OrderBy(l => Order(l.Icon)).ThenBy(l => l.Text, StringComparer.Ordinal).ToList();
    }

    static string RadiusText(SceneObject body, IEnumerable<int> faces)
    {
        var radii = new SortedSet<double>();
        foreach (int i in faces)
        {
            using var face = body.Shape.Sub(SubKind.Face, i); var info = Occ.FaceInfoOf(face);
            double r = info.Type == "Torus" ? info.R2 : info.R1; if (r > 0) radii.Add(Math.Round(r, 3));
        }
        return radii.Count == 0 ? "(variable)" : radii.Count == 1 ? $"R{N(radii.Min)}" : $"R{N(radii.Min)}–{N(radii.Max)}";
    }

    static string IconOf(FeatureKind k) => k switch
    {
        FeatureKind.Hole or FeatureKind.Counterbore or FeatureKind.Countersink => "movehole",
        FeatureKind.Thread => "hole", FeatureKind.Slot => "slot", FeatureKind.Boss => "cylinder", FeatureKind.Pocket => "extrude",
        FeatureKind.Text => "text", FeatureKind.Rib => "rib", FeatureKind.Chamfer => "chamfer", FeatureKind.Fillet => "fillet", _ => "box",
    };
}
