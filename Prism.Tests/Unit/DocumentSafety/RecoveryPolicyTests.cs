using System.Text.Json;
using Prism.Core;
using Xunit;

namespace Prism.UnitTests.DocumentSafety;

/// <seealso href="https://github.com/chrizpro/prism/blob/develop/docs/interface/document-safety/README.md">Document safety.</seealso>
[Trait("Feature", "DocumentSafety")]
[Trait("Layer", "Unit")]
public class RecoveryPolicyTests
{
    [Fact(DisplayName = "SAV-U001 | Recovery defaults off; interval preferences clamp and survive JSON")]
    [Trait("TestId", "SAV-U001")]
    public void PreferencesDefaultOffAndClampInterval()
    {
        var settings = new AppSettings();
        Assert.False(settings.AutoRecoveryEnabled); Assert.Equal(5, settings.AutoRecoveryIntervalMinutes);
        settings.AutoRecoveryEnabled = true; settings.AutoRecoveryIntervalMinutes = 7;
        var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.True(loaded.AutoRecoveryEnabled); Assert.Equal(7, loaded.AutoRecoveryIntervalMinutes);
        settings.AutoRecoveryIntervalMinutes = 0; Assert.Equal(1, settings.AutoRecoveryIntervalMinutes);
        settings.AutoRecoveryIntervalMinutes = int.MaxValue; Assert.Equal(120, settings.AutoRecoveryIntervalMinutes);
    }

    [Fact(DisplayName = "SAV-U002 | Recovery schedule waits an interval, changes interval and stops when disabled")]
    [Trait("TestId", "SAV-U002")]
    public void SchedulingUsesTheConfiguredIntervalWithoutSleeping()
    {
        var schedule = new RecoverySchedule(); var start = DateTimeOffset.UtcNow;
        Assert.False(schedule.ShouldRun(start, false, 5));
        Assert.False(schedule.ShouldRun(start.AddHours(1), false, 5));
        Assert.False(schedule.ShouldRun(start, true, 5));
        Assert.False(schedule.ShouldRun(start.AddMinutes(4), true, 5));
        Assert.True(schedule.ShouldRun(start.AddMinutes(5), true, 5));
        Assert.False(schedule.ShouldRun(start.AddMinutes(5), true, 5));
        Assert.False(schedule.ShouldRun(start.AddMinutes(6), true, 2));
        Assert.True(schedule.ShouldRun(start.AddMinutes(8), true, 2));
        Assert.False(schedule.ShouldRun(start.AddMinutes(20), false, 2));
        Assert.False(schedule.ShouldRun(start.AddMinutes(21), true, 2));
        Assert.True(schedule.ShouldRun(start.AddMinutes(23), true, 2));
    }

    [Fact(DisplayName = "SAV-U003 | Atomic native writes preserve a locked prior file and remove temporary output")]
    [Trait("TestId", "SAV-U003")]
    public void AtomicReplacementPreservesPriorContentsOnFailure()
    {
        string folder = Path.Combine(Path.GetTempPath(), "prism-atomic-" + Guid.NewGuid());
        Directory.CreateDirectory(folder); string path = Path.Combine(folder, "part.prism");
        try
        {
            NativeDocumentFile.WriteAtomic(path, "previous complete file");
            if (OperatingSystem.IsWindows())
            {
                using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var error = Record.Exception(() => NativeDocumentFile.WriteAtomic(path, "replacement"));
                Assert.True(error is IOException or UnauthorizedAccessException);
                Assert.Equal("previous complete file", File.ReadAllText(path));
            }
            NativeDocumentFile.WriteAtomic(path, "new complete file");
            Assert.Equal("new complete file", File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
        }
        finally { Directory.Delete(folder, true); }
    }
}
