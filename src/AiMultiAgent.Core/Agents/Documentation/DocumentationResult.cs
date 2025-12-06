namespace AiMultiAgent.Core.Agents.Documentation;

// Подумать над моделькой данных
public sealed class DocumentationResult
{
    public string Markdown { get; init; } = default!;
    public string? UmlPlantUml { get; init; }
}
