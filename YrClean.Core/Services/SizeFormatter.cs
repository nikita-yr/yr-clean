namespace YrClean.Core.Services;

public static class SizeFormatter
{
    public static string Format(long bytes)
    {
        const double kb = 1024;
        const double mb = kb * 1024;
        const double gb = mb * 1024;

        if (bytes >= gb)
            return $"{bytes / gb:F2} GB";
        if (bytes >= mb)
            return $"{bytes / mb:F2} MB";

        return $"{bytes / kb:F1} KB";
    }
}
