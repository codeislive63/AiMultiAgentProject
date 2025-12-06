using AiMultiAgent.Core.Agents.Pm;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AiMultiAgent.Mcp.Server.Tools.Pm;

[McpServerToolType]
public sealed class PmTools(PmAgent pmAgent)
{
    [McpServerTool(Name = "pm_plan", Title = "Сделать план по цели")]
    public Task<PmPlan> PlanAsync(
        [Description("Цель / what to achieve")] string goal,
        CancellationToken ct = default)
    {
        return pmAgent.PlanAsync(goal, ct);
    }
}
