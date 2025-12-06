namespace AiMultiAgent.Core.Agents.Pm;

public sealed class PmPlan
{
    public string Goal { get; init; } = default!;
    public List<PmStep> Steps { get; init; } = [];
}
