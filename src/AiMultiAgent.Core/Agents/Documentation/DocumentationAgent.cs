using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using PlantUml.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiMultiAgent.Core.Agents.Documentation;

/// <summary>
/// Агент для генерации документации компонента с помощью LLM.
/// Генерирует Markdown, структурированный JSON и PlantUML диаграммы.
/// При ошибках LLM использует fallback на заглушку.
/// </summary>
public sealed class DocumentationAgent
{
    private readonly IChatClient? _chat;
    private readonly ILogger<DocumentationAgent>? _log;

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Общие настройки JSON-сериализации для десериализации ответов LLM
    /// </summary>
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Создаёт новый экземпляр агента документации с LLM-интеграцией.
    /// </summary>
    public DocumentationAgent(IChatClient chat, ILogger<DocumentationAgent> log)
    {
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Конструктор без зависимостей для fallback-режима.
    /// </summary>
    public DocumentationAgent()
    {
    }

    /// <summary>
    /// Генерирует документацию компонента: Markdown, структурированный JSON и PlantUML диаграмму.
    /// При ошибках LLM возвращает заглушку.
    /// </summary>
    public async Task<DocumentationResult> GenerateAsync(
        string componentName,
        string description,
        CancellationToken ct = default)
    {
        if (_chat == null || _log == null)
        {
            _log?.LogWarning("DocumentationAgent: LLM не настроен, используется fallback-заглушка");
            return await CreateFallbackResultAsync(componentName, description, ct);
        }

        try
        {
            _log.LogInformation("Documentation generation started for component {Component}", componentName);

            // Генерируем Markdown и JSON одним запросом
            var (markdown, jsonData) = await GenerateMarkdownAndJsonAsync(componentName, description, ct);

            // Генерируем PlantUML диаграмму отдельным запросом
            var uml = await GeneratePlantUmlAsync(componentName, description, ct);

            // Конвертируем PlantUML текст в изображение PNG
            var umlImageBase64 = await ConvertPlantUmlToImageAsync(uml, ct);

            _log.LogInformation("Documentation generation completed for component {Component}", componentName);

            return new DocumentationResult
            {
                Markdown = markdown,
                UmlPlantUml = uml,
                UmlPlantUmlImageBase64 = umlImageBase64,
                StructuredJson = jsonData
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "LLM documentation generation failed for {Component}, using fallback", componentName);
            return await CreateFallbackResultAsync(componentName, description, ct);
        }
    }

    /// <summary>
    /// Генерирует Markdown-документацию и структурированный JSON.
    /// </summary>
    private async Task<(string Markdown, DocumentationJsonData JsonData)> GenerateMarkdownAndJsonAsync(
        string componentName,
        string description,
        CancellationToken ct)
    {
        var system = """
            You are a technical documentation writer. Generate comprehensive documentation for a software component.
            
            Return ONLY valid JSON. No markdown, no code fences, no extra text.
            
            Schema:
            {
              "markdown": string,
              "jsonData": {
                "componentName": string,
                "description": string,
                "responsibilities": [ string ],
                "keyMethods": [ string ],
                "dependencies": [ string ],
                "notes": string | null
              }
            }
            
            Rules:
            - markdown: Full Markdown documentation with sections like Overview, Responsibilities, Usage, etc.
            - jsonData.responsibilities: List of main responsibilities (3-7 items)
            - jsonData.keyMethods: List of key public methods/interfaces (if applicable)
            - jsonData.dependencies: List of dependencies or related components (if applicable)
            - jsonData.notes: Optional additional notes
            """;

        var user = $"""
            Component Name: {componentName}
            Description: {description}
            
            Generate comprehensive Markdown documentation and structured JSON data for this component.
            """;

        var json = await AskJsonAsync(system, user, ct);
        var response = JsonSerializer.Deserialize<DocumentationLlmResponse>(
            json,
            JsonSerializerOptions
        );

        if (response == null || string.IsNullOrWhiteSpace(response.Markdown))
        {
            throw new InvalidOperationException("LLM returned invalid documentation response");
        }

        return (response.Markdown, response.JsonData ?? new DocumentationJsonData
        {
            ComponentName = componentName,
            Description = description
        });
    }

    /// <summary>
    /// Генерирует PlantUML диаграмму на основе описания компонента.
    /// </summary>
    private async Task<string> GeneratePlantUmlAsync(
        string componentName,
        string description,
        CancellationToken ct)
    {
        var system = """
            You are a UML diagram generator. Generate PlantUML class diagrams.
            
            Return ONLY valid PlantUML code. Start with @startuml and end with @enduml.
            No markdown, no code fences, no explanations, no backticks.
            
            Rules:
            - Use standard PlantUML syntax
            - Create class diagrams that represent the component structure
            - Include relationships if applicable (-->, <--, etc.)
            - Keep it concise but informative
            - Use simple class names and methods
            """;

        var user = $"""
            Component Name: {componentName}
            Description: {description}
            
            Generate a PlantUML class diagram for this component.
            """;

        try
        {
            var messages = new[]
            {
                new ChatMessage(ChatRole.System, system),
                new ChatMessage(ChatRole.User, user)
            };

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_timeout);

            var response = await _chat!.GetResponseAsync(messages, cancellationToken: timeout.Token);
            var text = response.Text ?? response.Messages?.LastOrDefault()?.Text;

            var extracted = ExtractPlantUml(text);
            if (!string.IsNullOrWhiteSpace(extracted))
            {
                return extracted;
            }

            _log?.LogWarning("Invalid PlantUML from LLM, using fallback");
            return CreateFallbackUml(componentName);
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "PlantUML generation failed, using fallback");
            return CreateFallbackUml(componentName);
        }
    }

    /// <summary>
    /// Извлекает PlantUML код из ответа LLM.
    /// </summary>
    private static string ExtractPlantUml(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        text = text.Trim();

        // Ищем блок между @startuml и @enduml
        var startIdx = text.IndexOf("@startuml", StringComparison.OrdinalIgnoreCase);
        var endIdx = text.IndexOf("@enduml", StringComparison.OrdinalIgnoreCase);

        if (startIdx < 0 || endIdx < 0 || endIdx <= startIdx)
        {
            return string.Empty;
        }

        var extracted = text.Substring(startIdx, endIdx - startIdx + "@enduml".Length);

        // Убираем markdown code fences если есть
        extracted = extracted.Replace("```plantuml", string.Empty, StringComparison.OrdinalIgnoreCase)
                             .Replace("```uml", string.Empty, StringComparison.OrdinalIgnoreCase)
                             .Replace("```", string.Empty);

        return extracted.Trim();
    }

    /// <summary>
    /// Конвертирует PlantUML текст в PNG изображение (base64 строка).
    /// Использует PlantUML.Net библиотеку.
    /// </summary>
    private async Task<string?> ConvertPlantUmlToImageAsync(string? plantUmlText, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(plantUmlText))
        {
            return null;
        }

        try
        {
            var factory = new RendererFactory();
            var renderer = factory.CreateRenderer(new PlantUmlSettings());

            var imageBytes = await renderer.RenderAsync(plantUmlText, OutputFormat.Png, ct);

            if (imageBytes == null || imageBytes.Length == 0)
            {
                _log?.LogWarning("PlantUML renderer returned empty image");
                return null;
            }

            return Convert.ToBase64String(imageBytes);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Failed to convert PlantUML to image");
            return null;
        }
    }

    /// <summary>
    /// Отправляет запрос LLM и получает JSON-ответ.
    /// При ошибке пытается исправить один раз.
    /// </summary>
    private async Task<string> AskJsonAsync(string system, string user, CancellationToken ct)
    {
        var messages = new[]
        {
            new ChatMessage(ChatRole.System, system),
            new ChatMessage(ChatRole.User, user)
        };

        // Создаём отдельный токен для первого вызова
        using var firstTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        firstTimeout.CancelAfter(_timeout);

        var response = await _chat!.GetResponseAsync(messages, cancellationToken: firstTimeout.Token);
        var text = response.Text ?? response.Messages?.LastOrDefault()?.Text;

        var extracted = TryExtractJson(text);
        if (extracted != null)
        {
            return extracted;
        }

        _log?.LogWarning("Invalid JSON from LLM. Attempting repair.");

        var repairMessages = new[]
        {
            new ChatMessage(ChatRole.System, system),
            new ChatMessage(
                ChatRole.User,
                "Fix the output. Return ONLY valid JSON that matches the schema.\n\nBAD_OUTPUT:\n" + text
            )
        };

        // Создаём отдельный токен для repair, чтобы он имел полный таймаут
        using var repairTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        repairTimeout.CancelAfter(_timeout);

        var repair = await _chat.GetResponseAsync(repairMessages, cancellationToken: repairTimeout.Token);
        extracted = TryExtractJson(repair.Text ?? repair.Messages?.LastOrDefault()?.Text);

        if (extracted == null)
        {
            _log?.LogError(
                "LLM failed to produce valid JSON after repair.\nOriginal:\n{Orig}\nRepaired:\n{Rep}",
                text,
                repair.Text
            );

            throw new InvalidOperationException("LLM failed to return valid documentation JSON");
        }

        return extracted;
    }

    /// <summary>
    /// Извлекает валидный JSON из ответа LLM.
    /// </summary>
    private static string? TryExtractJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = text.Trim();

        // Убираем markdown code fences
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstBrace = text.IndexOf('{');
            var firstBracket = text.IndexOf('[');

            var start = (firstBrace >= 0 && (firstBracket < 0 || firstBrace < firstBracket))
                ? firstBrace
                : firstBracket;

            if (start >= 0)
            {
                text = text[start..];
            }

            var lastBrace = text.LastIndexOf('}');
            var lastBracket = text.LastIndexOf(']');

            var end = Math.Max(lastBrace, lastBracket);

            if (end > 0)
            {
                text = text[..(end + 1)];
            }
        }

        var startIdx = text.IndexOf('{');
        var endIdx = text.LastIndexOf('}');

        if (startIdx < 0 || endIdx <= startIdx)
        {
            return null;
        }

        var json = text.Substring(startIdx, endIdx - startIdx + 1);

        try
        {
            JsonDocument.Parse(json);
            return json;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Создаёт fallback-результат при ошибке LLM.
    /// </summary>
    private async Task<DocumentationResult> CreateFallbackResultAsync(
        string componentName,
        string description,
        CancellationToken ct)
    {
        var markdown = $"""
            # {componentName}

            _Заглушка документации._

            {description}

            ## Что будет дальше

            В будущем сюда будет подставляться сгенерированная документация
            по коду и UML-диаграмма.
            """;

        var fallbackUml = CreateFallbackUml(componentName);
        var fallbackImage = await ConvertPlantUmlToImageAsync(fallbackUml, ct);

        return new DocumentationResult
        {
            Markdown = markdown,
            UmlPlantUml = fallbackUml,
            UmlPlantUmlImageBase64 = fallbackImage,
            StructuredJson = new DocumentationJsonData
            {
                ComponentName = componentName,
                Description = description
            }
        };
    }

    /// <summary>
    /// Создаёт простую fallback PlantUML диаграмму.
    /// </summary>
    private static string CreateFallbackUml(string componentName)
    {
        return $$"""
            @startuml
            class {{componentName}} {
                + Handle()
            }
            @enduml
            """.Replace("{{componentName}}", componentName);
    }

    /// <summary>
    /// Внутренний класс для десериализации ответа LLM.
    /// </summary>
    private sealed class DocumentationLlmResponse
    {
        [JsonPropertyName("markdown")]
        public string Markdown { get; init; } = default!;

        [JsonPropertyName("jsonData")]
        public DocumentationJsonData? JsonData { get; init; }
    }
}
