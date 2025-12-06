namespace AiMultiAgent.Core.Agents.CodeReview;

public sealed class CodeReviewResult
{
    public string Summary { get; init; } = default!;
    public List<CodeReviewIssue> Issues { get; init; } = new();
    public List<string> Suggestions { get; init; } = new();
}
