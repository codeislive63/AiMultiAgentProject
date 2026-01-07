using AiMultiAgent.Mcp.Client;
using Microsoft.AspNetCore.Mvc;

namespace AiMultiAgent.Mcp.Server.Controllers;

[ApiController]
[Route("[controller]/mcp")]
public class DebugController(SseMcpClient mcpSseClient) : ControllerBase
{
    // GET /debug/mcp/echo?text=Привет
    [HttpGet("echo")]
    public async Task<IActionResult> Echo([FromQuery] string text, CancellationToken ct)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new
            {
                name = "echo",
                arguments = new
                {
                    text
                }
            }
        };

        // Можно собрать объект и отправить его через SendAsync
        var jToken = await mcpSseClient.SendAsync(payload, ct);

        // Также можно возвращать через Content
        return Ok(jToken);
    }

    // GET /debug/mcp/tools
    [HttpGet("tools")]
    public async Task<IActionResult> GetTools(CancellationToken ct)
    {
        var jToken = await mcpSseClient.CallToolsListAsync(ct: ct);
        return Ok(jToken);
    }
}
