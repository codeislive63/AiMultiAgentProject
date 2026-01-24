**PM Agent** — оркестратор, который:

- строит план действий через LLM Gemini,
- последовательно вызывает MCP tools других агентов: code_review для каждого файла, generate_docs для компонента,
- собирает результаты в единый отчёт, сохраняя reasoning и трассировку всех шагов

PM Agent **не выполняет анализ сам**, а управляет другими агентами и агрегирует результат

---

## MCP tool: 

- **Tool name:** `pm_report`
- **Назначение:** оркестрация `code_review`, `generate_docs` и агрегация в единый отчёт.

```csharp
[McpServerToolType]
public sealed class PmTools(PmAgent agent)
{
    /// <summary>
    /// Вызывает code_review и generate_docs,
    /// агрегирует результаты и возвращает единый отчёт
    /// </summary>
    [McpServerTool(Name = "pm_report", Title = "PM: оркестрация code_review + generate_docs")]
    public Task<PmReport> ReportAsync([Description("PM request DTO")] PmRequest request, CancellationToken ct = default) 
        => agent.OrchestrateAsync(request, ct);
}
```

### Входные параметры

PM Agent ожидает, что в запросе будут файлы. Минимально необходимое:

```json
{
  "componentName": "CodeReviewIssue",
  "componentDescription": "feat: add codereview agent",
  "files": [
    {
      "fileName": "CodeReviewIssue.cs",
      "data": "..."
    }
  ]
}
```

- **files** *(array, required)* – список файлов для ревью.
  - **fileName** *(string, required)* – имя файла.
  - **data** *(string, required)* – содержимое файла.
- **componentName** *(string, optional)* – имя компонента для генерации документации. Если не задано – PM попытается вывести имя из первого файла (по имени файла без расширения).
- **componentDescription** *(string, optional)* – описание компонента (если нет — PM формирует авто-описание из входных файлов).

---

## Выходные данные

```json
{
  "Meta": {
    "componentName": "CodeReviewIssue",
    "componentDescription": "feat: add codereview agent",
    "timestamp": "2026-01-22T20:25:21.1104697+00:00",
    "filesCount": 3,
    "aggregation": "llm"
  },
  "ToolResults": {
    "code_review": [
      {
        "fileName": "CodeReviewIssue.cs",
        "usedFallback": false,
        "truncated": false,
        "result": {
          "summary": "...",
          "issues": [{ "severity": "error", "title": "...", "details": "..." }],
          "suggestions": ["..."]
        }
      }
    ],
    "generate_docs": {
      "usedFallback": false,
      "result": {
        "markdown": "...",
        "umlPlantUml": "...",
        "umlPlantUmlImageBase64": null,
        "structuredJson": { "...": "..." }
      }
    }
  },
  "Risks": [ /* ... */ ],
  "NextActions": [ /* ... */ ],
  "Summary": "...",
  "Trace": [
    { "Ts": "...", "Type": "REASONING", "Tool": null, "Details": "..." },
    { "Ts": "...", "Type": "TOOL_CALL_START", "Tool": "code_review", "Details": "..." },
    { "Ts": "...", "Type": "TOOL_CALL_END", "Tool": "code_review", "Details": "ok" }
  ]
}
```
