using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;

namespace AiMultiAgent.Mcp.Client;

/// <summary>
/// Универсальный HTTP+SSE клиент для MCP.
/// Снаружи ты работаешь только с CallToolAsync / CallToolsListAsync
/// </summary>
public sealed class SseMcpClient(HttpClient http, IOptions<McpClientOptions> options) : IMcpClient
{
    private readonly HttpClient _http = http;
    private readonly McpClientOptions _options = options.Value;

    private static readonly JsonSerializer CamelCaseSerializer = JsonSerializer.Create(new JsonSerializerSettings
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver()
    });


    private const string JsonRpcVersion = "2.0";
    private const string ToolsCallMethod = "tools/call";
    private const string ToolsListMethod = "tools/list";

    /// <summary>
    /// Базовый отправитель JSON-RPC запроса к MCP. 
    /// Ожидает объект, который уже приведён к форме jsonrpc/id/method/params
    /// Возвращает JToken с РАСПАРСЕННЫМ JSON из строки "data: {...}".
    /// </summary>
    public async Task<JToken> SendAsync(object payload, CancellationToken ct = default)
    {
        const int maxRetries = 3;          // макс. попыток
        const int delayBetweenRetriesMs = 2000; // пауза между попытками
        int attempt = 0;

        while (true)
        {
            attempt++;

            try
            {
                var json = JsonConvert.SerializeObject(payload);

                var request = new HttpRequestMessage(HttpMethod.Post, _options.EndpointPath);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = await _http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct
                );

                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var reader = new StreamReader(stream);

                string? line;
                while ((line = await reader.ReadLineAsync(ct)) != null)
                {
                    if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var jsonPart = line["data:".Length..].Trim();
                    return JToken.Parse(jsonPart);
                }

                throw new InvalidOperationException("MCP response does not contain 'data:' line");
            }
            catch (Exception ex) when (
                ex is TaskCanceledException ||
                ex is IOException ||
                ex.InnerException is SocketException)
            {
                if (attempt >= maxRetries || ct.IsCancellationRequested)
                    throw; // не можем повторить больше раз или токен отменён

                // логируем попытку
                Console.WriteLine($"Attempt {attempt} failed: {ex.Message}. Retrying in {delayBetweenRetriesMs}ms...");

                await Task.Delay(delayBetweenRetriesMs, ct); // пауза перед повтором
            }
        }
    }


    /// <summary>
    /// Универсальный хелпер для tools/call.
    /// Принимает имя инструмента и произвольный объект с аргументами (анонимный тип, DTO, JObject).
    /// </summary>
    public Task<JToken> CallToolAsync(
        string toolName,
        object? arguments = null,
        string? jsonRpcId = null,
        CancellationToken ct = default)
    {
        jsonRpcId ??= Guid.NewGuid().ToString();

        var args = arguments switch
        {
            null => [],
            JObject j => j,
            _ => JObject.FromObject(arguments, CamelCaseSerializer)
        };

        var payload = new JObject
        {
            ["jsonrpc"] = JsonRpcVersion,
            ["id"] = jsonRpcId,
            ["method"] = ToolsCallMethod,
            ["params"] = new JObject
            {
                ["name"] = toolName,
                ["arguments"] = args
            }
        };

        return SendAsync(payload, ct);
    }

    /// <summary>
    /// Типизированный хелпер: парсит MCP-обёртку
    /// и достаёт JSON из result.content[0].json (или text, если там JSON-строка)
    /// </summary>
    public async Task<TResult?> CallToolAsync<TResult>(
        string toolName,
        object? arguments = null,
        string? jsonRpcId = null,
        CancellationToken ct = default)
    {
        var envelope = await CallToolAsync(toolName, arguments, jsonRpcId, ct);

        if (envelope["result"]?["content"] is not JArray content || content.Count == 0)
            return default;

        var first = content[0];

        // JSON-контент
        var jsonNode = first["json"];
        if (jsonNode is not null && jsonNode.Type != JTokenType.Null)
            return jsonNode.ToObject<TResult>();

        // На случай, если tool вернул text, а там лежит JSON-строка
        var textNode = first["text"];
        if (textNode is not null && textNode.Type == JTokenType.String)
        {
            var text = textNode.Value<string>()!;
            try
            {
                return JsonConvert.DeserializeObject<TResult>(text);
            }
            catch
            {
                return default;
            }
        }

        throw new InvalidOperationException("MCP tool response does not contain 'json' or JSON-parsable 'text' content.");
    }

    /// <summary>
    /// Хелпер для tools/list, возвращает сырой JToken
    /// </summary>
    public Task<JToken> CallToolsListAsync(
        string? jsonRpcId = null,
        CancellationToken ct = default)
    {
        jsonRpcId ??= Guid.NewGuid().ToString();

        var payload = new JObject
        {
            ["jsonrpc"] = JsonRpcVersion,
            ["id"] = jsonRpcId,
            ["method"] = ToolsListMethod,
            ["params"] = new JObject()
        };

        return SendAsync(payload, ct);
    }
}
