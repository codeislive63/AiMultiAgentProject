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
    public Task<PmOrchestrationReport> ReportAsync(
        [Description("Имя файла")] string fileName,
        [Description("Содержимое файла")] string data,
        [Description("Имя компонента для доков")] string componentName,
        [Description("Описание компонента для доков")] string componentDescription,
        CancellationToken ct = default) => agent.OrchestrateAsync(new PmOrchestrationRequest
        {
            FileName = fileName,
            Data = data,
            ComponentName = componentName,
            ComponentDescription = componentDescription
        }, ct);
}
