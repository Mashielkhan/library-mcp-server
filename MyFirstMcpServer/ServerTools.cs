using System.ComponentModel;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class ServerTools
{
    [McpServerTool, Description("Returns the supplied text unchanged.")]
    public static string Echo(string text) => text;

    [McpServerTool, Description("Returns the current UTC time in ISO 8601 format.")]
    public static string GetCurrentTime() => DateTimeOffset.UtcNow.ToString("O");

    [McpServerTool, Description("Adds two whole numbers and returns their sum.")]
    public static int Add(
        [Description("The first whole number.")] int first,
        [Description("The second whole number.")] int second) =>
        checked(first + second);
}
