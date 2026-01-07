using AiMultiAgent.Core.Agents.Pm;
using AiMultiAgent.Mcp.Client;
using Microsoft.AspNetCore.Mvc;

namespace AiMultiAgent.Mcp.Server.Controllers;

[ApiController]
[Route("[controller]/mcp")]
public sealed class PmDebugController(SseMcpClient mcpSseClient) : ControllerBase
{
    // GET /PmDebug/mcp/plan?goal=...
    [HttpGet("plan")]
    public async Task<ActionResult<PmPlan>> Plan(
        [FromQuery] string goal,
        CancellationToken ct)
    {
        const string toolName = "pm_plan";

        var result = await mcpSseClient.CallToolAsync<PmPlan>(
            toolName,
            new { goal },
            ct: ct);

        return Ok(result);
    }
}
