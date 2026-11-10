using System;
using System.IO;
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
        ApplyAccent();
    }

    public static void ApplyAccent()
    {
        if (Current is not { } app) return;
        string[] keys = ["Accent", "AccentDark", "AccentSoft", "CheckedBg", "CheckedLine", "AccentHover", "CurrentLine", "OnAccent"];
        var choice = Core.AppSettings.Current.AccentColor;
        if (choice is null or "Blue") { foreach (var key in keys) app.Resources.Remove(key); return; }
        string hex = choice switch { "Teal" => "#16897F", "Purple" => "#8556C8", "Orange" => "#BB631C", "Rose" => "#C14262", _ => "#2F7BF0" };
        var baseColor = Avalonia.Media.Color.Parse(hex);
        bool dark = app.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        Avalonia.Media.Color Mix(Avalonia.Media.Color a, Avalonia.Media.Color b, double t) => Avalonia.Media.Color.FromRgb(
            (byte)(a.R * (1 - t) + b.R * t), (byte)(a.G * (1 - t) + b.G * t), (byte)(a.B * (1 - t) + b.B * t));
        var white = Avalonia.Media.Colors.White;
        var surface = Avalonia.Media.Color.Parse(dark ? "#111A27" : "#FFFFFF");
        var accent = dark ? Mix(baseColor, white, .22) : baseColor;
        var colors = new[] { accent, Mix(baseColor, Avalonia.Media.Colors.Black, .16), Mix(surface, accent, dark ? .24 : .18),
            Mix(surface, accent, dark ? .32 : .24), accent, Mix(accent, white, .18), Mix(accent, white, .35), dark ? Avalonia.Media.Color.Parse("#0B1220") : white };
        for (int i = 0; i < keys.Length; i++) app.Resources[keys[i]] = new Avalonia.Media.SolidColorBrush(colors[i]);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ApplyTheme();
        Core.AppSettings.Changed += ApplyTheme;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainViewModel();
            ActualThemeVariantChanged += (_, _) => { ApplyAccent(); vm.Session.Invalidate(); };
            Core.AppSettings.Changed += () => vm.Session.Invalidate();      // grid style      // the 3D view paints its own background
            var window = new MainWindow { DataContext = vm };
            desktop.MainWindow = window;
            foreach (var path in desktop.Args ?? Array.Empty<string>())
            {
                if (Core.NativeDocumentFile.IsNative(path))
                    vm.OpenPath(path);
            }
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
