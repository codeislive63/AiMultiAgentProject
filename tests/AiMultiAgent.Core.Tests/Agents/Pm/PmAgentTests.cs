using AiMultiAgent.Core.Agents.CodeReview;
using AiMultiAgent.Core.Agents.Documentation;
using AiMultiAgent.Core.Agents.Pm;
using AiMultiAgent.Core.Agents.Pm.Llm;
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
    /// Фейковая реализация <see cref="IMcpClient"/> для тестов
    /// </summary>
    private sealed class FakeMcpClient : IMcpClient
    {
        private readonly Dictionary<string, object?> _typedResults = [];
        private readonly Dictionary<string, Exception> _exceptions = [];

        /// <summary>
        /// Список вызовов tool-ов MCP
        /// </summary>
        public List<(string ToolName, object? Args)> Calls { get; } = [];

        /// <summary>
        /// Настраивает типизированный результат для tool-а
        /// </summary>
        public FakeMcpClient WithTypedResult(string toolName, object? result)
        {
            _typedResults[toolName] = result;
            return this;
        }

        /// <summary>
        /// Настраивает исключение для tool-а
        /// </summary>
        public FakeMcpClient WithException(string toolName, Exception ex)
        {
            _exceptions[toolName] = ex;
            return this;
        }

        // ----------------------------
        // НЕ ИСПОЛЬЗУЕТСЯ ДЛЯ ТЕСТИРОВАНИЯ
        // ----------------------------

        /// <summary>
        /// Нетипизированный вызов tool-а
        /// </summary>
        public Task<JToken> CallToolAsync(string toolName, object? arguments = null, string? jsonRpcId = null,
            CancellationToken ct = default)
            => Task.FromResult<JToken>(new JObject());

        /// <summary>
        /// Типизированный вызов tool-а
        /// </summary>
        public Task<TResult?> CallToolAsync<TResult>(string toolName, object? arguments = null, string? jsonRpcId = null,
            CancellationToken ct = default)
        {
            Calls.Add((toolName, arguments));

            if (_exceptions.TryGetValue(toolName, out var ex))
                throw ex;

            if (_typedResults.TryGetValue(toolName, out var value))
                return Task.FromResult((TResult?)value);

            return Task.FromResult<TResult?>(default);
        }

        /// <summary>
        /// Возвращает список tools
        /// </summary>
        public Task<JToken> CallToolsListAsync(string? jsonRpcId = null, CancellationToken ct = default)
            => Task.FromResult<JToken>(new JObject());
    }

    /// <summary>
    /// Фейковый planner для тестов
    /// </summary>
    private sealed class FakePlanner(
        PmPlan plan,
        Func<PmOrchestrationRequest, object, List<TraceEvent>, PmOrchestrationReport> aggregate) : IPmPlanner
    {
        private readonly PmPlan _plan = plan;
        private readonly Func<PmOrchestrationRequest, object, List<TraceEvent>, PmOrchestrationReport> _aggregate = aggregate;

        /// <summary>
        /// Счётчик вызовов <see cref="CreatePlanAsync"/>
        /// </summary>
        public int CreatePlanCalls { get; private set; }

        /// <summary>
        /// Счётчик вызовов <see cref="AggregateAsync"/>
        /// </summary>
        public int AggregateCalls { get; private set; }

        /// <summary>
        /// Возвращает заранее заданный план
        /// </summary>
        public Task<PmPlan> CreatePlanAsync(PmOrchestrationRequest req, CancellationToken ct)
        {
            CreatePlanCalls++;
            return Task.FromResult(_plan);
        }

        /// <summary>
        /// Возвращает результат агрегации
        /// </summary>
        public Task<PmOrchestrationReport> AggregateAsync(
            PmOrchestrationRequest req,
            object toolResults,
            List<TraceEvent> traces,
            CancellationToken ct)
        {
            AggregateCalls++;
            return Task.FromResult(_aggregate(req, toolResults, traces));
        }
    }

    /// <summary>
    /// Оба MCP tool-а отработали, отчёт заполнен
    /// </summary>
    [Fact]
    public async Task OrchestrateAsync_HappyPath_Should_Call_Mcp_Tools_And_Return_Filled_Report()
    {
        var mcp = new FakeMcpClient()
            .WithTypedResult("code_review", new CodeReviewResult
            {
                Summary = "mcp review",
                Issues = [],
                Suggestions = ["s1"]
            })
            .WithTypedResult("generate_docs", new DocumentationResult
            {
                Markdown = "mcp docs",
                UmlPlantUml = "@startuml\n@enduml"
            });

        var plan = new PmPlan
        {
            Objective = "Test objective",
            Steps =
            [
                new PmPlanStep { Id = "1", Tool = "code_review", Arguments = new { anything = 1 }, OnFail = "continue" },
                new PmPlanStep { Id = "2", Tool = "generate_docs", Arguments = new { anything = 2 }, OnFail = "continue" }
            ]
        };

        var planner = new FakePlanner(
            plan,
            aggregate: (_, __, ___) =>
                new PmOrchestrationReport
                {
                    Summary = "LLM aggregation summary",
                    Risks = [],
                    NextActions = []
                }
            );

        var agent = CreatePmAgent(mcp, planner);
        var req = CreateRequest();

        var report = await agent.OrchestrateAsync(req);

        planner.CreatePlanCalls.Should().Be(1);
        planner.AggregateCalls.Should().Be(1);

        mcp.Calls.Select(c => c.ToolName)
                 .Should()
                 .ContainInOrder("code_review", "generate_docs");

        report.Should().NotBeNull();
        report.Summary.Should().Be("LLM aggregation summary");
        report.Trace.Should().NotBeNull();
        report.Trace.Should().NotBeEmpty();

        report.Meta.Should().NotBeNull();
        report.Meta.Should().BeOfType<Dictionary<string, object?>>();
        var meta = report.Meta;
        meta["aggregation"].Should().Be("llm");
        meta["steps"].Should().Be(2);
        meta["objective"].Should().Be("Test objective");

        report.ToolResults.Should().NotBeNull();
        report.ToolResults.Should().BeOfType<Dictionary<string, object?>>();
        var toolResults = report.ToolResults;

        toolResults.Should().ContainKey("1");
        toolResults.Should().ContainKey("2");

        var cr = toolResults["1"].Should().BeOfType<CodeReviewResult>().Subject;
        cr.Summary.Should().Be("mcp review");

        var docs = toolResults["2"].Should().BeOfType<DocumentationResult>().Subject;
        docs.Markdown.Should().Be("mcp docs");

        toolResults["code_review_usedFallback"].Should().Be(false);
        toolResults["generate_docs_usedFallback"].Should().Be(false);

        report.Trace.Select(t => t.Type).Should().Contain("TOOL_CALL_START");
        report.Trace.Select(t => t.Type).Should().Contain("TOOL_CALL_END");
        report.Trace.Select(t => t.Type).Should().Contain("REASONING");
    }

    /// <summary>
    /// MCP code_review падает, но пайплайн продолжается
    /// </summary>
    [Fact]
    public async Task OrchestrateAsync_When_CodeReview_Mcp_Fails_Should_Fallback_And_Continue()
    {
        var mcp = new FakeMcpClient()
            .WithException("code_review", new HttpRequestException("mcp down"))
            .WithTypedResult("generate_docs", new DocumentationResult
            {
                Markdown = "mcp docs",
                UmlPlantUml = "@startuml\n@enduml"
            });

        var plan = new PmPlan
        {
            Objective = "Test objective",
            Steps =
            [
                new PmPlanStep { Id = "1", Tool = "code_review", Arguments = new { }, OnFail = "continue" },
                new PmPlanStep { Id = "2", Tool = "generate_docs", Arguments = new { }, OnFail = "continue" }
            ]
        };

        var planner = new FakePlanner(
            plan,
            aggregate: (_, __, ___) => new PmOrchestrationReport
            {
                Summary = "LLM aggregation summary",
                Risks = [],
                NextActions = []
            }
        );

        var agent = CreatePmAgent(mcp, planner);
        var req = CreateRequest();

        var report = await agent.OrchestrateAsync(req);

        var toolResults = report.ToolResults;

        toolResults["code_review_usedFallback"].Should().Be(true);
        toolResults["generate_docs_usedFallback"].Should().Be(false);

        toolResults["1"].Should().NotBeNull();

        toolResults["2"].Should().BeOfType<DocumentationResult>();
        ((DocumentationResult)toolResults["2"]!).Markdown.Should().Be("mcp docs");

        report.Trace.Any(t => (t.Details ?? "").Contains("Fallback", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue();
    }

    /// <summary>
    /// Fallback: MCP generate_docs падает, но пайплайн продолжается.
    /// </summary>
    [Fact]
    public async Task OrchestrateAsync_When_GenerateDocs_Mcp_Fails_Should_Fallback_And_Continue()
    {
        var mcp = new FakeMcpClient()
            .WithTypedResult("code_review", new CodeReviewResult
            {
                Summary = "mcp review",
                Issues = [],
                Suggestions = []
            })
            .WithException("generate_docs", new TimeoutException("timeout"));

        var plan = new PmPlan
        {
            Objective = "Test objective",
            Steps =
            [
                new PmPlanStep { Id = "1", Tool = "code_review", Arguments = new { }, OnFail = "continue" },
                new PmPlanStep { Id = "2", Tool = "generate_docs", Arguments = new { }, OnFail = "continue" }
            ]
        };

        var planner = new FakePlanner(
            plan,
            aggregate: (_, __, ___) => new PmOrchestrationReport
            {
                Summary = "LLM aggregation summary",
                Risks = [],
                NextActions = []
            });

        var agent = CreatePmAgent(mcp, planner);
        var req = CreateRequest();

        var report = await agent.OrchestrateAsync(req);

        var toolResults = report.ToolResults;

        toolResults["code_review_usedFallback"].Should().Be(false);
        toolResults["generate_docs_usedFallback"].Should().Be(true);

        toolResults["1"].Should().BeOfType<CodeReviewResult>();
        toolResults["2"].Should().NotBeNull();

        report.Trace.Any(t => (t.Details ?? "").Contains("Fallback", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue();
    }


    /// <summary>
    /// Создаёт экземпляр <see cref="PmAgent"/> для тестов
    /// </summary>
    private static PmAgent CreatePmAgent(IMcpClient mcp, IPmPlanner planner)
    {
        var codeReviewStub = new CodeReviewerAgent();
        var docsStub = new DocumentationAgent();

        return new PmAgent(
            mcp,
            codeReviewStub,
            docsStub,
            planner,
            NullLogger<PmAgent>.Instance
        );
    }

    /// <summary>
    /// Создаёт тестовый <see cref="PmOrchestrationRequest"/>
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
