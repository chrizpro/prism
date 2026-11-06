using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Prism.ViewModels;

namespace Prism.Views;

public partial class MainWindow
{
    void InitializeFileDrop()
    {
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnFileDragOver, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnFileDrop, RoutingStrategies.Bubble, handledEventsToo: true);
    }
    static IEnumerable<string> DroppedPaths(DragEventArgs e) =>
        e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>() ?? e.Data.GetFileNames() ?? [];
    void OnFileDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.None;
        try
        {
            if (!CloseReviewPending && DataContext is MainViewModel { CanAcceptFileDrop: true } && DroppedPaths(e).Any(MainViewModel.IsSupportedDroppedFile))
                e.DragEffects = DragDropEffects.Copy;
        }
        catch (Exception) { /* A virtual/unavailable shell item is not a file drop Prism can open. */ }
        e.Handled = true;
    }
    void OnFileDrop(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.None; e.Handled = true;
        if (DataContext is not MainViewModel vm) return;
        if (CloseReviewPending) { vm.Session.Say("Complete or cancel the close review before dropping files.", true); return; }
        try { if (vm.OpenDroppedFiles(DroppedPaths(e).ToArray()) > 0) e.DragEffects = DragDropEffects.Copy; }
        catch (Exception ex) { vm.Session.Say("Could not read dropped files: " + ex.Message, true); }
    }
}
