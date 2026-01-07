using FluentAssertions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace AiMultiAgent.Mcp.Client.Tests;

/// <summary>
/// Набор юнит-тестов для <see cref="SseMcpClient"/>.
/// Реальный MCP Server в этих тестах НЕ запускается!
/// Мы подменяем сеть через <see cref="FakeHttpMessageHandler"/>, который возвращает заранее
/// подготовленный SSE-ответ, тестируется логика клиента
/// </summary>
public class SseMcpClientTests
{
    /// <summary>
    /// Фейковый <see cref="HttpMessageHandler"/> для unit-тестов.
    /// Перехватывает HTTP-запросы <see cref="HttpClient"/> и возвращает заранее заданный ответ,
    /// позволяя тестировать код без поднятого сервера и без сети
    /// </summary>
    private class FakeHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }

        /// <summary>
        /// Создаёт HTTP-ответ с типом "text/event-stream", имитирующий SSE-стрим от MCP сервера
        /// </summary>
        public static HttpResponseMessage CreateSseResponse(string body)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
            };
        }
    }

    /// <summary>
    /// Проверяет, что <see cref="SseMcpClient.SendAsync(object, string, CancellationToken)"/>
    /// корректно парсит первую строку "data: ..." из SSE-ответа и возвращает JSON-результат
    /// </summary>
    [Fact]
    public async Task SendAsync_Should_Parse_First_Data_Line_From_Sse_Response()
    {
        var sseResponse =
            "event: message\n" +
            "data: {\"hello\":\"world\"}\n\n";

        var handler = new FakeHttpMessageHandler(_ =>
            FakeHttpMessageHandler.CreateSseResponse(sseResponse)
        );

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };

        var options = Options.Create(new McpClientOptions
        {
            EndpointPath = "/mcp"
        });

        var client = new SseMcpClient(httpClient, options);
        var result = await client.SendAsync(new { ping = "pong" });

        result.Should().NotBeNull();
        result["hello"]!.Value<string>().Should().Be("world");
    }

    /// <summary>
    /// Проверяет, что <see cref="SseMcpClient.SendAsync(object, CancellationToken)"/>
    /// игнорирует строки, не начинающиеся с "data:", и берёт первую подходящую "data:" строку
    /// </summary>
    [Fact]
    public async Task SendAsync_Should_Ignore_NonData_Lines_And_Take_First_Data_Line()
    {
        var sseResponse =
            "id: 1\n" +
            "retry: 1000\n" +
            "data: {\"a\":1}\n" +
            "data: {\"a\":2}\n\n";

        var client = CreateClientReturningSse(sseResponse);

        var result = await client.SendAsync(new { });

        result["a"]!.Value<int>().Should().Be(1);
    }

    /// <summary>
    /// Проверяет, что <see cref="SseMcpClient.SendAsync(object, CancellationToken)"/>
    /// бросает исключение, если в SSE-ответе нет ни одной строки "data:"
    /// </summary>
    [Fact]
    public async Task SendAsync_Should_Throw_When_Data_Line_Is_Missing()
    {
        var sseResponse =
            "event: message\n" +
            "id: 1\n\n";

        var client = CreateClientReturningSse(sseResponse);

        await FluentActions.Invoking(() => client.SendAsync(new { }))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not contain 'data:'*");
    }

    /// <summary>
    /// Проверяет, что <see cref="SseMcpClient.CallToolAsync(string, object?, string?, CancellationToken)"/>
    /// формирует корректный JSON-RPC payload
    /// и отправляет его через HTTP POST на указанный EndpointPath
    /// </summary>
    [Fact]
    public async Task CallToolAsync_Should_Send_Valid_JsonRpc_Payload()
    {
        string? capturedBody = null;
        string? capturedPath = null;
        HttpMethod? capturedMethod = null;
        MediaTypeWithQualityHeaderValue[]? capturedAccept = null;

        var sseOk = "data: {\"jsonrpc\":\"2.0\",\"id\":\"1\",\"result\":{\"content\":[{\"json\":{}}]}}\n\n";

        var client = CreateClientWithHandler(req =>
        {
            capturedPath = req.RequestUri!.ToString();
            capturedMethod = req.Method;
            capturedAccept = req.Headers.Accept.ToArray();
            capturedBody = req.Content!.ReadAsStringAsync().Result;

            return FakeHttpMessageHandler.CreateSseResponse(sseOk);
        });

        await client.CallToolAsync("code_review", new { title = "test" }, jsonRpcId: "1");

        capturedMethod.Should().Be(HttpMethod.Post);
        capturedPath.Should().EndWith("/mcp");

        capturedAccept.Should().Contain(a => a.MediaType == "application/json");
        capturedAccept.Should().Contain(a => a.MediaType == "text/event-stream");

        capturedBody.Should().NotBeNull();
        var json = JObject.Parse(capturedBody!);

        json["jsonrpc"]!.Value<string>().Should().Be("2.0");
        json["id"]!.Value<string>().Should().Be("1");
        json["method"]!.Value<string>().Should().Be("tools/call");
        json["params"]!["name"]!.Value<string>().Should().Be("code_review");
        json["params"]!["arguments"]!["title"]!.Value<string>().Should().Be("test");
    }

    /// <summary>
    /// Проверяет, что типизированный <see cref="SseMcpClient.CallToolAsync{TResult}(string, object?, string?, CancellationToken)"/>
    /// умеет извлекать результат из result.content[0].json
    /// </summary>
    [Fact]
    public async Task CallToolAsync_Typed_Should_Read_Result_Content_Json()
    {
        var sse = "data: {\"jsonrpc\":\"2.0\",\"id\":\"1\",\"result\":{\"content\":[{\"json\":{\"x\":42}}]}}\n\n";

        var client = CreateClientReturningSse(sse);

        var dto = await client.CallToolAsync<MyDto>("any_tool", jsonRpcId: "1");

        dto.Should().NotBeNull();
        dto!.X.Should().Be(42);
    }

    /// <summary>
    /// Проверяет, что типизированный <see cref="SseMcpClient.CallToolAsync{TResult}(string, object?, string?, CancellationToken)"/>
    /// умеет распарсить JSON-строку из result.content[0].text (fallback-ветка).
    /// </summary>
    [Fact]
    public async Task CallToolAsync_Typed_Should_Parse_Json_From_Text_When_Json_Node_Is_Missing()
    {
        var sse = "data: {\"jsonrpc\":\"2.0\",\"id\":\"1\",\"result\":{\"content\":[{\"text\":\"{\\\"x\\\":7}\"}]}}\n\n";

        var client = CreateClientReturningSse(sse);

        var dto = await client.CallToolAsync<MyDto>("any_tool", jsonRpcId: "1");

        dto.Should().NotBeNull();
        dto!.X.Should().Be(7);
    }

    /// <summary>
    /// Проверяет, что типизированный <see cref="SseMcpClient.CallToolAsync{TResult}(string, object?, string?, CancellationToken)"/>
    /// возвращает default, если content отсутствует или пустой
    /// </summary>
    [Fact]
    public async Task CallToolAsync_Typed_Should_Return_Default_When_Content_Is_Missing()
    {
        var sse = "data: {\"jsonrpc\":\"2.0\",\"id\":\"1\",\"result\":{}}\n\n";

        var client = CreateClientReturningSse(sse);

        var dto = await client.CallToolAsync<MyDto>("any_tool", jsonRpcId: "1");

        dto.Should().BeNull();
    }

    /// <summary>
    /// Проверяет, что типизированный <see cref="SseMcpClient.CallToolAsync{TResult}(string, object?, string?, CancellationToken)"/>
    /// бросает исключение, если content[0] не содержит ни 'json', ни JSON-парсибельного 'text'
    /// </summary>
    [Fact]
    public async Task CallToolAsync_Typed_Should_Throw_When_No_Json_And_No_Parsable_Text()
    {
        var sse = "data: {\"jsonrpc\":\"2.0\",\"id\":\"1\",\"result\":{\"content\":[{\"text\":123}]}}\n\n";

        var client = CreateClientReturningSse(sse);

        await FluentActions.Invoking(() => client.CallToolAsync<MyDto>("any_tool", jsonRpcId: "1"))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not contain 'json'*");
    }

    /// <summary>
    /// Проверяет, что <see cref="SseMcpClient.CallToolsListAsync(string?, CancellationToken)"/>
    /// формирует JSON-RPC запрос с method/tools/list
    /// </summary>
    [Fact]
    public async Task CallToolsListAsync_Should_Send_ToolsList_Request()
    {
        string? capturedBody = null;

        var sseOk = "data: {\"jsonrpc\":\"2.0\",\"id\":\"99\",\"result\":{\"tools\":[]}}\n\n";

        var client = CreateClientWithHandler(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().Result;
            return FakeHttpMessageHandler.CreateSseResponse(sseOk);
        });

        await client.CallToolsListAsync(jsonRpcId: "99");

        capturedBody.Should().NotBeNull();
        var json = JObject.Parse(capturedBody!);

        json["jsonrpc"]!.Value<string>().Should().Be("2.0");
        json["id"]!.Value<string>().Should().Be("99");
        json["method"]!.Value<string>().Should().Be("tools/list");
    }

    /// <summary>
    /// Простой DTO для проверки типизированных вызовов <see cref="SseMcpClient.CallToolAsync{TResult}"/>
    /// </summary>
    private sealed class MyDto
    {
        public int X { get; set; }
    }

    /// <summary>
    /// Создаёт <see cref="SseMcpClient"/> с фейковым HTTP-ответом SSE
    /// </summary>
    private static SseMcpClient CreateClientReturningSse(string sseBody)
        => CreateClientWithHandler(_ => FakeHttpMessageHandler.CreateSseResponse(sseBody));

    /// <summary>
    /// Создаёт <see cref="SseMcpClient"/> с пользовательским обработчиком HTTP-запросов
    /// </summary>
    private static SseMcpClient CreateClientWithHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new FakeHttpMessageHandler(responder);

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };

        var options = Options.Create(new McpClientOptions
        {
            EndpointPath = "/mcp"
        });

        return new SseMcpClient(httpClient, options);
    }
}
