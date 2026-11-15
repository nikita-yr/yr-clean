using YrClean.Core.Services;
using Xunit;

namespace YrClean.Tests;

public class StartupManagerTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" --minimized", @"C:\Program Files\App\app.exe")]
    [InlineData(@"C:\Program Files\App\app.exe /background", @"C:\Program Files\App\app.exe")]
    [InlineData(@"C:\Tools\run.cmd", @"C:\Tools\run.cmd")]
    [InlineData("rundll32.exe shell32.dll,Control_RunDLL", null)]
    public void Executable_is_extracted_from_a_command_line(string command, string? expected)
    {
        Assert.Equal(expected, StartupManager.ExecutableFromCommand(command));
    }

    [Fact]
    public void Startup_approved_bytes_follow_the_task_manager_format()
    {
        Assert.True(StartupManager.IsApproved(null));
        Assert.True(StartupManager.IsApproved(new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
        Assert.True(StartupManager.IsApproved(new byte[] { 6, 0, 0, 0 }));
        Assert.False(StartupManager.IsApproved(new byte[] { 3, 0, 0, 0 }));
        Assert.False(StartupManager.IsApproved(new byte[] { 7, 0, 0, 0 }));

        var disabled = StartupManager.ApprovedValue(false, DateTime.UtcNow);
        Assert.Equal(12, disabled.Length);
        Assert.False(StartupManager.IsApproved(disabled));
        Assert.True(StartupManager.IsApproved(StartupManager.ApprovedValue(true, DateTime.UtcNow)));
    }

    private const string TaskXml = """
        <?xml version="1.0"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <Triggers>{0}</Triggers>
          <Settings><Enabled>{1}</Enabled></Settings>
          <Actions Context="Author">
            <Exec><Command>C:\Program Files\App\updater.exe</Command><Arguments>/silent</Arguments></Exec>
          </Actions>
        </Task>
        """;

    [Theory]
    [InlineData("<LogonTrigger><Enabled>true</Enabled></LogonTrigger>", "true", true)]
    [InlineData("<BootTrigger />", "false", false)]
    public void Logon_and_boot_tasks_are_startup_items(string trigger, string enabledText, bool enabled)
    {
        var task = StartupManager.ParseTask(string.Format(TaskXml, trigger, enabledText));

        Assert.NotNull(task);
        Assert.Equal("\"C:\\Program Files\\App\\updater.exe\" /silent", task.Value.Command);
        Assert.Equal(enabled, task.Value.Enabled);
    }

    [Fact]
    public void Task_on_a_plain_schedule_is_not_a_startup_item()
    {
        Assert.Null(StartupManager.ParseTask(string.Format(TaskXml, "<CalendarTrigger />", "true")));
    }

    // Read-only against the real machine: must enumerate without throwing
    [Fact]
    public void Listing_startup_items_works_on_this_machine()
    {
        Assert.NotNull(StartupManager.GetItems());
    }
}
