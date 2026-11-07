using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Xunit;

namespace LibraryMCPServer.Tests;

public record DemoDto(string Message, int Count);

[McpServerToolType]
public class DemoTools
{
    [McpServerTool(Name = "get_demo"), Description("Returns demo DTO")]
    public DemoDto GetDemo()
    {
        return new DemoDto("Hello MCP", 42);
    }

    [McpServerTool(Name = "echo_message"), Description("Echoes message")]
    public string EchoMessage([Description("Message to echo")] string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Message cannot be empty.");
        }
        return $"Echo: {message}";
    }
}

public class DemoToolTests
{
    [Fact]
    public void TestToolRegistrationSucceeds()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<DemoTools>();
        builder.Services.AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly(typeof(DemoTools).Assembly);

        var host = builder.Build();
        Assert.NotNull(host);
    }
}
