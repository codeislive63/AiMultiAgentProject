using AiMultiAgent.Core.Agents.Documentation;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AiMultiAgent.Mcp.Server.Tools.Documentation;

[McpServerToolType]
public sealed class DocumentationTools(DocumentationAgent agent)
{
    [McpServerTool(Name = "generate_docs", Title = "Сгенерировать документацию")]
    public Task<DocumentationResult> GenerateAsync(
        [Description("Имя компонента/сервиса")] string componentName,
        [Description("Краткое описание, что делает компонент")] string description,
        CancellationToken ct = default)
    {
        return agent.GenerateAsync(componentName, description, ct);
    }
}
