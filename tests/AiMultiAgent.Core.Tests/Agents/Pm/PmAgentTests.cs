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
/// Проверяем:
/// - оркестрацию вызовов MCP tools
/// - обработку всех файлов (code_review на каждый файл)
/// - генерацию документации (generate_docs один раз)
/// - явный локальный fallback при сбоях MCP
/// </summary>
public sealed class PmAgentTests
{
    /// <summary>
    /// Фейковая реализация <see cref="IMcpClient"/> для тестов:
    /// - возвращает типизированные результаты для tool-ов
    /// - бросает исключение для tool-а
    /// - собирает историю вызовов (toolName + args)
    /// </summary>
    private sealed class FakeMcpClient : IMcpClient
    {
        private readonly Dictionary<string, object?> _typedResults = new();
        private readonly Dictionary<string, Exception> _exceptions = new();

        /// <summary>
        /// История вызовов MCP tools.
        /// </summary>
        public List<(string ToolName, object? Args)> Calls { get; } = new();

        /// <summary>
        /// Настраивает типизированный результат для tool-а.
        /// </summary>
        public FakeMcpClient WithTypedResult(string toolName, object? result)
        {
            _typedResults[toolName] = result;
            return this;
        }

        /// <summary>
        /// Настраивает исключение для tool-а.
        /// </summary>
        public FakeMcpClient WithException(string toolName, Exception ex)
        {
            _exceptions[toolName] = ex;
            return this;
        }

        /// <summary>
        /// Нетипизированный вызов tool-а (в этих тестах не используется).
        /// </summary>
        public Task<JToken> CallToolAsync(
            string toolName,
            object? arguments = null,
            string? jsonRpcId = null,
            CancellationToken ct = default)
            => Task.FromResult<JToken>(new JObject());

        /// <summary>
        /// Типизированный вызов tool-а.
        /// </summary>
        public Task<TResult?> CallToolAsync<TResult>(
            string toolName,
            object? arguments = null,
            string? jsonRpcId = null,
            CancellationToken ct = default)
        {
            Calls.Add((toolName, arguments));

            if (_exceptions.TryGetValue(toolName, out var ex))
            {
                throw ex;
            }

            if (_typedResults.TryGetValue(toolName, out var value))
            {
                return Task.FromResult((TResult?)value);
            }

            return Task.FromResult<TResult?>(default);
        }

        /// <summary>
        /// Возвращает список tools (в этих тестах не используется).
        /// </summary>
        public Task<JToken> CallToolsListAsync(string? jsonRpcId = null, CancellationToken ct = default)
            => Task.FromResult<JToken>(new JObject());
    }

