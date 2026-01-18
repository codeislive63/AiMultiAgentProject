using System.Text.Json.Serialization;

namespace AiMultiAgent.Core.Agents.Documentation;

/// <summary>
/// Результат генерации документации компонента
/// </summary>
public sealed class DocumentationResult
{
    /// <summary>
    /// Markdown-документация компонента
    /// </summary>
    public string Markdown { get; init; } = default!;

    /// <summary>
    /// PlantUML диаграмма в текстовом формате
    /// </summary>
    public string? UmlPlantUml { get; init; }

    /// <summary>
    /// Изображение PlantUML диаграммы в формате PNG (base64 строка)
    /// </summary>
    public string? UmlPlantUmlImageBase64 { get; init; }

    /// <summary>
    /// Структурированные JSON-данные о компоненте
    /// </summary>
    public DocumentationJsonData? StructuredJson { get; init; }
}

/// <summary>
/// Структурированные данные компонента в JSON-формате
/// </summary>
public sealed class DocumentationJsonData
{
    [JsonPropertyName("componentName")]
    public string ComponentName { get; init; } = default!;

    [JsonPropertyName("description")]
    public string Description { get; init; } = default!;

    [JsonPropertyName("responsibilities")]
    public List<string> Responsibilities { get; init; } = new();

    [JsonPropertyName("keyMethods")]
    public List<string> KeyMethods { get; init; } = new();

    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; init; } = new();

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}
