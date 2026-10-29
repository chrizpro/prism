using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Prism.ViewModels;
using Prism.Views;

namespace Prism;

public partial class App : Application
{
    public override void Initialize()
    {
        Core.BrandMigration.CopyLegacySettings();
        Name = "Prism";
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>Applies the Theme setting (Dark, Light, or follow Windows).</summary>
    public static void ApplyTheme()
    {
        if (Current is not { } a) return;
        a.RequestedThemeVariant = Core.AppSettings.Current.Theme switch
        {
            "Light" => Avalonia.Styling.ThemeVariant.Light,
            "System" => Avalonia.Styling.ThemeVariant.Default,
            _ => Avalonia.Styling.ThemeVariant.Dark,
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ApplyTheme();
        Core.AppSettings.Changed += ApplyTheme;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainViewModel();
            ActualThemeVariantChanged += (_, _) => vm.Session.Invalidate();
            Core.AppSettings.Changed += () => vm.Session.Invalidate();      // grid style      // the 3D view paints its own background
            var window = new MainWindow { DataContext = vm };
            desktop.MainWindow = window;
            var api = new Automation.AutomationApi(vm, window);
            vm.InitAssistant(new Automation.AssistantService(api));
            var server = Automation.AutomationServer.TryStart(api);
            desktop.Exit += (_, _) => server?.Dispose();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
            single.MainView = new MainView { DataContext = new MainViewModel() };

        base.OnFrameworkInitializationCompleted();
    }
}
