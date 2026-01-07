using Microsoft.Extensions.DependencyInjection;

namespace AiMultiAgent.Mcp.Client;

/// <summary>
/// Методы регистрации MCP-клиента в DI.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует SseMCP-клиент и его настройки
    /// </summary>
    public static IServiceCollection AddSseMcpClient(
        this IServiceCollection services,
        Action<McpClientOptions> configureOptions,
        Action<HttpClient>? configureHttpClient = null)
    {
        services.Configure(configureOptions);

        var http = services.AddHttpClient<SseMcpClient>();

        if (configureHttpClient is not null)
        {
            http.ConfigureHttpClient(configureHttpClient);
        }

        services.AddTransient<IMcpClient>(sp => sp.GetRequiredService<SseMcpClient>());

        return services;
    }
}
