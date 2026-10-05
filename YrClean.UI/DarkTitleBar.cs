using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace YrClean.UI;

public static class DarkTitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    // Must be called once the window has an HWND, i.e. from SourceInitialized or later
    public static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        int useDarkMode = 1;
        DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref useDarkMode, sizeof(int));
    }
}
