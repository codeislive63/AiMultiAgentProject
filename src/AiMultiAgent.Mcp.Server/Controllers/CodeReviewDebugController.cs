using AiMultiAgent.Core.Agents.CodeReview;
using AiMultiAgent.Mcp.Client;
using Microsoft.AspNetCore.Mvc;

namespace AiMultiAgent.Mcp.Server.Controllers;

[ApiController]
[Route("[controller]/mcp")]
public sealed class CodeReviewDebugController(McpSseClient mcpSseClient) : ControllerBase
{
    // POST /CodeReviewDebug/mcp/review
    [HttpPost("review")]
    public async Task<ActionResult<CodeReviewResult>> Review(
        [FromBody] CodeReviewRequest request,
        CancellationToken ct)
    {
        // ВАЖНО: имя должно совпадать с Name в [McpServerTool]
        const string toolName = "code_review";

        var result = await mcpSseClient.CallToolAsync<CodeReviewResult>(
            toolName,
            new
            {
                title = request.Title,
                description = request.Description,
                diff = request.Diff
            },
            ct: ct);

        return Ok(result);
    }
}

public sealed class CodeReviewRequest
{
    public string Title { get; init; } = default!;
    public string Description { get; init; } = default!;
    public string Diff { get; init; } = default!;
}
