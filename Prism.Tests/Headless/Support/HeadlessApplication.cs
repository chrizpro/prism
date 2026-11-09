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
        // Tests can change and save preferences; they must never write the user's settings.json.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PRISM_SETTINGS_FILE")))
            Environment.SetEnvironmentVariable("PRISM_SETTINGS_FILE", Path.Combine(Path.GetTempPath(), $"prism-headless-settings-{Environment.ProcessId}.json"));
        if (!OperatingSystem.IsWindows())
            return BuildWithUnixNativeDirectory();

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

    /// <summary>
    /// Non-Windows runs are a developer convenience, not the reference platform: PRISM_NATIVE_DIR must name a directory
    /// holding a locally built libprism_kernel (and optionally libprism_sim). Preloading by full path lets the
    /// DllImport("prism_kernel") probes resolve to the already loaded library.
    /// </summary>
    static AppBuilder BuildWithUnixNativeDirectory()
    {
        var nativeDirectory = Environment.GetEnvironmentVariable("PRISM_NATIVE_DIR");
        if (string.IsNullOrWhiteSpace(nativeDirectory))
            throw new PlatformNotSupportedException("Headless OCCT tests on this platform need PRISM_NATIVE_DIR set to a directory containing a locally built libprism_kernel.");
        var extension = OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        var kernel = Path.Combine(Path.GetFullPath(nativeDirectory), "libprism_kernel" + extension);
        if (!File.Exists(kernel))
            throw new FileNotFoundException($"OCCT runtime missing: {kernel}.");
        NativeLibrary.Load(kernel);
        var sim = Path.Combine(Path.GetFullPath(nativeDirectory), "libprism_sim" + extension);
        if (File.Exists(sim))
            NativeLibrary.Load(sim);
        return AppBuilder.Configure<Application>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetDllDirectory(string path);
}
