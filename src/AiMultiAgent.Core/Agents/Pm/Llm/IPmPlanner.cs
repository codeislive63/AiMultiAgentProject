namespace AiMultiAgent.Core.Agents.Pm.Llm;

public interface IPmPlanner
{
    Task<PmPlan> CreatePlanAsync(PmOrchestrationRequest req, CancellationToken ct);
    
    Task<PmOrchestrationReport> AggregateAsync(
        PmOrchestrationRequest req,
        object toolResults,
        List<TraceEvent> traces,
        CancellationToken ct
    );
}
