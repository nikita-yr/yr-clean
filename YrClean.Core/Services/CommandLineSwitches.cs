namespace YrClean.Core.Services;

// Switches the UI executable understands; shared with the scheduled task definitions.
public static class CommandLineSwitches
{
    public const string AutoClean = "--auto-clean";
    public const string ElevatedClean = "--elevated-clean";

    // Monthly maintenance task: DISM /StartComponentCleanup
    public const string ComponentCleanup = "--component-cleanup";

    // DISM /RestoreHealth followed by sfc /scannow
    public const string Health = "--health";

    public const string StartupReport = "--startup-report";
    public const string DiskReport = "--disk-report";
}
