using Avalonia.Controls;
using Avalonia.Interactivity;
using Prism.Commands;
using Prism.ViewModels;

namespace Prism.Views;

public partial class StudiosPanel : UserControl
{
    public StudiosPanel() => InitializeComponent();
    MainViewModel? Vm => DataContext as MainViewModel;
    void OnClose(object? s, RoutedEventArgs e) => Vm?.OpenStudio(null);
    void OnRunChecks(object? s, RoutedEventArgs e) => Vm?.RunStudioChecks();
    void OnSelectIssue(object? s, SelectionChangedEventArgs e) { if (e.AddedItems.Count > 0 && e.AddedItems[0] is StudioIssue issue && Vm != null) Vm.SelectedStudioIssue = issue; }
    void OnLocateIssue(object? s, RoutedEventArgs e) { if (Vm?.SelectedStudioIssue is { } issue) Vm.LocateStudioIssue(issue); }
    void OnEditIssue(object? s, RoutedEventArgs e) { if (Vm?.SelectedStudioIssue is { } issue) Vm.EditStudioIssue(issue); }
    void OnLocateReference(object? s, RoutedEventArgs e) => Vm?.LocateStudioReference();
    void OnEditReference(object? s, RoutedEventArgs e) => Vm?.EditStudioReference();
    void OnCreatePlane(object? s, RoutedEventArgs e) => Vm?.Session.StartCommand(() => new ConstructionPlaneCommand());
    void OnAddParameter(object? s, RoutedEventArgs e) => Vm?.AddStudioParameter();
    void OnRemoveParameter(object? s, RoutedEventArgs e) { if ((s as Control)?.DataContext is ParameterDraft p) { Vm?.DocumentParameterRows.Remove(p); Vm?.PreviewDocumentParameters(); } }
    void OnPreviewParameters(object? s, RoutedEventArgs e) => Vm?.PreviewDocumentParameters();
    void OnAnimateParameter(object? s, RoutedEventArgs e) => Vm?.AnimateSelectedParameter();
    void OnApplyParameters(object? s, RoutedEventArgs e) => Vm?.ApplyStudioParameters();
    void OnRevertParameters(object? s, RoutedEventArgs e) => Vm?.RevertStudioParameters();
    void OnApplyFeature(object? s, RoutedEventArgs e) => Vm?.ApplyStudioFeatureValues();
    void OnOpenFeatureEditor(object? s, RoutedEventArgs e) { if (Vm?.SelectedStudioFeature is { } feature) Vm.Session.EditFeature(feature.Node); }
    void OnOpenDependency(object? s, RoutedEventArgs e) { if ((s as Control)?.DataContext is FeatureChoice feature) Vm?.Session.EditFeature(feature.Node); }
    void OnSelectParameter(object? s, RoutedEventArgs e) { if ((s as Control)?.DataContext is ParameterDraft row && Vm != null) { Vm.ParameterSearch = ""; Vm.SelectedParameterDraft = row; } }
    void OnToggleAddParameter(object? s, RoutedEventArgs e) { if (Vm != null) Vm.AddingParameter = !Vm.AddingParameter; }
    void OnRemoveSelectedParameter(object? s, RoutedEventArgs e) { if (Vm?.SelectedParameterDraft is { } row) { Vm.DocumentParameterRows.Remove(row); Vm.RefreshParameterTable(); } }
}
