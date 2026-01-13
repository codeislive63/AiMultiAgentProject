using AiMultiAgent.Core.Agents.CodeReview;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AiMultiAgent.Mcp.Server.Tools.CodeReview;

[McpServerToolType]
public sealed class CodeReviewTools(CodeReviewerAgent agent)
{
    [McpServerTool(Name = "code_review", Title = "Сделать ревью коммита")]
    public Task<CodeReviewResult> ReviewAsync(
        [Description("Имя файла")] string fileName,
        [Description("Содержимое")] string data,
        CancellationToken ct = default)
    {
        return agent.ReviewAsync(fileName, data, ct);
    }
}
