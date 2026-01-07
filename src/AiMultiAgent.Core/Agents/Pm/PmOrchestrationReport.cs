namespace AiMultiAgent.Core.Agents.Pm;

public sealed class PmOrchestrationReport
{
    public object Meta { get; set; } = new { };
    public object ToolResults { get; set; } = new { };
    public List<object> Risks { get; set; } = [];
    public List<object> NextActions { get; set; } = [];
    public string Summary { get; set; } = "";
    public List<TraceEvent> Trace { get; set; } = [];
}
