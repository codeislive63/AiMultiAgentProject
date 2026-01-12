using AiMultiAgent.Core.Agents.CodeReview;
using AiMultiAgent.Mcp.Client;
using Microsoft.AspNetCore.Mvc;

namespace AiMultiAgent.Mcp.Server.Controllers;

[ApiController]
[Route("[controller]/mcp")]
public sealed class CodeReviewDebugController(SseMcpClient mcpSseClient) : ControllerBase
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
                fileName = request.FileName,
                data = request.Data
            },
            ct: ct);

        return Ok(result);
    }
}

public sealed class CodeReviewRequest
{
    public string FileName { get; init; } = default!;
    public string Data { get; init; } = default!;
}
