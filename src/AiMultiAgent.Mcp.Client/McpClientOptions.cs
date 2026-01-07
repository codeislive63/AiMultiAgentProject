namespace AiMultiAgent.Mcp.Client;

/// <summary>
/// Настройки MCP-клиента
/// </summary>
public sealed class McpClientOptions
{
    /// <summary>
    /// Путь до MCP endpoint
    /// </summary>
    public required string EndpointPath { get; set; }
}
