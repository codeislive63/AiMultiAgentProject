namespace AiMultiAgent.Core.Agents.Pm;

public sealed class PmStep
{
    public int Order { get; init; }
    public string Title { get; init; } = default!;
    public string? Description { get; init; }
}
