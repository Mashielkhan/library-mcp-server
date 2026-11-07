using ModelContextProtocol.Server;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace LibraryMCPServer.Tests;

public class ToolInspectionTests
{
    private readonly ITestOutputHelper _output;

    public ToolInspectionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void InspectMcpAttributes()
    {
        var asm = typeof(McpServerToolAttribute).Assembly;
        foreach (var type in asm.GetExportedTypes())
        {
            if (type.Name.Contains("Resource") || type.Name.Contains("Prompt") || type.Name.Contains("Tool"))
            {
                _output.WriteLine($"Type: {type.FullName}");
            }
        }
    }
}
