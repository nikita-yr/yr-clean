namespace YrClean.Core.Services;

public static class AppPaths
{
    public static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YrClean");

    public static readonly string LocalDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YrClean");

    public static readonly string QuarantineDirectory = Path.Combine(LocalDataDirectory, "Quarantine");
}
