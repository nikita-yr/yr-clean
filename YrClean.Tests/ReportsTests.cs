using YrClean.Core.Models;
using YrClean.Core.Services;
using Xunit;

namespace YrClean.Tests;

// Read-only against the real machine: the reports must build without throwing
public class ReportsTests
{
    [Fact]
    public void Startup_report_has_a_summary_line()
    {
        Assert.StartsWith("Startup entries:", Reports.Startup()[0]);
    }

    [Fact]
    public void Tool_verdicts_name_the_tool_and_outcome()
    {
        var lines = Reports.Verdicts(new[]
        {
            new ToolResult("dism", 0, "The operation completed successfully."),
            new ToolResult("sfc", 2, "Could not repair.")
        });

        Assert.Equal("dism: completed - The operation completed successfully.", lines[0]);
        Assert.Equal("sfc: exit code 2 - Could not repair.", lines[1]);
    }
}
