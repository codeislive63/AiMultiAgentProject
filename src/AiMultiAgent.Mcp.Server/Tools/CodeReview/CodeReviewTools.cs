using AiMultiAgent.Core.Agents.CodeReview;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AiMultiAgent.Mcp.Server.Tools.CodeReview;

[McpServerToolType]
public sealed class CodeReviewTools(CodeReviewerAgent agent)
{
    [McpServerTool(Name = "code_review", Title = "Сделать ревью PR")]
    public Task<CodeReviewResult> ReviewAsync(
        [Description("Заголовок PR")] string title,
        [Description("Описание PR")] string description,
        [Description("Diff в любом удобном формате")] string diff,
        CancellationToken ct = default)
    {
        return agent.ReviewAsync(title, description, diff, ct);
    }
}
