using AiMultiAgent.Core.Agents.CodeReview;
using AiMultiAgent.Core.Agents.Documentation;
using AiMultiAgent.Core.Agents.Pm.Llm;
using AiMultiAgent.Mcp.Client;
using Microsoft.Extensions.Logging;

namespace AiMultiAgent.Core.Agents.Pm;

/// <summary>
/// Project Manager Agent.
/// Оркестрирует вызовы других агентов через MCP (code_review + generate_docs),
/// агрегирует ответы в единый отчёт (JSON + краткий summary)
/// </summary>
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

    /// <summary>
    /// Вызывает через MCP агента code_review и documentation, агрегирует их результаты
    /// и возвращает единый отчёт с reasoning steps
    /// </summary>
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
                        Id = "code_review",
                        Tool = "code_review",
                        Arguments = new { title = req.PrTitle, description = req.PrDescription, diff = req.Diff },
                        OnFail = "continue"
                    },
                    new PmPlanStep
                    {
                        Id = "generate_docs",
                        Tool = "generate_docs",
                        Arguments = new { componentName = req.ComponentName, description = req.ComponentDescription },
                        OnFail = "continue"
                    }
                ]
            };
        }

        // Выполнение плана
        foreach (var step in plan.Steps)
        {
            Step($"PM: выполняю шаг {step.Id} -> tool '{step.Tool}'");
            Trace("TOOL_CALL_START", tool: step.Tool, details: step.Id);

            try
            {
                if (step.Tool == "code_review")
                {
                    var (result, usedFallback) = await TryMcpOrFallbackAsync(
                        toolName: "code_review",
                        mcpArgs: step.Arguments,
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
                    var (result, usedFallback) = await TryMcpOrFallbackAsync(
                        toolName: "generate_docs",
                        mcpArgs: step.Arguments,
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

        // Аггрегация через LLM
        Step("PM: запрашиваю у LLM финальную агрегацию отчёта");

        try
        {
            var report = await _planner.AggregateAsync(req, toolResults, trace, ct);

            // Гарантируем стабильные поля (чтобы отчёт всегда был машинно-читаемым)
            report.ToolResults = toolResults;
            report.Trace = trace;

            report.Meta = new
            {
                objective = plan.Objective,
                steps = plan.Steps.Count,
                timestamp = DateTimeOffset.UtcNow,
                aggregation = "llm"
            };

            Step("PM: отчёт готов (LLM aggregation)");
            return report;
        }
        catch (Exception ex)
        {
            Step($"PM: LLM aggregation упала ({ex.GetType().Name}). Fallback aggregation");
            _log.LogWarning(ex, "LLM aggregation failed, fallback report");

            return new PmOrchestrationReport
            {
                Meta = new
                {
                    objective = plan.Objective,
                    steps = plan.Steps.Count,
                    timestamp = DateTimeOffset.UtcNow,
                    aggregation = "fallback"
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
