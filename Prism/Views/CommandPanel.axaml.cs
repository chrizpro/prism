using System.ComponentModel;
using System.Linq;
using Avalonia.VisualTree;
using Avalonia;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Prism.Commands;
using Prism.ViewModels;
using Prism.Core;

namespace Prism.Views;

public partial class CommandPanel : UserControl
{
    MainViewModel? _observed;
    Command? _command;
    Vector _returnOffset;
    bool _showingStep;
    public CommandPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
        AttachedToVisualTree += (_, _) => Attach();
        DetachedFromVisualTree += (_, _) => Detach();
    }
    MainViewModel? Vm => DataContext as MainViewModel;
    internal CadSession? Session => Vm?.Session;

    void Detach()
    {
        if (_observed != null) _observed.PropertyChanged -= OnViewModelChanged;
        _observed = null;
        if(_command!=null)_command.PropertyChanged-=OnCommandChanged;
        _command=null;
    }

    void Attach()
    {
        Detach(); _observed = Vm;
        if (_observed != null) _observed.PropertyChanged += OnViewModelChanged;
        ObserveCommand();
    }

    void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.ActiveCommand)) return;
        ObserveCommand();
        PanelScroll.Offset = default;
    }

    void ObserveCommand()
    {
        if(_command!=null)_command.PropertyChanged-=OnCommandChanged;
        _command=Vm?.ActiveCommand;
        _showingStep=_command?.HasPanelStep==true;
        if(_command!=null)_command.PropertyChanged+=OnCommandChanged;
    }
    void OnCommandChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName!=nameof(Command.HasPanelStep))return;
        bool showing=_command?.HasPanelStep==true;if(showing==_showingStep)return;_showingStep=showing;
        if(showing){_returnOffset=PanelScroll.Offset;PanelScroll.Offset=default;}
        else Dispatcher.UIThread.Post(()=>PanelScroll.Offset=_returnOffset,DispatcherPriority.Loaded);
    }

    void OnOk(object? sender, RoutedEventArgs e) => Vm?.Session.AcceptCommandPanel();
    void OnHelp(object? sender, RoutedEventArgs e)
    {
        if (sender is Control anchor)
            this.FindAncestorOfType<MainView>()?.GetVisualDescendants().OfType<Ribbon.RibbonView>().FirstOrDefault()
                ?.ShowExample(anchor, Help.ToolHelp.ForCommand(Vm?.ActiveCommand)?.Icon);
    }
    void OnCancel(object? sender, RoutedEventArgs e) => Vm?.Session.CancelCommandPanel();
    void OnActionClick(object? sender, RoutedEventArgs e) { if ((sender as Control)?.DataContext is ActionParam a) a.Run(); }
    void OnGizmo(object? sender,RoutedEventArgs e)=>Vm?.OpenGizmoPanel();

}
