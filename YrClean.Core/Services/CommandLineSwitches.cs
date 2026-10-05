namespace YrClean.Core.Services;

// Switches the UI executable understands; shared with the scheduled task definition.
public static class CommandLineSwitches
{
    public const string AutoClean = "--auto-clean";
    public const string ElevatedClean = "--elevated-clean";
}
