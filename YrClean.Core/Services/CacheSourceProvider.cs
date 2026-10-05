using YrClean.Core.Models;

namespace YrClean.Core.Services;

public static class CacheSourceProvider
{
    public static List<CacheSource> GetKnownSources()
    {
        return new List<CacheSource>
        {
            new() { Name = "User Temp", PathTemplate = "%TEMP%", Category = "System" },
            new() { Name = "System Temp", PathTemplate = "%SystemRoot%\\Temp", Category = "System" },
            new() { Name = "Prefetch", PathTemplate = "%SystemRoot%\\Prefetch", Category = "System" },

            new() { Name = "NVIDIA Shader Cache (GL)", PathTemplate = "%LocalAppData%\\NVIDIA\\GLCache", Category = "GPU" },
            new() { Name = "NVIDIA Shader Cache (DX)", PathTemplate = "%LocalAppData%\\NVIDIA\\DXCache", Category = "GPU" },
            new() { Name = "AMD Shader Cache (DX)", PathTemplate = "%LocalAppData%\\AMD\\DxCache", Category = "GPU" },
            new() { Name = "AMD Shader Cache (GL)", PathTemplate = "%LocalAppData%\\AMD\\GLCache", Category = "GPU" },
            new() { Name = "AMD Shader Cache (Vulkan)", PathTemplate = "%LocalAppData%\\AMD\\VkCache", Category = "GPU" },

            new() { Name = "Discord Cache", PathTemplate = "%AppData%\\discord\\Cache", Category = "Messengers" },
            new() { Name = "Discord Code Cache", PathTemplate = "%AppData%\\discord\\Code Cache", Category = "Messengers" },

            new() { Name = "Chrome Cache", PathTemplate = "%LocalAppData%\\Google\\Chrome\\User Data\\Default\\Cache\\Cache_Data", Category = "Browsers" },
            new() { Name = "Edge Cache", PathTemplate = "%LocalAppData%\\Microsoft\\Edge\\User Data\\Default\\Cache\\Cache_Data", Category = "Browsers" },
            new() { Name = "Firefox Cache", PathTemplate = "%LocalAppData%\\Mozilla\\Firefox\\Profiles\\*\\cache2\\entries", Category = "Browsers" },

            new() { Name = "Steam Browser Cache", PathTemplate = "%LocalAppData%\\Steam\\htmlcache\\Cache", Category = "Games" },
        };
    }
}