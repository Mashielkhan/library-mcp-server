using ModelContextProtocol.Server;
using Xunit;
using Xunit.Abstractions;

namespace LibraryMCPServer.Tests;

public class ResourceAndPromptInspectionTests
{
    private readonly ITestOutputHelper _output;

    public ResourceAndPromptInspectionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void InspectResourceAndPromptAttributes()
    {
        var resType = typeof(McpServerResourceAttribute);
        _output.WriteLine($"ResourceAttr properties:");
        foreach (var p in resType.GetProperties())
        {
            _output.WriteLine($" - {p.Name} ({p.PropertyType.Name})");
        }
        foreach (var c in resType.GetConstructors())
        {
            var pList = string.Join(", ", c.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
            _output.WriteLine($" - Ctor({pList})");
        }

        var promptType = typeof(McpServerPromptAttribute);
        _output.WriteLine($"PromptAttr properties:");
        foreach (var p in promptType.GetProperties())
        {
            _output.WriteLine($" - {p.Name} ({p.PropertyType.Name})");
        }
        foreach (var c in promptType.GetConstructors())
        {
            var pList = string.Join(", ", c.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
            _output.WriteLine($" - Ctor({pList})");
        }
    }
}
