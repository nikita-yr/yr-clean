using YrClean.Core.Models;
using YrClean.Core.Services;
using Xunit;

namespace YrClean.Tests;

public class LeftoverScannerTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private static readonly List<string> Tokens = LeftoverScanner.InstalledTokens(new[]
    {
        new InstalledProgram("Discord", null, "Discord Inc."),
        new InstalledProgram("IntelliJ IDEA 2024.1", @"C:\Program Files\JetBrains\IntelliJ IDEA 2024.1\", "JetBrains s.r.o."),
        new InstalledProgram("7-Zip 23.01 (x64)", @"C:\Program Files\7-Zip", "Igor Pavlov")
    });

    [Theory]
    [InlineData("discord", true)]              // program name
    [InlineData("JetBrains", true)]            // publisher
    [InlineData("7-Zip", true)]                // install folder
    [InlineData("IntelliJ IDEA 2024.1", true)]
    [InlineData("ab", true)]                   // too short to judge - never flagged
    [InlineData("SomeRemovedApp", false)]
    [InlineData("OldGameStudio", false)]
    public void Folder_is_known_when_it_overlaps_an_installed_program(string folder, bool known)
    {
        Assert.Equal(known, LeftoverScanner.IsKnownFolder(folder, Tokens));
    }

    [Fact]
    public void Task_action_is_read_whatever_the_trigger()
    {
        const string xml = """
            <Task xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Triggers><CalendarTrigger /></Triggers>
              <Actions><Exec><Command>C:\Gone\updater.exe</Command></Exec></Actions>
            </Task>
            """;

        var task = StartupManager.ParseTaskAction(xml);

        Assert.NotNull(task);
        Assert.False(task.Value.RunsAtStartup);
        Assert.Equal(@"C:\Gone\updater.exe", StartupManager.ExecutableFromCommand(task.Value.Command));
    }

    [Fact]
    public void Quarantining_a_leftover_folder_keeps_protected_and_recent_files()
    {
        var folder = Path.Combine(_temp.CacheRoot, "OldApp");
        var data = _temp.AddFile(@"OldApp\data.bin");
        var exe = _temp.AddFile(@"OldApp\uninstall.exe");
        var recent = _temp.AddFile(@"OldApp\recent.log", ageDays: 1);
        var quarantine = new QuarantineService(_temp.QuarantineRoot);

        // Same call QuarantineFolder makes, with a private quarantine instead of the real one
        var files = new FolderScanner().Scan(folder).Select(f => f.FullPath).ToList();
        var result = SafeDeleteService.DeleteFiles(files, new[] { folder }, new CleanSettings().MinAgeDays, null, quarantine);

        Assert.Equal(1, result.DeletedCount);
        Assert.False(File.Exists(data));
        Assert.True(File.Exists(exe));
        Assert.True(File.Exists(recent));
    }

    // Read-only against the real machine: must complete without throwing
    [Fact]
    public void Scan_works_on_this_machine()
    {
        Assert.NotNull(LeftoverScanner.Scan());
    }
}
