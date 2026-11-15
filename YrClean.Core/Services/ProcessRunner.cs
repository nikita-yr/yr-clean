using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace YrClean.Core.Services;

// Runs Windows' own command-line tools (DISM, sfc, ...) and streams their output line by line.
// The app is already elevated, so the tools inherit administrator rights without another prompt.
public static class ProcessRunner
{
    private static readonly Regex PercentPattern = new(@"(\d{1,3})(?:[.,]\d+)?\s*%", RegexOptions.Compiled);

    static ProcessRunner() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    // Console tools write in the OEM code page; a few (sfc) write UTF-16 when redirected.
    public static Encoding OemEncoding => Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);

    // Full path, so an elevated run can never pick up a look-alike from the PATH or current folder
    public static string System32(string fileName) => Path.Combine(Environment.SystemDirectory, fileName);

    public static int Run(
        string exePath,
        IEnumerable<string> args,
        Action<string>? onLine = null,
        Encoding? encoding = null,
        CancellationToken cancellation = default)
    {
        var psi = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = encoding ?? OemEncoding,
            StandardErrorEncoding = encoding ?? OemEncoding
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {exePath}");
        using var kill = cancellation.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });

        var errors = Task.Run(() => Pump(process.StandardError, onLine));
        Pump(process.StandardOutput, onLine);
        errors.Wait();
        process.WaitForExit();
        cancellation.ThrowIfCancellationRequested();
        return process.ExitCode;
    }

    // Progress bars are redrawn with a bare carriage return, so both \r and \n end a line
    private static void Pump(StreamReader reader, Action<string>? onLine)
    {
        var line = new StringBuilder();
        int next;
        while ((next = reader.Read()) >= 0)
        {
            var c = (char)next;
            if (c is '\r' or '\n')
            {
                var text = line.ToString().Trim();
                line.Clear();
                if (text.Length > 0)
                    onLine?.Invoke(text);
            }
            else if (c != '\0')
            {
                line.Append(c);
            }
        }

        if (line.ToString().Trim() is { Length: > 0 } rest)
            onLine?.Invoke(rest);
    }

    // Pulls "42" out of "[====  42.0%  ====]" or "Verification 42% complete", in any UI language
    public static int? TryParsePercent(string line)
    {
        var match = PercentPattern.Match(line);
        return match.Success && int.TryParse(match.Groups[1].Value, out var percent) && percent <= 100
            ? percent
            : null;
    }
}
