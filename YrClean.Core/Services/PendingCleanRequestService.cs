using System.Text.Json;
using YrClean.Core.Models;

namespace YrClean.Core.Services;

public static class PendingCleanRequestService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string Save(PendingCleanRequest request)
    {
        var directory = Path.Combine(Path.GetTempPath(), "YrClean");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"pending-clean-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(request, JsonOptions));
        return path;
    }

    public static PendingCleanRequest Load(string path)
    {
        var request = JsonSerializer.Deserialize<PendingCleanRequest>(File.ReadAllText(path));
        return request ?? throw new InvalidDataException("The pending cleanup request is empty.");
    }

    public static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The elevated helper has already consumed the request.
        }
    }
}
