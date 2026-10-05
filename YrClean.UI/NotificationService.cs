using System.Windows.Forms;
using System.Windows.Threading;

namespace YrClean.UI;

public static class NotificationService
{
    public static void ShowBalloonBlocking(string title, string message)
    {
        using var icon = CreateIcon(title, message);

        using var closeTimer = new System.Windows.Forms.Timer { Interval = 6000 };
        closeTimer.Tick += (_, _) =>
        {
            closeTimer.Stop();
            icon.Visible = false;
            Application.ExitThread();
        };

        icon.ShowBalloonTip(5000);
        closeTimer.Start();
        Application.Run();
    }

    public static void ShowBalloon(string title, string message, int durationMs = 4000)
    {
        var icon = CreateIcon(title, message);
        icon.ShowBalloonTip(durationMs);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs + 1000) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            icon.Visible = false;
            icon.Dispose();
        };
        timer.Start();
    }

    private static NotifyIcon CreateIcon(string title, string message) => new()
    {
        Icon = System.Drawing.SystemIcons.Information,
        Visible = true,
        BalloonTipTitle = title,
        BalloonTipText = message,
        BalloonTipIcon = ToolTipIcon.Info
    };
}
