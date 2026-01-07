using AiMultiAgent.Core.Agents.CodeReview;
using AiMultiAgent.Core.Agents.Documentation;
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
    ILogger<PmAgent> log)
{
    private readonly IMcpClient _mcp = mcp;
    private readonly CodeReviewerAgent _codeReviewStub = codeReviewStub;
    private readonly DocumentationAgent _docsStub = docsStub;
    private readonly ILogger<PmAgent> _log = log;

    /// <summary>
    /// Вызывает через MCP агента code_review и documentation, агрегирует их результаты
    /// и возвращает единый отчёт с reasoning steps
    /// </summary>
    public async Task<PmOrchestrationReport> OrchestrateAsync(
        PmOrchestrationRequest req,
        CancellationToken ct = default)
    {
        var reasoning = new List<string>();

        void Step(string message)
        {
            reasoning.Add(message);

            if (_log.IsEnabled(LogLevel.Information))
            {
                _log.LogInformation("[PM reasoning] {Message}", message);
            }
        }

        Step("Начинаю оркестрацию: подготовка входных данных для code_review и generate_docs");

        var (codeReview, usedFallbackCodeReview) = await TryMcpOrFallbackAsync(
            toolName: "code_review",
            mcpArgs: new
            {
                title = req.PrTitle,
                description = req.PrDescription,
                diff = req.Diff
            },
            fallbackFactory: async () =>
            {
                Step("Fallback: вызываю локальный CodeReviewerAgent вместо MCP code_review");
                return await _codeReviewStub.ReviewAsync(req.PrTitle, req.PrDescription, req.Diff, ct);
            },
            step: Step,
            ct: ct
        );

        var (docs, usedFallbackDocs) = await TryMcpOrFallbackAsync(
            toolName: "generate_docs",
            mcpArgs: new
            {
                componentName = req.ComponentName,
                description = req.ComponentDescription
            },
            fallbackFactory: async () =>
            {
                Step("Fallback: вызываю локальный DocumentationAgent вместо MCP generate_docs");
                return await _docsStub.GenerateAsync(req.ComponentName, req.ComponentDescription, ct);
            },
            step: Step,
            ct: ct
        );

        Step("Агрегирую результаты в единый отчёт");

        var summary = $"PM report: code_review={(usedFallbackCodeReview ? "fallback" : "mcp")}, " +
                      $"documentation={(usedFallbackDocs ? "fallback" : "mcp")}.";

        return new PmOrchestrationReport
        {
            Summary = summary,
            ReasoningLog = reasoning,
            CodeReview = codeReview,
            Documentation = docs
        };
    }

    /// <summary>
    /// Пытается вызвать MCP tool и распарсить типизированный результат
    /// </summary>
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

            if (_log.IsEnabled(LogLevel.Information))
            {
                _log.LogInformation("[PM MCP] tools/call -> {Tool}", toolName);
            }

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
            
            if (_log.IsEnabled(LogLevel.Warning))
            {
                _log.LogWarning(ex, "[PM MCP] tool '{Tool}' failed. Using fallback stub.", toolName);
            }

            var fallback = await fallbackFactory();
            return (fallback, true);
        }
    }
}
