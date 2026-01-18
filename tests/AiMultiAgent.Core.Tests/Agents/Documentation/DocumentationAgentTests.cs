using AiMultiAgent.Core.Agents.Documentation;
using Xunit;

namespace AiMultiAgent.Core.Tests.Agents.Documentation;

/// <summary>
/// Unit-тесты для DocumentationAgent
/// </summary>
public sealed class DocumentationAgentTests
{
    private const string ComponentName = "TestComponent";
    private const string ComponentDescription = "Тестовый компонент для проверки генерации документации";

    /// <summary>
    /// Проверка fallback-режима без LLM (конструктор без зависимостей)
    /// </summary>
    [Fact]
    public async Task GenerateAsync_ReturnsFallbackResult_WhenLLMNotConfigured()
    {
        // Arrange - создаём агент без LLM зависимостей (fallback режим)
        var agent = new DocumentationAgent();

        // Act
        var result = await agent.GenerateAsync(ComponentName, ComponentDescription);

        // Assert
        Assert.NotNull(result);
        
        // Проверяем, что Markdown не пустой
        Assert.NotNull(result.Markdown);
        Assert.NotEmpty(result.Markdown);
        Assert.Contains(ComponentName, result.Markdown);
        Assert.Contains(ComponentDescription, result.Markdown);

        // Проверяем, что PlantUML диаграмма сгенерирована
        Assert.NotNull(result.UmlPlantUml);
        Assert.NotEmpty(result.UmlPlantUml);
        Assert.Contains("@startuml", result.UmlPlantUml);
        Assert.Contains("@enduml", result.UmlPlantUml);
        Assert.Contains(ComponentName, result.UmlPlantUml);

        // Проверяем, что StructuredJson заполнен
        Assert.NotNull(result.StructuredJson);
        Assert.Equal(ComponentName, result.StructuredJson.ComponentName);
        Assert.Equal(ComponentDescription, result.StructuredJson.Description);

        // Проверяем, что изображение PlantUML сгенерировано (base64 строка)
        Assert.NotNull(result.UmlPlantUmlImageBase64);
        Assert.NotEmpty(result.UmlPlantUmlImageBase64);
    }

    /// <summary>
    /// Проверка, что fallback-результат имеет корректную структуру
    /// </summary>
    [Fact]
    public async Task GenerateAsync_FallbackResult_HasValidStructure()
    {
        // Arrange
        var agent = new DocumentationAgent();

        // Act
        var result = await agent.GenerateAsync("MyService", "Сервис для обработки данных");

        // Assert
        Assert.NotNull(result);
        
        // Markdown должен содержать заголовок и описание
        Assert.Contains("# MyService", result.Markdown);
        
        // PlantUML должен быть валидным
        Assert.StartsWith("@startuml", result.UmlPlantUml!);
        Assert.EndsWith("@enduml", result.UmlPlantUml);
        
        // StructuredJson должен содержать базовые данные
        Assert.Equal("MyService", result.StructuredJson!.ComponentName);
        Assert.Equal("Сервис для обработки данных", result.StructuredJson.Description);

        // Проверяем, что изображение PlantUML сгенерировано
        Assert.NotNull(result.UmlPlantUmlImageBase64);
        Assert.NotEmpty(result.UmlPlantUmlImageBase64);
    }

    /// <summary>
    /// Проверка, что агент работает с разными именами компонентов
    /// </summary>
    [Theory]
    [InlineData("SimpleClass")]
    [InlineData("ComplexService")]
    [InlineData("ApiController")]
    [InlineData("Middleware")]
    public async Task GenerateAsync_WorksWithDifferentComponentNames(string componentName)
    {
        // Arrange
        var agent = new DocumentationAgent();

        // Act
        var result = await agent.GenerateAsync(componentName, "Test description");

        // Assert
        Assert.NotNull(result);
        Assert.Contains(componentName, result.Markdown);
        Assert.Contains(componentName, result.UmlPlantUml!);
        Assert.Equal(componentName, result.StructuredJson!.ComponentName);
    }
}