using System;
using System.IO;
using System.Linq;
using Prism.Core.Data;

namespace Prism.ViewModels;

public sealed partial class MainViewModel
{
    public bool ShowDataEditor => RightPanelContentId == "data";
    public bool IsDataPanelOpen => IsRightPanelOpen("data");

    /// <summary>Show or hide the Data panel. The first time it opens, it starts with one empty sheet to type or paste into.</summary>
    public void ToggleDataPanel()
    {
        if (IsRightPanelOpen("data")) { CloseRightPanel("data"); return; }
        OpenRightPanel("data");
        if (Session.DataSheets.Count == 0) Session.NewSheet();
        OnPropertyChanged(nameof(ShowDataEditor));
    }

    /// <summary>Pick a CSV, text or Excel file and add its sheets to the Data panel.</summary>
    public async void ImportTable()
    {
        if (PickOpenFile == null) return;
        var path = await PickOpenFile("Open a table", ["*.csv", "*.tsv", "*.txt", "*.xlsx", "*.xlsm"]);
        if (path == null) return;
        ImportTableFile(path);
    }

    public void ImportTableFile(string path)
    {
        try
        {
            var sheets = DataSheet.FromFile(path);
            foreach (var sheet in sheets) Session.AddSheet(sheet);
            OpenRightPanel("data");
            OnPropertyChanged(nameof(ShowDataEditor));
            Session.Say(sheets.Count == 1 ? $"Opened {Path.GetFileName(path)} as {sheets[0].Name}." : $"Opened {sheets.Count} sheets from {Path.GetFileName(path)}.");
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            Session.Say("Could not open that table: " + e.Message, true);
        }
    }
}
