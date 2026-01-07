namespace AiMultiAgent.Core.Agents.Pm;

public sealed class PmOrchestrationRequest
{
    public string PrTitle { get; init; } = default!;
    public string PrDescription { get; init; } = default!;
    public string Diff { get; init; } = default!;

    public string ComponentName { get; init; } = default!;
    public string ComponentDescription { get; init; } = default!;
}
