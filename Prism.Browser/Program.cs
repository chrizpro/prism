using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using Prism;

internal sealed partial class Program
{
    private static async Task Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Console.Error.WriteLine("Prism unhandled: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => Console.Error.WriteLine("Prism unobserved task: " + e.Exception);
        try
        {
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) => Console.Error.WriteLine("Prism UI thread: " + e.Exception);
            System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.ConsoleTraceListener(true));
            await BuildAvaloniaApp()
                .WithInterFont()
                .LogToTrace(Avalonia.Logging.LogEventLevel.Warning)
                .StartBrowserAppAsync("out");
            // Layout probe: report what the root view looks like a few seconds after startup.
            _ = Task.Delay(4000).ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var lt = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime;
                var v = lt?.MainView;
                Console.Error.WriteLine($"Prism probe: view={v?.GetType().Name} bounds={v?.Bounds} visible={v?.IsVisible} parent={v?.Parent?.GetType().Name}");
                if (v is Avalonia.Controls.Control c) Console.Error.WriteLine($"Prism probe: children={Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(c).Count()} dc={c.DataContext?.GetType().Name}");
            }));
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