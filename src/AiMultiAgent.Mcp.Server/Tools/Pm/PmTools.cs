using AiMultiAgent.Core.Agents.Pm;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AiMultiAgent.Mcp.Server.Tools.Pm;

[McpServerToolType]
public sealed class PmTools(PmAgent agent)
{
    /// <summary>
    /// Вызывает code_review и generate_docs,
    /// агрегирует результаты и возвращает единый отчёт
    /// </summary>
    [McpServerTool(Name = "pm_report", Title = "PM: оркестрация code_review + generate_docs")]
    public Task<PmReport> ReportAsync([Description("PM request DTO")] PmRequest request, CancellationToken ct = default) 
        => agent.OrchestrateAsync(request, ct);
}