    /// <summary>
    /// Фейковый planner для тестов.
    /// В текущей реализации <see cref="PmAgent"/> планирование не используется,
    /// поэтому проверяем только факт вызова AggregateAsync.
    /// </summary>
    private sealed class FakePlanner(
        Func<PmRequest, object, List<TraceEvent>, PmReport> aggregate) : IPmPlanner
    {
        private readonly Func<PmRequest, object, List<TraceEvent>, PmReport> _aggregate = aggregate;

        /// <summary>
        /// Счётчик вызовов <see cref="IPmPlanner.AggregateAsync"/>.
        /// </summary>
        public int AggregateCalls { get; private set; }

        /// <summary>
        /// Возвращает результат агрегации.
        /// </summary>
        public Task<PmReport> AggregateAsync(
            PmRequest req,
            object toolResults,
            List<TraceEvent> traces,
            CancellationToken ct)
        {
            AggregateCalls++;
            return Task.FromResult(_aggregate(req, toolResults, traces));
        }

        /// <summary>
        /// Планирование. Не используется в этих тестах.
        /// </summary>
        public Task<PmPlan> CreatePlanAsync(PmRequest req, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Оба MCP tool-а отработали успешно.
    /// Проверяем:
    /// - code_review вызван для каждого файла
    /// - generate_docs вызван один раз
    /// - toolResults содержит "code_review" (list) и "generate_docs" (object)
    /// - planner.AggregateAsync вызван один раз
    /// </summary>
    [Fact]
    public async Task OrchestrateAsync_HappyPath_Should_Call_Mcp_Tools_For_All_Files_And_Return_Filled_Report()
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

        var planner = new FakePlanner(
            aggregate: (_, __, ___) => new PmReport
            {
                Summary = "LLM aggregation summary",
                Risks = [],
                NextActions = []
            });

        var agent = CreatePmAgent(mcp, planner);
        var req = CreateRequest();

        var report = await agent.OrchestrateAsync(req);

        planner.AggregateCalls.Should().Be(1);

        mcp.Calls.Count(c => c.ToolName == "code_review").Should().Be(req.Files!.Count);
        mcp.Calls.Count(c => c.ToolName == "generate_docs").Should().Be(1);

        report.Should().NotBeNull();
        report.Summary.Should().Be("LLM aggregation summary");

        report.Trace.Should().NotBeNull();
        report.Trace.Should().NotBeEmpty();

        report.ToolResults.Should().NotBeNull();
        report.ToolResults.Should().ContainKey("code_review");
        report.ToolResults.Should().ContainKey("generate_docs");

        var crList = report.ToolResults["code_review"]
            .Should().BeOfType<List<object?>>().Subject;

        crList.Should().HaveCount(req.Files!.Count);

        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in crList)
        {
            var j = JObject.FromObject(item!);

            var fileName = j["fileName"]?.Value<string>();
            fileName.Should().NotBeNullOrWhiteSpace();
            fileNames.Add(fileName!);

            j["usedFallback"]?.Value<bool?>().Should().BeFalse();

            var resultObj = j["result"] as JObject;
            resultObj.Should().NotBeNull("каждый элемент code_review должен содержать объект result");

            resultObj!["Summary"]!.Value<string?>().Should().Be("mcp review");
        }

        fileNames.Should().BeEquivalentTo(req.Files!.Select(f => f.FileName));

        var docsObj = JObject.FromObject(report.ToolResults["generate_docs"]!);

        docsObj.ContainsKey("usedFallback").Should().BeTrue("PmAgent кладёт usedFallback в toolResults.generate_docs");
        docsObj.ContainsKey("result").Should().BeTrue("PmAgent кладёт result в toolResults.generate_docs");

        docsObj["usedFallback"]!.Value<bool?>().Should().BeFalse();

        var docsResult = docsObj["result"] as JObject;
        docsResult.Should().NotBeNull();

        docsResult!["Markdown"]!.Value<string?>().Should().Be("mcp docs");

        report.Trace.Select(t => t.Type).Should().Contain("TOOL_CALL_START");
        report.Trace.Select(t => t.Type).Should().Contain("TOOL_CALL_END");
        report.Trace.Select(t => t.Type).Should().Contain("REASONING");
    }

