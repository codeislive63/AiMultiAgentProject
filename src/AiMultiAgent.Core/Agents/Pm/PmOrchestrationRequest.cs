namespace AiMultiAgent.Core.Agents.Pm;

public sealed class PmOrchestrationRequest
{
    public string FileName { get; init; } = default!;
    public string Data { get; init; } = default!;

    public string ComponentName { get; init; } = default!;
    public string ComponentDescription { get; init; } = default!;
}
