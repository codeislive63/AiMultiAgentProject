using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AiMultiAgent.Core.Agents.Pm.Llm;

public sealed class GeminiPmLlmPlanner(IChatClient chat) : IPmPlanner
{
    private readonly IChatClient _chat = chat;

    private static JsonSerializerOptions JsonOpts() => new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<PmPlan> CreatePlanAsync(PmOrchestrationRequest req, CancellationToken ct)
    {
        var system = 
        """
            You are a PM planner. Return ONLY valid JSON. No markdown. No extra text.
            
            Allowed tools: code_review, generate_docs.
            Schema:
            {
              "objective": string,
              "steps": [
                { "id": string, "tool": "code_review"|"generate_docs", "arguments": object, "onFail": "continue"|"stop" }
              ]
            }
        """;

        var user = "Request JSON:\n" + JsonSerializer.Serialize(req);
        var json = await AskJsonAsync(system, user, ct);

        return JsonSerializer.Deserialize<PmPlan>(json, JsonOpts())!;
    }

    public async Task<PmOrchestrationReport> AggregateAsync(
     PmOrchestrationRequest req,
     object toolResults,
     List<TraceEvent> traces,
     CancellationToken ct)
    {
        var system =
        """
            You are a PM aggregator. Return ONLY valid JSON. No markdown. No extra text.
            
            Return JSON that matches EXACTLY this schema:

            {
              "meta": object,
              "toolResults": object,
              "risks": [ object ],
              "nextActions": [ object ],
              "summary": string,
              "trace": [ { "ts": string, "type": string, "tool": string|null, "details": string|null } ]
            }

            Rules:
            - summary: 3-5 short lines.
            - risks: list of issues found from codeReview/docs results (if any). If none, empty [].
            - nextActions: concrete next steps. If none, empty [].
            - meta: include at least { "aggregation": "llm" }.
            - toolResults and trace MUST be included as passed in (don’t drop them).
        """;

        var user =
            "Request:\n" + JsonSerializer.Serialize(req) +
            "\nToolResults:\n" + JsonSerializer.Serialize(toolResults) +
            "\nTrace:\n" + JsonSerializer.Serialize(traces);

        var json = await AskJsonAsync(system, user, ct);
        return JsonSerializer.Deserialize<PmOrchestrationReport>(json, JsonOpts())!;
    }


    private async Task<string> AskJsonAsync(string system, string user, CancellationToken ct)
    {
        var text = await CallAsync(system, user, ct);
        var extracted = TryExtractJson(text);
        
        if (extracted is not null)
        {
            return extracted;
        }

        var repairUser = "Fix output. Return ONLY valid JSON, no extra text.\n\nBAD_OUTPUT:\n" + text;
        var repaired = await CallAsync(system, repairUser, ct);

        if (!IsValidJson(repaired))
        {
            throw new InvalidOperationException("LLM failed to return valid JSON after retry.");
        }

        return repaired;
    }

    private static string? TryExtractJson(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();

        if (s.StartsWith("```"))
        {
            var firstBrace = s.IndexOf('{');
            var firstBracket = s.IndexOf('[');
            var start = (firstBrace >= 0 && (firstBracket < 0 || firstBrace < firstBracket)) ? firstBrace : firstBracket;
            if (start >= 0) s = s.Substring(start);
            var lastBrace = s.LastIndexOf('}');
            var lastBracket = s.LastIndexOf(']');
            var end = Math.Max(lastBrace, lastBracket);
            if (end > 0) s = s.Substring(0, end + 1);
        }

        try { JsonDocument.Parse(s); return s; } catch { return null; }
    }


    private async Task<string> CallAsync(string system, string user, CancellationToken ct)
    {
        var messages = new[]
        {
            new ChatMessage(ChatRole.System, system),
            new ChatMessage(ChatRole.User, user),
        };

        ChatResponse response = await _chat.GetResponseAsync(messages, cancellationToken: ct);

        var text = response.Text ?? 
                   response.Messages?.LastOrDefault()?.Text;

        return string.IsNullOrWhiteSpace(text) ? "{}" : text;
    }

    private static bool IsValidJson(string s)
    {
        s = (s ?? "").Trim();
        
        if (!(s.StartsWith('{') || s.StartsWith('[')))
        {
            return false;
        }

        try 
        { 
            JsonDocument.Parse(s); 
            return true; 
        } 
        catch 
        { 
            return false; 
        }
    }
}
