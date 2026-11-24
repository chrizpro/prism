using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using Avalonia.Media;
using Prism;

internal sealed partial class Program
{
    private static async Task Main(string[] args)
    {
        // Do not touch Dispatcher.UIThread before the app is set up: it must pick up the browser dispatcher,
        // otherwise nothing posted to the UI thread ever runs and the first frame never paints.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Console.Error.WriteLine("Prism unhandled: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => Console.Error.WriteLine("Prism unobserved task: " + e.Exception);
        Prism.Rendering.GlViewportHosts.Factory = v => new BrowserGlHost(v);
        // packaging/Publish-Browser.ps1 writes the notices next to index.html.
        // A failure here only leaves the link on its GitHub fallback; it must never stop startup.
        try
        {
            var page = JSHost.GlobalThis.GetPropertyAsJSObject("location")?.GetPropertyAsString("href");
            if (page != null && Uri.TryCreate(new Uri(page), "THIRD-PARTY-NOTICES.txt", out var notices))
                Prism.Core.AppInfo.WebNoticesUrl = notices.ToString();
        }
        catch (Exception ex) { Console.Error.WriteLine("Prism: could not read the page address: " + ex.Message); }
        try
        {
            System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.ConsoleTraceListener(true));
            await BuildAvaloniaApp()
                .WithInterFont()
                .With(new FontManagerOptions { FontFallbacks = FallbackFonts })
                // Errors only: start-up raises about 110 harmless binding warnings (null paths before a tool opens), and in the browser's
                // interpreter formatting and printing each one is measurable start-up time (2026-11-23).
                .LogToTrace(Avalonia.Logging.LogEventLevel.Error)
                .StartBrowserAppAsync("out");
        }
        catch (Exception ex)
        {
            // The browser console only shows the outer AggregateException wrapper; log the whole chain.
            Console.Error.WriteLine("Prism failed to start: " + ex);
            throw;
        }
    }

    // Desktop falls back to system symbol fonts; the browser has none, so these stand in.
    // Order matters: Noto Symbols 2 matches Inter best, DejaVu fills box drawing, math and modifier letters, Noto Emoji the pictographs.
    // See docs/interface/browser/README.md#fonts.
    static readonly FontFallback[] FallbackFonts =
    [
        new() { FontFamily = new FontFamily("avares://Prism.Browser/Assets/Fonts#Noto Sans Symbols 2") },
        new() { FontFamily = new FontFamily("avares://Prism.Browser/Assets/Fonts#DejaVu Sans") },
        new() { FontFamily = new FontFamily("avares://Prism.Browser/Assets/Fonts#Noto Emoji") },
    ];

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
