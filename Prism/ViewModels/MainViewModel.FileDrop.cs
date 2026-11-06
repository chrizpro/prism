using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Prism.Core;

namespace Prism.ViewModels;

public sealed partial class MainViewModel
{
    public bool CanAcceptFileDrop => Documents.All(d => d.Session.Sketch == null && d.Session.ActiveCommand == null);
    public static bool IsSupportedDroppedFile(string path) => File.Exists(path) && OpenPatterns.Any(p =>
        Path.GetExtension(path).Equals(p[1..], StringComparison.OrdinalIgnoreCase));

    /// <summary>Load privately before adding a tab, so invalid drops cannot replace unsaved work.</summary>
    public int OpenDroppedFiles(IEnumerable<string> paths)
    {
        if (!CanAcceptFileDrop) { Session.Say("Finish or cancel the current edit before dropping files.", true); return 0; }
        int opened = 0, failed = 0, ignored = 0;
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!CanAcceptFileDrop) { Session.Say("Finish this import before dropping more files.", true); break; }
            if (!IsSupportedDroppedFile(path)) { ignored++; continue; }
            try
            {
                var session = new CadSession();
                if (Path.GetExtension(path).Equals(".prism", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".ourcad", StringComparison.OrdinalIgnoreCase)) session.Open(path);
                else session.ImportFile(path);
                var tab = new DocumentTab(session, ++_docCounter) { DisplayName = Path.GetFileNameWithoutExtension(path) };
                tab.Refresh(); Documents.Add(tab); ActiveDocument = tab;
                AddRecent(path); CloseHome(); opened++;
            }
            catch (Exception e) { failed++; Session.Say("Could not open " + Path.GetFileName(path) + ": " + e.Message, true); }
        }
        if (failed > 0 || ignored > 0)
            Session.Say($"Drop: {opened} opened, {failed} failed, {ignored} unsupported or unavailable. Use Prism, STEP, IGES, BREP, DXF or mesh files.", true);
        return opened;
    }
}
