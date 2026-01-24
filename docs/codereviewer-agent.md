# Code Reviewer Agent

Code Reviewer Agent выполняет автоматическое code review **с помощью LLM** и возвращает **структурированный JSON**

---

## MCP Tool

- **Tool name:** `code_review`
- **Назначение:** выполнить ревью одного файла и вернуть `CodeReviewResult`

```csharp
[McpServerToolType]
public sealed class CodeReviewTools(CodeReviewerAgent agent)
{
    [McpServerTool(Name = "code_review", Title = "Сделать ревью коммита")]
    public Task<CodeReviewResult> ReviewAsync(
        [Description("Имя файла")] string fileName,
        [Description("Содержимое")] string data,
        CancellationToken ct = default)
    {
        return agent.ReviewAsync(fileName, data, ct);
    }
}
```

### Входные параметры

Минимальный контракт:

```json
{
  "fileName": "CodeReviewerAgent.cs",
  "data": "using ...\npublic sealed class CodeReviewerAgent { ... }"
}
```

Поля:
- `fileName` *(string, required)* — имя файла.
- `data` *(string, required)* — содержимое файла/код.

---

## Выходные данные 

```json
{
  "summary": "string",
  "issues": [
    {
      "severity": "info | warning | error",
      "title": "string",
      "details": "string"
    }
  ],
  "suggestions": ["string"]
}
```
