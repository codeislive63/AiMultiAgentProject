namespace AiMultiAgent.Core.Agents.Pm;

// TODO: Реализовать логику планирования здесь
public class PmAgent
{
    public Task<PmPlan> PlanAsync(string goal, CancellationToken ct = default)
    {
        var plan = new PmPlan
        {
            Goal = goal,
            Steps =
            [
                new PmStep { Order = 1, Title = "Уточнить требования", Description = "Проговорить с командой, что именно нужно сделать." },
                new PmStep { Order = 2, Title = "Разбить на задачи", Description = "Составить список задач для агентов PM/CodeReview/Docs." },
                new PmStep { Order = 3, Title = "Настроить MCP-сервер", Description = "Подключить агентов, протестировать через Scalar." }
            ]
        };

        return Task.FromResult(plan);
    }
}
