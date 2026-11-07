using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Xunit;

namespace LibraryMCPServer.Tests;

public interface ITestService
{
    string GetData();
}

public class TestService : ITestService
{
    public string GetData() => "TestPassed";
}

[McpServerToolType]
public class SampleDiTools
{
    private readonly ITestService _service;

    public SampleDiTools(ITestService service)
    {
        _service = service;
    }

    [McpServerTool(Name = "sample_test"), Description("Sample test tool with DI")]
    public string SampleTest()
    {
        return _service.GetData();
    }
}

public class ToolRegistrationTests
{
    [Fact]
    public void TestToolsWithDependencyInjection()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<ITestService, TestService>();
        builder.Services.AddSingleton<SampleDiTools>();
        builder.Services.AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly(typeof(SampleDiTools).Assembly);

        var host = builder.Build();
        Assert.NotNull(host);
    }
}
