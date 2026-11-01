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
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Console.Error.WriteLine("Prism unhandled: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => Console.Error.WriteLine("Prism unobserved task: " + e.Exception);
        try
        {
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) => Console.Error.WriteLine("Prism UI thread: " + e.Exception);
            await BuildAvaloniaApp()
                .WithInterFont()
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