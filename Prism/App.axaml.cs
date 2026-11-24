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
    /// <summary>The geometry kernel's libraries load on a worker thread from the first moment, alongside the window system's own start (startup.log 2026-11-23: 1.3 s).</summary>
    static System.Threading.Tasks.Task? _kernelWarmUp;

    /// <summary>Start loading the geometry kernel on a worker thread. The desktop calls this on its first line, before the window system starts.</summary>
    public static void StartKernelWarmUp() =>
        _kernelWarmUp ??= System.Threading.Tasks.Task.Run(() => { using var warm = Kernel.Occ.Box(1, 1, 1); _ = warm.Tessellate(); });

    public override void Initialize()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime) StartKernelWarmUp();
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
            Core.StartupLog.Mark("Runtime and window system");
            // The start-up card shows first, then each stage is built with a pause in between so the card can draw its progress
            // (the main view model and window must be made on this UI thread). PRISM_SPLASH=0 or --no-splash skips the card.
            bool splash = Environment.GetEnvironmentVariable("PRISM_SPLASH") != "0" && Array.IndexOf(desktop.Args ?? [], "--no-splash") < 0;
            var card = splash ? new SplashWindow() : null;
            if (card != null) desktop.MainWindow = card;
            if (card == null) { StartDesktop(desktop, null); }
            else Avalonia.Threading.Dispatcher.UIThread.Post(() => StartDesktop(desktop, card), Avalonia.Threading.DispatcherPriority.Background);
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
            single.MainView = new MainView { DataContext = new MainViewModel() };

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Let the start-up card draw the stage it was just given before the next stage holds the UI thread.</summary>
    static async System.Threading.Tasks.Task Breathe(SplashWindow? card, string stage, double fraction)
    {
        if (card == null) return;
        card.SetStage(stage, fraction);
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Render);
        await System.Threading.Tasks.Task.Delay(15);
    }

    /// <summary>Build the main window stage by stage, moving the start-up card along, then swap the card for the window.</summary>
    async void StartDesktop(IClassicDesktopStyleApplicationLifetime desktop, SplashWindow? card)
    {
        try
        {
            await Breathe(card, "Loading geometry kernel…", 0.04);
            await (_kernelWarmUp ?? System.Threading.Tasks.Task.Run(() => { using var warm = Kernel.Occ.Box(1, 1, 1); _ = warm.Tessellate(); }));
            Core.StartupLog.Mark("Geometry kernel (rest of the warm-up)");

            await Breathe(card, "Preparing the workspace…", 0.15);
            var vm = new MainViewModel();
            ActualThemeVariantChanged += (_, _) => { ApplyAccent(); vm.Session.Invalidate(); };
            Core.AppSettings.Changed += () => vm.Session.Invalidate();      // grid style      // the 3D view paints its own background
            Core.StartupLog.Mark("Workspace (main view model)");

            await Breathe(card, "Building the window…", 0.35);
            var window = new MainWindow { DataContext = vm };
            Core.StartupLog.Mark("Main window");
            if (Environment.GetEnvironmentVariable("PRISM_CHECK_UPDATES") != "0" && window.Content is MainView view)
            {
                async void CheckOnce(object? sender, EventArgs args)
                {
                    window.Opened -= CheckOnce;
                    await view.CheckForStartupUpdateAsync(window);
                }
                window.Opened += CheckOnce;
            }

            await Breathe(card, "Opening documents…", 0.8);
            foreach (var path in desktop.Args ?? Array.Empty<string>())
            {
                if (Core.NativeDocumentFile.IsNative(path))
                    vm.OpenPath(path);
            }
            Core.StartupLog.Mark("Documents");

            await Breathe(card, "Opening Prism…", 1);
            desktop.MainWindow = window;
            // The assistant and the automation server are not needed to see the window (startup.log 2026-11-23: 2.6 s in that stage), so they
            // start right after it has opened; scripts and MCP connect a moment later.
            async void AfterOpen(object? sender, EventArgs args)
            {
                window.Opened -= AfterOpen;
                Core.StartupLog.Mark("Main window shown");
                await System.Threading.Tasks.Task.Delay(50);
                Core.StartupLog.Mark("UI thread free again after opening");
                var api = new Automation.AutomationApi(vm, window);
                vm.InitAssistant(new Automation.AssistantService(api));
                var server = Automation.AutomationServer.TryStart(api);
                desktop.Exit += (_, _) => server?.Dispose();
                Core.StartupLog.Mark("Assistant and automation (after the window opened)");
                Core.StartupLog.Save();
            }
            window.Opened += AfterOpen;
            window.Show();
            card?.Close();
        }
        catch (Exception ex)
        {
            // a failure here would otherwise leave only the card on screen: record it like any crash and close
            try { Core.PendingCrashStore.Local.Save(Core.CrashDiagnostic.Capture(ex)); } catch { }
            card?.Close();
            throw;
        }
    }
}
