using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Core.Data;

namespace Prism.Core;

/// <summary>The document's tables: spreadsheets whose rows tools can read as points, positions or motion, and that are saved with the file.</summary>
public sealed partial class CadSession
{
    public List<DataSheet> DataSheets { get; } = [];
    public event Action? DataChanged;

    /// <summary>Tell the panels the tables changed. Tables are saved with the document but are not an undo step.</summary>
    public void RaiseData() { Dirty = true; DataChanged?.Invoke(); }

    public DataSheet AddSheet(DataSheet sheet)
    {
        string name = string.IsNullOrWhiteSpace(sheet.Name) ? "Sheet" : sheet.Name.Trim().TrimStart('@');
        string unique = name;
        for (int n = 2; DataSheets.Any(s => s.Name.Equals(unique, StringComparison.OrdinalIgnoreCase)); n++) unique = $"{name} {n}";
        sheet.Name = unique;
        DataSheets.Add(sheet);
        RaiseData();
        return sheet;
    }

    public DataSheet NewSheet() => AddSheet(new DataSheet { Name = "Sheet" });

    public void RemoveSheet(DataSheet sheet) { if (DataSheets.Remove(sheet)) RaiseData(); }

    public void RenameSheet(DataSheet sheet, string name)
    {
        name = name.Trim().TrimStart('@');
        if (name.Length == 0 || DataSheets.Any(s => !ReferenceEquals(s, sheet) && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { Say("Each sheet needs its own name.", true); return; }
        sheet.Name = name; RaiseData();
    }
}
