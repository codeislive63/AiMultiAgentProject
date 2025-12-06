namespace AiMultiAgent.Core.Agents.CodeReview;

// TODO: Реализовать логику codereview здесь
public class CodeReviewerAgent
{
    public Task<CodeReviewResult> ReviewAsync(
        string prTitle,
        string prDescription,
        string diff,
        CancellationToken ct = default)
    {
        var result = new CodeReviewResult
        {
            Summary = $"Stub: обзор PR \"{prTitle}\" ещё не реализован, но пайплайн работает.",
            Issues =
            [
                new CodeReviewIssue
                {
                    Severity = "info",
                    Title = "Заглушка ревью",
                    Details = "Это заглушка. Здесь позже появится реальный анализ кода с помощью LLM."
                }
            ],
            Suggestions =
            [
                "Подключить реальную LLM-модель для анализа diff.",
                "Добавить проверку стиля и потенциальных багов."
            ]
        };

        return Task.FromResult(result);
    }
}
