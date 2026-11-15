using YrClean.Core.Services;
using Xunit;

namespace YrClean.Tests;

public class SystemToolsTests
{
    [Theory]
    [InlineData("[==========================60.0%===                       ]", 60)]
    [InlineData("[===========================100.0%==========================]", 100)]
    [InlineData("Verification 42% complete.", 42)]
    [InlineData("Проверка 7% завершена.", 7)]
    [InlineData("12,5 %", 12)]
    public void Percent_is_read_from_tool_progress_lines(string line, int expected)
    {
        Assert.Equal(expected, ProcessRunner.TryParsePercent(line));
    }

    [Theory]
    [InlineData("The operation completed successfully.")]
    [InlineData("Image Version: 10.0.19044.1288")]
    public void Ordinary_lines_are_not_progress(string line)
    {
        Assert.Null(ProcessRunner.TryParsePercent(line));
    }

    // Read-only calls against the real machine: they must answer without throwing
    [Fact]
    public void Read_only_system_queries_work_on_this_machine()
    {
        Assert.True(SystemMaintenance.QueryRecycleBin().Bytes >= 0);

        var lines = new List<string>();
        var exitCode = ProcessRunner.Run(ProcessRunner.System32("cmd.exe"), new[] { "/c", "echo hello" }, lines.Add);
        Assert.Equal(0, exitCode);
        Assert.Equal(new[] { "hello" }, lines);
    }
}
