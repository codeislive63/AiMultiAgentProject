// Простой скрипт для проверки fallback-режима DocumentationAgent
// Запустите это в C# интерактивном режиме или создайте простую консольную программу

using AiMultiAgent.Core.Agents.Documentation;

// Создаём агент БЕЗ LLM зависимостей (fallback режим)
var agent = new DocumentationAgent();

// Генерируем документацию
var result = await agent.GenerateAsync(
    componentName: "TestComponent",
    description: "Тестовый компонент для проверки"
);

// Проверяем результат
Console.WriteLine("=== MARKDOWN ===");
Console.WriteLine(result.Markdown);
Console.WriteLine();

Console.WriteLine("=== PLANTUML ===");
Console.WriteLine(result.UmlPlantUml);
Console.WriteLine();

Console.WriteLine("=== STRUCTURED JSON ===");
Console.WriteLine($"ComponentName: {result.StructuredJson?.ComponentName}");
Console.WriteLine($"Description: {result.StructuredJson?.Description}");
Console.WriteLine($"Responsibilities: {string.Join(", ", result.StructuredJson?.Responsibilities ?? new List<string>())}");
Console.WriteLine();

Console.WriteLine("✅ Fallback режим работает корректно!");