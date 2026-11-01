using System;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
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
        try
        {
            System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.ConsoleTraceListener(true));
            await BuildAvaloniaApp()
                .WithInterFont()
                .LogToTrace(Avalonia.Logging.LogEventLevel.Warning)
                .StartBrowserAppAsync("out");
        }
        catch (Exception ex)
        {
            // The browser console only shows the outer AggregateException wrapper; log the whole chain.
            Console.Error.WriteLine("Prism failed to start: " + ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
