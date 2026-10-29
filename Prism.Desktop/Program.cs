using System;
using Avalonia;

namespace Prism.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (Array.IndexOf(args, "--verify-runtime") >= 0)
        {
            using var shape = Kernel.Occ.Box(10, 10, 10);
            if (!shape.IsValid) throw new InvalidOperationException("Geometry runtime verification failed.");
            var mesh = shape.Tessellate();
            var soup = new float[mesh.TriangleCount * 9];
            for (int i = 0; i < mesh.TriangleCount * 3; i++)
            {
                int vertex = (int)mesh.Indices[i];
                soup[i * 3] = mesh.Positions[vertex * 3];
                soup[i * 3 + 1] = mesh.Positions[vertex * 3 + 1];
                soup[i * 3 + 2] = mesh.Positions[vertex * 3 + 2];
            }
            using var volume = Kernel.FemMesh.FromSurface(soup, 5);
            if (volume.TetCount == 0) throw new InvalidOperationException("Simulation runtime verification failed.");
            Console.WriteLine("Prism geometry and simulation runtimes verified.");
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
