using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Prism.HeadlessTests.Support.HeadlessApplication))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Prism.HeadlessTests.Support;

/// <summary>Avalonia supplies a dispatcher, but no window, renderer or desktop application is started.</summary>
public static class HeadlessApplication
{
    public static AppBuilder BuildAvaloniaApp()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Headless OCCT tests currently require the Windows native binaries.");

        var nativeDirectory = Environment.GetEnvironmentVariable("PRISM_NATIVE_DIR");
        if (string.IsNullOrWhiteSpace(nativeDirectory))
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Prism.slnx")))
                directory = directory.Parent;
            if (directory == null)
                throw new DirectoryNotFoundException("Cannot locate Prism.slnx; set PRISM_NATIVE_DIR to the native DLL directory.");
            nativeDirectory = Path.Combine(directory.FullName, "native", "bin");
        }

        nativeDirectory = Path.GetFullPath(nativeDirectory);
        if (!File.Exists(Path.Combine(nativeDirectory, "prism_kernel.dll")))
            throw new FileNotFoundException($"OCCT runtime missing from {nativeDirectory}. Build/stage native/bin or set PRISM_NATIVE_DIR.");
        if (!SetDllDirectory(nativeDirectory))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return AppBuilder.Configure<Application>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetDllDirectory(string path);
}
