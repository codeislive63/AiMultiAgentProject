using AiMultiAgent.Core.Agents.CodeReview;
using AiMultiAgent.Core.Agents.Documentation;

namespace AiMultiAgent.Core.Agents.Pm;

public sealed class PmOrchestrationReport
{
    public string Summary { get; init; } = default!;
    public List<string> ReasoningLog { get; init; } = [];

    public CodeReviewResult? CodeReview { get; init; }
    public DocumentationResult? Documentation { get; init; }
}
