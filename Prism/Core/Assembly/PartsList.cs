using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Prism.Core.Assembly;

/// <summary>One line of an assembly's parts list: a distinct part and how many of it there are.</summary>
public sealed record PartsRow(string Name, string PartNumber, string Description, int Quantity, string Material, double? UnitMassGrams, double? UnitCost, string Supplier)
{
    public double? TotalMassGrams => UnitMassGrams * Quantity;
    public double? TotalCost => UnitCost * Quantity;
}

/// <summary>The parts list (bill of materials) of an assembly, built from each component's part info and geometry.</summary>
public static class PartsList
{
    public static List<PartsRow> Build(CadSession s)
    {
        var groups = new Dictionary<string, List<SceneObject>>();
        foreach (var comp in s.Components)
        {
            var ci = comp.Component!;
            // components of the same part number (or, without one, the same part name) are one line
            string key = ci.Info is { PartNumber.Length: > 0 } info ? "#" + info.PartNumber : "@" + ci.PartName;
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = [];
            list.Add(comp);
        }
        var rows = new List<PartsRow>();
        foreach (var (_, list) in groups)
        {
            var first = list[0]; var ci = first.Component!; var info = ci.Info;
            var material = first.Material;
            double? mass = null;
            if (material != null) { try { mass = first.Shape.Props().Volume * material.Density / 1000.0; } catch (Exception) { } }
            rows.Add(new PartsRow(ci.PartName, info?.PartNumber ?? "", info?.Description ?? "", list.Count, material?.Name ?? info?.Material ?? "", mass, info?.Cost, info?.Supplier ?? ""));
        }
        return [.. rows.OrderBy(r => r.PartNumber.Length == 0).ThenBy(r => r.PartNumber, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)];
    }

    static string Csv(string v) => v.IndexOfAny([',', '"', '\n']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    static string Num(double? v) => v == null ? "" : v.Value.ToString("0.###", CultureInfo.InvariantCulture);

    public static string ToCsv(IReadOnlyList<PartsRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Item,Part number,Name,Description,Quantity,Material,Unit mass (g),Total mass (g),Unit cost,Total cost,Supplier");
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            sb.AppendLine(string.Join(",", (i + 1).ToString(CultureInfo.InvariantCulture), Csv(r.PartNumber), Csv(r.Name), Csv(r.Description), r.Quantity.ToString(CultureInfo.InvariantCulture), Csv(r.Material), Num(r.UnitMassGrams), Num(r.TotalMassGrams), Num(r.UnitCost), Num(r.TotalCost), Csv(r.Supplier)));
        }
        return sb.ToString();
    }
}