    /// <summary>
    /// MCP code_review падает, но пайплайн продолжается.
    /// Ожидаем:
    /// - для каждого файла будет элемент в code_review list
    /// - usedFallback=true для каждого элемента
    /// - generate_docs при этом успешен
    /// </summary>
    [Fact]
    public async Task OrchestrateAsync_When_CodeReview_Mcp_Fails_Should_Fallback_For_All_Files_And_Continue()
    {
        var mcp = new FakeMcpClient()
            .WithException("code_review", new HttpRequestException("mcp down"))
            .WithTypedResult("generate_docs", new DocumentationResult
            {
                Markdown = "mcp docs",
                UmlPlantUml = "@startuml\n@enduml"
            });

        var planner = new FakePlanner(
            aggregate: (_, __, ___) => new PmReport
            {
                Summary = "LLM aggregation summary",
                Risks = [],
                NextActions = []
            });

        var agent = CreatePmAgent(mcp, planner);
        var req = CreateRequest();

        var report = await agent.OrchestrateAsync(req);

        mcp.Calls.Count(c => c.ToolName == "code_review").Should().Be(req.Files!.Count);
        mcp.Calls.Count(c => c.ToolName == "generate_docs").Should().Be(1);

        var crList = report.ToolResults["code_review"]
            .Should().BeOfType<List<object?>>().Subject;

        crList.Should().HaveCount(req.Files!.Count);

        foreach (var item in crList)
        {
            var j = JObject.FromObject(item!);
            j["usedFallback"]!.Value<bool?>().Should().BeTrue();

            var resultObj = j["result"] as JObject;
            resultObj.Should().NotBeNull();

            resultObj!["Summary"]!.Value<string?>()
                .Should().Contain("Локальный отчёт code review", because: "fallback должен быть явным и читаемым");
        }

        var docsObj = JObject.FromObject(report.ToolResults["generate_docs"]!);
        docsObj["usedFallback"]!.Value<bool?>().Should().BeFalse();

        var docsResult = docsObj["result"] as JObject;
        docsResult.Should().NotBeNull();
        docsResult!["Markdown"]!.Value<string?>().Should().Be("mcp docs");

        report.Trace.Any(t => (t.Details ?? "").Contains("fallback", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue();
    }

    /// <summary>
    /// MCP generate_docs падает, но пайплайн продолжается.
    /// Ожидаем:
    /// - code_review успешен для всех файлов
    /// - generate_docs вернёт usedFallback=true и локальный markdown
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

        var planner = new FakePlanner(
            aggregate: (_, __, ___) => new PmReport
            {
                Summary = "LLM aggregation summary",
                Risks = new(),
                NextActions = new()
            });

        var agent = CreatePmAgent(mcp, planner);
        var req = CreateRequest();

        var report = await agent.OrchestrateAsync(req);

        mcp.Calls.Count(c => c.ToolName == "code_review").Should().Be(req.Files!.Count);
        mcp.Calls.Count(c => c.ToolName == "generate_docs").Should().Be(1);

        var crList = report.ToolResults["code_review"]
            .Should().BeOfType<List<object?>>().Subject;

        crList.Should().HaveCount(req.Files!.Count);

        foreach (var item in crList)
        {
            var j = JObject.FromObject(item!);
            j["usedFallback"]!.Value<bool?>().Should().BeFalse();

            var resultObj = j["result"] as JObject;
            resultObj.Should().NotBeNull();
            resultObj!["Summary"]!.Value<string?>().Should().Be("mcp review");
        }

        var docsObj = JObject.FromObject(report.ToolResults["generate_docs"]!);
        docsObj["usedFallback"]!.Value<bool?>().Should().BeTrue();

        var docsResult = docsObj["result"] as JObject;
        docsResult.Should().NotBeNull();
        docsResult!["Markdown"]!.Value<string?>()
            .Should().Contain("Локальная документация", because: "при падении инструмента должна быть локальная заглушка");

        report.Trace.Any(t => (t.Details ?? "").Contains("fallback", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue();
    }

    /// <summary>
    /// Некорректный запрос: files отсутствует или пустой
    /// </summary>
    [Fact]
    public async Task OrchestrateAsync_When_Files_Missing_Should_Throw_ArgumentException()
    {
        var mcp = new FakeMcpClient();
        var planner = new FakePlanner((_, __, ___) => new PmReport
        {
            Summary = "x",
            Risks = [],
            NextActions = []
        });

        var agent = CreatePmAgent(mcp, planner);

        var req = new PmRequest
        {
            Files = null,
            ComponentName = "C",
            ComponentDescription = "D"
        };

        var act = async () => await agent.OrchestrateAsync(req);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*files*");
    }

    /// <summary>
    /// Создаёт экземпляр <see cref="PmAgent"/> для тестов.
    /// </summary>
    private static PmAgent CreatePmAgent(IMcpClient mcp, IPmPlanner planner)
    {
        return new PmAgent(
            mcp,
            planner,
            NullLogger<PmAgent>.Instance
        );
    }

    /// <summary>
    /// Создаёт тестовый <see cref="PmRequest"/>.
    /// </summary>
    private static PmRequest CreateRequest() => new()
    {
        Files =
        [
            new PmFile { FileName = "1.txt", Data = "1asf asf asfx" },
            new PmFile { FileName = "2.txt", Data = "2zfasfd asf" },
        ],
        ComponentName = "Component",
        ComponentDescription = "Component description"
    };
}
