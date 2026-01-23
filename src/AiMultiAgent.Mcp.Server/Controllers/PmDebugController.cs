using AiMultiAgent.Core.Agents.Pm;
using AiMultiAgent.Mcp.Client;
using Microsoft.AspNetCore.Mvc;

namespace AiMultiAgent.Mcp.Server.Controllers;

[ApiController]
[Route("[controller]/mcp")]
public sealed class PmDebugController(IMcpClient mcpClient) : ControllerBase
{
    /// <summary>
    /// POST /PmDebug/mcp/report
    /// Дёргает MCP tool "pm_report" и возвращает агрегированный отчёт.
    /// </summary>
    [HttpPost("report")]
    public async Task<ActionResult<PmReport>> Report(
     [FromBody] PmRequest request,
     CancellationToken ct)
    {
        const string toolName = "pm_report";

        var result = await mcpClient.CallToolAsync<PmReport>(
            toolName,
            new
            {
                request = request
            },
            ct : ct
        );

        return Ok(result);
    }

}
