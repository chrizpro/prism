using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Prism.Core;

namespace Prism.ViewModels;

public sealed partial class MainViewModel
{
    public ObservableCollection<SavedAppearance> SavedAppearances { get; } = new(AppSettings.Current.SavedAppearances);
    [ObservableProperty] public partial string SavedAppearanceName { get; set; } = "";
    [ObservableProperty] public partial SavedAppearance? SelectedSavedAppearance { get; set; }
    [ObservableProperty] public partial string AppearanceLibraryStatus { get; set; } = "";

    public void ApplyGenericAppearance() => ApplyFinishPreset(FinishPresets[0]);

    public void SaveCustomAppearance()
    {
        var source = TargetsForLook().FirstOrDefault();
        if (source == null) { AppearanceLibraryStatus = "Select a body first."; return; }
        var name = (SavedAppearanceName ?? "").Trim();
        if (name.Length == 0 || name.Length > 80) { AppearanceLibraryStatus = "Enter a name of 1–80 characters."; return; }
        if (SavedAppearances.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        { AppearanceLibraryStatus = "That name is already saved. Choose another name or remove the saved entry first."; return; }
        var recipe = SavedAppearance.Capture(name, source.Color, source.Look);
        SavedAppearances.Add(recipe);
        SelectedSavedAppearance = recipe;
        if (!PersistAppearances())
        {
            SavedAppearances.Remove(recipe); SelectedSavedAppearance = null;
            AppSettings.Current.SavedAppearances = SavedAppearances.ToList();
            AppearanceLibraryStatus = "Could not save appearances to this computer. Check available disk space and permissions."; return;
        }
        AppearanceLibraryStatus = "Saved on this computer. Uses the first selected body's appearance.";
    }

    public void ApplySavedAppearance()
    {
        if (SelectedSavedAppearance is not { } recipe) return;
        EditAppearance(o => o.WithColor(recipe.Color).WithLook(recipe.ApplyTo(o.Look)), "Appearance: " + recipe.Name);
    }

    public void RemoveSavedAppearance()
    {
        if (SelectedSavedAppearance is not { } recipe) return;
        SavedAppearances.Remove(recipe);
        SelectedSavedAppearance = null;
        if (!PersistAppearances())
        {
            SavedAppearances.Add(recipe); SelectedSavedAppearance = recipe;
            AppSettings.Current.SavedAppearances = SavedAppearances.ToList();
            AppearanceLibraryStatus = "Could not remove the saved entry from this computer."; return;
        }
        AppearanceLibraryStatus = "Removed from this computer. Applied appearances stay in their documents.";
    }

    bool PersistAppearances()
    {
        AppSettings.Current.SavedAppearances = SavedAppearances.ToList();
        return AppSettings.TrySave(false);
    }
}
