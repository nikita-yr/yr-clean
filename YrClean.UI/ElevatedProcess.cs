using System.ComponentModel;
using System.Diagnostics;

namespace YrClean.UI;

public static class ElevatedProcess
{
    // True when running as administrator (app.manifest requests this), so no helper process is needed
    public static bool IsCurrentProcessElevated => Environment.IsPrivilegedProcess;

    // Relaunches this executable as administrator. Returns false if the user cancelled the UAC prompt.
    public static bool TryStartSelf(params string[] args)
    {
        var psi = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = true,
            Verb = "runas"
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        try
        {
            Process.Start(psi);
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
