namespace YrClean.Core.Services;

public record RuleUpdateResult(bool Ok, int RuleCount, string? Error);

// Fetches rule files on the user's request. Downloads are HTTPS-only, size-capped and never
// followed across redirects; nothing is saved unless the whole file validates. Saved rules are
// loaded through RuleEngine like any other file, so every safety check still applies to them.
public static class RuleUpdater
{
    public const string RulesUrl =
        "https://raw.githubusercontent.com/nikita-yr/yr-clean/main/YrClean.Core/Rules/builtin.json";

    // Fetched straight from its authors (CC BY-SA 4.0) into the user's rules folder; not redistributed with YrClean
    public const string Winapp2Url =
        "https://raw.githubusercontent.com/MoscaDotTo/Winapp2/master/Winapp2.ini";

    private const int MaxRulesBytes = 1024 * 1024;
    private const int MaxWinapp2Bytes = 8 * 1024 * 1024;

    public static async Task<RuleUpdateResult> UpdateRulesAsync(CancellationToken cancellation = default)
    {
        try
        {
            var json = await DownloadAsync(RulesUrl, MaxRulesBytes, cancellation);
            return SaveRules(json, RuleEngine.UserRulesDirectory);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException or UnauthorizedAccessException)
        {
            return new RuleUpdateResult(false, 0, ex.Message);
        }
    }

    public static async Task<RuleUpdateResult> DownloadWinapp2Async(CancellationToken cancellation = default)
    {
        try
        {
            var text = await DownloadAsync(Winapp2Url, MaxWinapp2Bytes, cancellation);
            return SaveWinapp2(text, RuleEngine.UserRulesDirectory);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException or UnauthorizedAccessException)
        {
            return new RuleUpdateResult(false, 0, ex.Message);
        }
    }

    // Strict on purpose: a downloaded rule base with even one rejected rule is not installed.
    public static RuleUpdateResult SaveRules(string json, string rulesDirectory)
    {
        var rules = RuleEngine.Parse(json, out var errors);
        if (errors.Count > 0)
            return new RuleUpdateResult(false, 0, errors[0]);
        if (rules.Count == 0)
            return new RuleUpdateResult(false, 0, "The file contains no rules.");

        WriteAtomically(Path.Combine(rulesDirectory, RuleEngine.RemoteRulesFileName), json);
        return new RuleUpdateResult(true, rules.Count, null);
    }

    // Counts only the entries that survive validation; the rest are ignored at load time anyway.
    public static RuleUpdateResult SaveWinapp2(string text, string rulesDirectory)
    {
        var usable = Winapp2Parser.Parse(text).Count(rule => RuleEngine.Validate(rule) is null);
        if (usable == 0)
            return new RuleUpdateResult(false, 0, "The file does not look like a winapp2.ini.");

        WriteAtomically(Path.Combine(rulesDirectory, RuleEngine.Winapp2FileName), text);
        return new RuleUpdateResult(true, usable, null);
    }

    public static bool IsAllowedUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static async Task<string> DownloadAsync(string url, int maxBytes, CancellationToken cancellation)
    {
        if (!IsAllowedUrl(url))
            throw new InvalidDataException("Only HTTPS downloads are allowed.");

        // No redirects: a redirect could quietly downgrade to http or leave the expected host
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("YrClean");

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"The server answered {(int)response.StatusCode}.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellation)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                throw new InvalidDataException("The file is larger than expected.");
            buffer.Write(chunk, 0, read);
        }

        using var reader = new StreamReader(new MemoryStream(buffer.ToArray()));
        return await reader.ReadToEndAsync(cancellation);
    }

    // A failed write must never leave a half-written rule file behind
    private static void WriteAtomically(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }
}
