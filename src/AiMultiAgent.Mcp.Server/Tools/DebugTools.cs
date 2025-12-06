using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AiMultiAgent.Mcp.Server.Tools;

[McpServerToolType]
public sealed class DebugTools
{
    [McpServerTool(Name = "echo", Title = "Echo back the given text")]
    public Task<string> EchoAsync([Description("Message sent to MCP server")] string text, CancellationToken ct = default)
    {
        return Task.FromResult(text);
    }
}
