using AiMultiAgent.Core.Agents.CodeReview;
using AiMultiAgent.Core.Agents.Documentation;
using AiMultiAgent.Core.Agents.Pm.Llm;
using AiMultiAgent.Mcp.Client;
using GenerativeAI.Exceptions;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AiMultiAgent.Core.Agents.Pm;

public sealed class PmAgent(
    IMcpClient mcp,
    CodeReviewerAgent codeReviewStub,
    DocumentationAgent docsStub,
    IPmPlanner planner,
    ILogger<PmAgent> logger)
{
    private readonly IMcpClient _mcp = mcp;
    private readonly CodeReviewerAgent _codeReviewStub = codeReviewStub;
    private readonly DocumentationAgent _docsStub = docsStub;
    private readonly IPmPlanner _planner = planner;
    private readonly ILogger<PmAgent> _log = logger;

    public async Task<PmOrchestrationReport> OrchestrateAsync(
        PmOrchestrationRequest req,
        CancellationToken ct = default)
    {
        var toolResults = new Dictionary<string, object?>();
        var trace = new List<TraceEvent>();

        void Trace(string type, string? tool = null, string? details = null)
            => trace.Add(new TraceEvent(DateTimeOffset.UtcNow, type, tool, details));

        void Step(string message)
        {
            Trace("REASONING", details: message);
            _log.LogInformation("[PM reasoning] {Message}", message);
        }

        Step("PM: старт. Запрашиваю у LLM план действий");

        PmPlan plan;

        try
        {
            plan = await _planner.CreatePlanAsync(req, ct);
            Step($"PM: план получен. Steps={plan.Steps.Count}");
        }
        catch (Exception ex)
        {
            Step($"PM: LLM planner упал ({ex.GetType().Name}). Fallback -> default plan");
            _log.LogWarning(ex, "LLM planner failed, fallback to default scenario");

            plan = new PmPlan
            {
                Objective = "Fallback default plan",
                Steps =
                [
                    new PmPlanStep
                    {
                        Id = "1",
                        Tool = "code_review",
                        Arguments = new { title = req.PrTitle, description = req.PrDescription, diff = req.Diff },
                        OnFail = "continue"
                    },
                    new PmPlanStep
                    {
                        Id = "2",
                        Tool = "generate_docs",
                        Arguments = new { componentName = req.ComponentName, description = req.ComponentDescription },
                        OnFail = "continue"
                    }
                ]
            };
        }

        // Execute plan
        foreach (var step in plan.Steps)
        {
            Step($"PM: выполняю шаг {step.Id} -> tool '{step.Tool}'");
            Trace("TOOL_CALL_START", tool: step.Tool, details: step.Id);

            try
            {
                if (step.Tool == "code_review")
                {
                    Step($"LLM suggested args for code_review: {JsonSerializer.Serialize(step.Arguments)}");

                    // SAFE-MODE args
                    var mcpArgs = new
                    {
                        title = req.PrTitle,
                        description = req.PrDescription,
                        diff = req.Diff
                    };

                    var (result, usedFallback) = await TryMcpOrFallbackAsync(
                        toolName: "code_review",
                        mcpArgs: mcpArgs,
                        fallbackFactory: async () =>
                        {
                            Step("Fallback: локальный CodeReviewerAgent");
                            return await _codeReviewStub.ReviewAsync(req.PrTitle, req.PrDescription, req.Diff, ct);
                        },
                        step: Step,
                        ct: ct
                    );

                    toolResults[step.Id] = result;
                    toolResults["code_review_usedFallback"] = usedFallback;
                }
                else if (step.Tool == "generate_docs")
                {
                    Step($"LLM suggested args for generate_docs: {JsonSerializer.Serialize(step.Arguments)}");

                    // SAFE-MODE args
                    var mcpArgs = new
                    {
                        componentName = req.ComponentName,
                        description = req.ComponentDescription
                    };

                    var (result, usedFallback) = await TryMcpOrFallbackAsync(
                        toolName: "generate_docs",
                        mcpArgs: mcpArgs,
                        fallbackFactory: async () =>
                        {
                            Step("Fallback: локальный DocumentationAgent");
                            return await _docsStub.GenerateAsync(req.ComponentName, req.ComponentDescription, ct);
                        },
                        step: Step,
                        ct: ct
                    );

                    toolResults[step.Id] = result;
                    toolResults["generate_docs_usedFallback"] = usedFallback;
                }
                else
                {
                    Step($"PM: неизвестный tool '{step.Tool}', пропускаю");
                    toolResults[step.Id] = new { error = "unknown_tool", tool = step.Tool };
                }

                Trace("TOOL_CALL_END", tool: step.Tool, details: "ok");
            }
            catch (Exception ex)
            {
                Trace("TOOL_CALL_END", tool: step.Tool, details: "error: " + ex.Message);
                Step($"PM: шаг {step.Id} упал: {ex.GetType().Name}");

                toolResults[step.Id] = new { error = ex.Message, exception = ex.GetType().Name };

                if (!string.Equals(step.OnFail, "continue", StringComparison.OrdinalIgnoreCase))
                    throw;
            }
        }

        Step("PM: запрашиваю у LLM финальную агрегацию отчёта");

        try
        {
            var report = await _planner.AggregateAsync(req, toolResults, trace, ct);

            // Force stable machine-readable fields
            report.ToolResults = toolResults;
            report.Trace = trace;

            report.Meta = new Dictionary<string, object?>
            {
                ["objective"] = plan.Objective,
                ["steps"] = plan.Steps.Count,
                ["timestamp"] = DateTimeOffset.UtcNow,
                ["aggregation"] = "llm"
            };

            Step("PM: отчёт готов (LLM aggregation)");
            return report;
        }
        catch (Exception ex)
        {
            Step($"PM: LLM aggregation упала ({ex.GetType().Name}). Fallback aggregation");
            _log.LogWarning(ex, "LLM aggregation failed, fallback report");

            var aggregation = "fallback";

            if (ex is ApiException apiEx)
            {
                var msg = apiEx.Message ?? "";

                if (apiEx.ErrorCode == 429 || msg.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase))
                    aggregation = "fallback_quota";
                else if (apiEx.ErrorCode == 503 || msg.Contains("UNAVAILABLE", StringComparison.OrdinalIgnoreCase))
                    aggregation = "fallback_overloaded";
            }

            return new PmOrchestrationReport
            {
                Meta = new Dictionary<string, object?>
                {
                    ["objective"] = plan.Objective,
                    ["steps"] = plan.Steps.Count,
                    ["timestamp"] = DateTimeOffset.UtcNow,
                    ["aggregation"] = aggregation,
                    ["llm_error"] = ex.GetType().Name,
                    ["llm_error_message"] = ex.Message
                },
                ToolResults = toolResults,
                Summary = "PM report (fallback aggregation).",
                Risks = [],
                NextActions = [],
                Trace = trace
            };
        }
    }

    private async Task<(TResult Result, bool UsedFallback)> TryMcpOrFallbackAsync<TResult>(
        string toolName,
        object mcpArgs,
        Func<Task<TResult>> fallbackFactory,
        Action<string> step,
        CancellationToken ct)
    {
        try
        {
            step($"Пробую вызвать MCP tool '{toolName}'");
            _log.LogInformation("[PM MCP] tools/call -> {Tool}", toolName);

            var result = await _mcp.CallToolAsync<TResult>(
                toolName: toolName,
                arguments: mcpArgs,
                jsonRpcId: null,
                ct: ct
            ) ?? throw new InvalidOperationException($"MCP tool '{toolName}' вернул null");

            step($"MCP tool '{toolName}' успешно вернул результат");
            return (result, false);
        }
        catch (Exception ex)
        {
            step($"MCP вызов '{toolName}' не удался ({ex.GetType().Name}). Использую fallback-заглушку");
            _log.LogWarning(ex, "[PM MCP] tool '{Tool}' failed. Using fallback stub.", toolName);

            var fallback = await fallbackFactory();
            return (fallback, true);
        }
    }
}
