namespace YrClean.Core.Services;

// Folders no cleanup may ever reach into, whatever a rule (built-in or downloaded) says.
// The exceptions are a closed list compiled into the app, so external rules cannot extend it.
public static class ProtectedLocations
{
    private static readonly string Windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static readonly string[] Protected = new[]
    {
        Windows,
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetEnvironmentVariable("ProgramW6432") ?? "",
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        UserProfile.Length > 0 ? Path.Combine(UserProfile, "Downloads") : "",
        AppPaths.DataDirectory,
        AppPaths.LocalDataDirectory
    }.Where(p => p.Length > 0).Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    // Cache-like folders inside the Windows directory that are safe to empty.
    private static readonly string[] AllowedInsideProtected = new[]
    {
        "Temp",
        "Prefetch",
        "Minidump",
        "LiveKernelReports",
        @"SoftwareDistribution\Download",
        @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"
    }.Select(sub => Normalize(Path.Combine(Windows, sub))).ToArray();

    // True when the path sits inside a protected folder and not inside one of the allowed exceptions.
    public static bool IsForbidden(string path)
    {
        var full = Normalize(path);
        return Protected.Any(p => IsUnder(full, p)) && !AllowedInsideProtected.Any(a => IsUnder(full, a));
    }

    // A cleanup root is also forbidden when it is a drive root or contains a protected folder,
    // e.g. "C:\" or "C:\Users\me" - cleaning it would sweep the protected folder along.
    public static bool IsForbiddenRoot(string root)
    {
        var full = Normalize(root);
        return IsForbidden(full)
            || string.Equals(Path.GetPathRoot(full), full, StringComparison.OrdinalIgnoreCase)
            || Protected.Any(p => IsUnder(p, full));
    }

    // True when the path or any existing folder above it (up to and including stopAt, or the
    // drive root when stopAt is null) is a symlink/junction that could redirect the operation.
    public static bool HasReparsePoint(string path, string? stopAt = null)
    {
        var stop = stopAt is null ? null : Normalize(stopAt);
        for (var current = Normalize(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            var attributes = File.Exists(current) || Directory.Exists(current)
                ? File.GetAttributes(current)
                : default;
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
                return true;
            if (stop is not null && string.Equals(current, stop, StringComparison.OrdinalIgnoreCase))
                break;
        }

        return false;
    }

    public static bool IsUnder(string path, string folder) =>
        path.Equals(folder, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(
            Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    // Keeps the separator on drive roots ("C:\"), where trimming it would change the meaning.
    public static string Normalize(string path) =>
        path.Length == 0 ? path : Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
