using AiMultiAgent.Core.Agents.CodeReview;
using AiMultiAgent.Core.Agents.Documentation;
using AiMultiAgent.Core.Agents.Pm;
using AiMultiAgent.Mcp.Client;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace AiMultiAgent.Core.Tests.Agents.Pm;

/// <summary>
/// Набор unit-тестов для <see cref="PmAgent"/>.
/// Проверяем оркестрацию вызовов MCP tools,
/// агрегацию результата в отчёт и fallback на stub-агентов при сбоях MCP
/// </summary>
public sealed class PmAgentTests
{
    /// <summary>
    /// Фейковая реализация <see cref="IMcpClient"/> для тестирования <see cref="PmAgent"/> без поднятого MCP сервера
    /// </summary>
    private sealed class FakeMcpClient : IMcpClient
    {
        private readonly Dictionary<string, object?> _typedResults = [];
        private readonly Dictionary<string, Exception> _exceptions = [];

        public List<string> Calls { get; } = [];

        public FakeMcpClient WithTypedResult(string toolName, object? result)
        {
            _typedResults[toolName] = result;
            return this;
        }

        public FakeMcpClient WithException(string toolName, Exception ex)
        {
            _exceptions[toolName] = ex;
            return this;
        }

        public Task<JToken> CallToolAsync(string toolName, object? arguments = null, string? jsonRpcId = null, CancellationToken ct = default)
            => Task.FromResult<JToken>(new JObject());

        public Task<TResult?> CallToolAsync<TResult>(string toolName, object? arguments = null, string? jsonRpcId = null, CancellationToken ct = default)
        {
            Calls.Add(toolName);

            if (_exceptions.TryGetValue(toolName, out var ex))
                throw ex;

            if (_typedResults.TryGetValue(toolName, out var value))
                return Task.FromResult((TResult?)value);

            return Task.FromResult<TResult?>(default);
        }

        public Task<JToken> CallToolsListAsync(string? jsonRpcId = null, CancellationToken ct = default)
            => Task.FromResult<JToken>(new JObject());
    }

    /// <summary>
    /// Проверяет happy-path: при доступном MCP оба tool-вызова успешны,
    /// PM Agent агрегирует результаты, формирует Summary = "mcp/mcp",
    /// а также пишет шаги в ReasoningLog и вызывает tools в правильном порядке
    /// </summary>
    [Fact]
    public async Task OrchestrateAsync_Should_Aggregate_Mcp_Results_When_Mcp_Is_Available()
    {
        var mcp = new FakeMcpClient()
            .WithTypedResult("code_review", new CodeReviewResult
            {
                Summary = "mcp review",
                Issues = [],
                Suggestions = []
            })
            .WithTypedResult("generate_docs", new DocumentationResult
            {
                Markdown = "mcp docs",
                UmlPlantUml = "@startuml\n@enduml"
            });

        var agent = CreatePmAgent(mcp);

        var req = CreateRequest();

        var report = await agent.OrchestrateAsync(req);

        report.Should().NotBeNull();
        report.Summary.Should().Be("PM report: code_review=mcp, documentation=mcp.");
        report.CodeReview!.Summary.Should().Be("mcp review");
        report.Documentation!.Markdown.Should().Be("mcp docs");

        mcp.Calls.Should().ContainInOrder("code_review", "generate_docs");

        report.ReasoningLog.Should().Contain(x => x.Contains("Пробую вызвать MCP tool") && x.Contains("code_review"));
        report.ReasoningLog.Should().Contain(x => x.Contains("Пробую вызвать MCP tool") && x.Contains("generate_docs"));
        report.ReasoningLog.Should().Contain(x => x.Contains("Агрегирую"));
    }

    /// <summary>
    /// Проверяет fallback для code_review: если MCP-вызов tool "code_review" падает,
    /// PM Agent должен использовать локальную stub-реализацию CodeReviewerAgent,
    /// при этом tool "generate_docs" продолжает вызываться через MCP.
    /// В Summary должно отразиться "fallback/mcp", а в ReasoningLog — факт fallback.
    /// </summary>
    [Fact]
    public async Task OrchestrateAsync_Should_Fallback_To_Stub_When_CodeReview_Mcp_Fails()
    {
        var mcp = new FakeMcpClient()
            .WithException("code_review", new HttpRequestException("mcp down"))
            .WithTypedResult("generate_docs", new DocumentationResult
            {
                Markdown = "mcp docs",
                UmlPlantUml = "@startuml\n@enduml"
            });

        var agent = CreatePmAgent(mcp);
        var req = CreateRequest();

        var report = await agent.OrchestrateAsync(req);

        report.Summary.Should().Be("PM report: code_review=fallback, documentation=mcp.");
        report.CodeReview.Should().NotBeNull();
        report.Documentation!.Markdown.Should().Be("mcp docs");

        report.ReasoningLog.Should().Contain(x => x.Contains("Fallback"));
        mcp.Calls.Should().Contain("code_review");
        mcp.Calls.Should().Contain("generate_docs");
    }

    /// <summary>
    /// Проверяет fallback для generate_docs: если MCP-вызов tool "generate_docs" падает,
    /// PM Agent должен использовать локальную stub-реализацию DocumentationAgent,
    /// при этом tool "code_review" продолжает вызываться через MCP.
    /// В Summary должно отразиться "mcp/fallback", а в ReasoningLog — факт fallback
    /// </summary>
    [Fact]
    public async Task OrchestrateAsync_Should_Fallback_To_Stub_When_Docs_Mcp_Fails()
    {
        var mcp = new FakeMcpClient()
            .WithTypedResult("code_review", new CodeReviewResult
            {
                Summary = "mcp review",
                Issues = [],
                Suggestions = []
            })
            .WithException("generate_docs", new TimeoutException("timeout"));

        var agent = CreatePmAgent(mcp);
        var req = CreateRequest();

        var report = await agent.OrchestrateAsync(req);

        report.Summary.Should().Be("PM report: code_review=mcp, documentation=fallback.");
        report.CodeReview!.Summary.Should().Be("mcp review");
        report.Documentation.Should().NotBeNull();

        report.ReasoningLog.Should().Contain(x => x.Contains("Fallback"));
        mcp.Calls.Should().Contain("code_review");
        mcp.Calls.Should().Contain("generate_docs");
    }

    /// <summary>
    /// Создаёт экземпляр <see cref="PmAgent"/> для тестов
    /// </summary>
    private static PmAgent CreatePmAgent(IMcpClient mcp)
    {
        var codeReviewStub = new CodeReviewerAgent();
        var docsStub = new DocumentationAgent();

        return new PmAgent(
            mcp,
            codeReviewStub,
            docsStub,
            NullLogger<PmAgent>.Instance
        );
    }

    /// <summary>
    /// Создаёт типовой <see cref="PmOrchestrationRequest"/> для тестирования оркестрации
    /// </summary>
    private static PmOrchestrationRequest CreateRequest() => new()
    {
        PrTitle = "PR title",
        PrDescription = "PR description",
        Diff = "diff --git a/... b/...",
        ComponentName = "Component",
        ComponentDescription = "Component description"
    };
}
