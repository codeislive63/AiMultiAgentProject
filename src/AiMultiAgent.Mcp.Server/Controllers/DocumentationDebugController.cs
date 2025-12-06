using AiMultiAgent.Core.Agents.Documentation;
using AiMultiAgent.Mcp.Client;
using Microsoft.AspNetCore.Mvc;

namespace AiMultiAgent.Mcp.Server.Controllers;

[ApiController]
[Route("[controller]/mcp")]
public sealed class DocumentationDebugController(McpSseClient mcpSseClient) : ControllerBase
{
    // POST /DocumentationDebug/mcp/generate
    [HttpPost("generate")]
    public async Task<ActionResult<DocumentationResult>> Generate(
        [FromBody] GenerateDocsRequest request,
        CancellationToken ct)
    {
        // Имя должно совпадать с Name в твоём MCP tool для документации
        const string toolName = "documentation_generate";

        var result = await mcpSseClient.CallToolAsync<DocumentationResult>(
            toolName,
            new
            {
                componentName = request.ComponentName,
                description = request.Description
            },
            ct: ct);

        return Ok(result);
    }
}

public sealed class GenerateDocsRequest
{
    public string ComponentName { get; init; } = default!;
    public string Description { get; init; } = default!;
}
