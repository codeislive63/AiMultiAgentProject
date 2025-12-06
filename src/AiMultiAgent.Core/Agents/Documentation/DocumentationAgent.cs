namespace AiMultiAgent.Core.Agents.Documentation;

public class DocumentationAgent
{
    public Task<DocumentationResult> GenerateAsync(
        string componentName,
        string description,
        CancellationToken ct = default)
    {
        var markdown = $"""
        # {componentName}

        _Заглушка документации._

        {description}

        ## Что будет дальше

        В будущем сюда будет подставляться сгенерированная документация
        по коду и UML-диаграмма.
        """;

        var uml = $"""
        @startuml
        class {componentName} {"Handle()"}
        @enduml
        """;

        return Task.FromResult(new DocumentationResult
        {
            Markdown = markdown,
            UmlPlantUml = uml
        });
    }
}
