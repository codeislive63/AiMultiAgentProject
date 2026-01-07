using Newtonsoft.Json.Linq;

namespace AiMultiAgent.Mcp.Client;

/// <summary>
/// Контракт MCP-клиента для вызова и получение списка тулов
/// </summary>
public interface IMcpClient
{
    /// <summary>
    /// Вызывает tool и возвращает сырой JSON-ответ
    /// </summary>
    Task<JToken> CallToolAsync(
        string toolName,
        object? arguments = null,
        string? jsonRpcId = null,
        CancellationToken ct = default
    );

    /// <summary>
    /// Вызывает tool и возвращает типизированный результат
    /// </summary>
    Task<TResult?> CallToolAsync<TResult>(
        string toolName,
        object? arguments = null,
        string? jsonRpcId = null,
        CancellationToken ct = default
    );

    /// <summary>
    /// Возвращает список доступных тулов (tools/list)
    /// </summary>
    Task<JToken> CallToolsListAsync(string? jsonRpcId = null, CancellationToken ct = default);
}
