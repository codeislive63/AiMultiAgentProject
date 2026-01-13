**PM Agent** — оркестратор, который:

- строит план действий через LLM Gemini,
- последовательно вызывает MCP tools,
- собирает результаты в единый отчёт, сохраняя reasoning и трассировку всех шагов

PM Agent **не выполняет анализ сам**, а управляет другими агентами и агрегирует результат

---

## MCP tool: `pm_report`

**Название tool:** `pm_report`

### Входные параметры

```json
{
  "prTitle": "Add PM LLM planning",
  "prDescription": "PM should plan and aggregate using Gemini",
  "diff": "diff --git a/a.cs b/a.cs\n+ test",
  "componentName": "PmAgent",
  "componentDescription": "PM orchestrates tools via MCP and uses LLM"
}
```

---

## Пример результата

```json
{
  "Meta": {
    "objective": "Implement and document PM LLM planning capabilities for aggregation using Gemini.",
    "steps": 2,
    "timestamp": "2026-01-08T19:51:01.4398605+03:00",
    "aggregation": "llm"
  },
  "ToolResults": {
    "1": {
      "summary": "Stub: обзор PR \"Add PM LLM planning\" ещё не реализован, но пайплайн работает.",
      "issues": [
        {
          "severity": "info",
          "title": "Заглушка ревью",
          "details": "Это заглушка. Здесь позже появится реальный анализ кода с помощью LLM."
        }
      ],
      "suggestions": [
        "Подключить реальную LLM-модель для анализа diff.",
        "Добавить проверку стиля и потенциальных багов."
      ]
    },
    "2": {
      "markdown": "# PmAgent\n\n_Заглушка документации._\n\nPM orchestrates tools via MCP and uses LLM",
      "umlPlantUml": "@startuml\nclass PmAgent Handle()\n@enduml"
    },
    "code_review_usedFallback": false,
    "generate_docs_usedFallback": false
  },
  "Risks": [
    {
      "Severity": "high",
      "Title": "Core functionality not implemented",
      "Details": "Tools are currently stubs and do not use real LLM analysis."
    }
  ],
  "NextActions": [
    {
      "Step": "1",
      "Action": "Integrate real LLM-based code review."
    }
  ],
  "Summary": "PM Agent successfully orchestrated MCP tools and aggregated results using LLM.",
  "Trace": [
    {
      "Ts": "2026-01-08T16:50:42.5839879+00:00",
      "Type": "REASONING",
      "Tool": null,
      "Details": "PM: старт. Запрашиваю у LLM план действий"
    },
    {
      "Ts": "2026-01-08T16:50:47.1778083+00:00",
      "Type": "TOOL_CALL_END",
      "Tool": "code_review",
      "Details": "ok"
    }
  ]
}
```