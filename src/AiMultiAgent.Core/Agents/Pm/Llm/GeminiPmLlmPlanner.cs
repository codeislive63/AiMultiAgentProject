using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GenerativeAI.Exceptions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;

namespace AiMultiAgent.Core.Agents.Pm.Llm;

public sealed class GeminiPmLlmPlanner(IChatClient chat, IMemoryCache cache) : IPmPlanner
{
    private readonly IChatClient _chat = chat;
    private readonly IMemoryCache _cache = cache;

    private static JsonSerializerOptions JsonOpts() => new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static string CacheKey(string kind, string system, string user)
        => $"{kind}:{system.GetHashCode()}:{user.GetHashCode()}";

    public async Task<PmPlan> CreatePlanAsync(PmOrchestrationRequest req, CancellationToken ct)
    {
        var system =
        """
You are a PM planner. Return ONLY valid JSON. No markdown, no code fences, no extra text.
Output MUST start with '{' and end with '}'.

Allowed tools: code_review, generate_docs.

For tool "code_review" arguments MUST be exactly:
{ "title": string, "description": string, "diff": string }

For tool "generate_docs" arguments MUST be exactly:
{ "componentName": string, "description": string }

Schema:
{
  "objective": string,
  "steps": [
    { "id": string, "tool": "code_review"|"generate_docs", "arguments": object, "onFail": "continue"|"stop" }
  ]
}
""";

        var user = "Request JSON:\n" + JsonSerializer.Serialize(req);
        var json = await AskJsonAsync("plan", system, user, ct);

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
You are a PM aggregator. Return ONLY valid JSON. No markdown, no extra text.

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
- risks / nextActions can be empty arrays.
""";

        var user =
            "Request:\n" + JsonSerializer.Serialize(req) +
            "\nToolResults:\n" + JsonSerializer.Serialize(toolResults) +
            "\nTrace:\n" + JsonSerializer.Serialize(traces);

        var json = await AskJsonAsync("agg", system, user, ct);
        return JsonSerializer.Deserialize<PmOrchestrationReport>(json, JsonOpts())!;
    }

    private async Task<string> AskJsonAsync(string kind, string system, string user, CancellationToken ct)
    {
        var key = CacheKey(kind, system, user);
        if (_cache.TryGetValue<string>(key, out var cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        var text = await CallAsync(system, user, ct);
        var extracted = TryExtractJson(text);

        if (extracted is not null)
        {
            _cache.Set(key, extracted, TimeSpan.FromMinutes(2));
            return extracted;
        }

        var repairUser = "Fix output. Return ONLY valid JSON, no extra text.\n\nBAD_OUTPUT:\n" + text;
        var repaired = await CallAsync(system, repairUser, ct);

        var repairedExtracted = TryExtractJson(repaired);
        if (repairedExtracted is null)
        {
            throw new InvalidOperationException("LLM failed to return valid JSON after retry.");
        }

        _cache.Set(key, repairedExtracted, TimeSpan.FromMinutes(2));
        return repairedExtracted;
    }

    private static string? TryExtractJson(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();

        // If model wrapped in ```json ... ```
        if (s.StartsWith("```", StringComparison.Ordinal))
        {
            var firstBrace = s.IndexOf('{');
            var firstBracket = s.IndexOf('[');

            var start = (firstBrace >= 0 && (firstBracket < 0 || firstBrace < firstBracket))
                ? firstBrace
                : firstBracket;

            if (start >= 0) s = s.Substring(start);

            var lastBrace = s.LastIndexOf('}');
            var lastBracket = s.LastIndexOf(']');

            var end = Math.Max(lastBrace, lastBracket);
            if (end > 0) s = s.Substring(0, end + 1);
        }

        try
        {
            JsonDocument.Parse(s);
            return s;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string> CallAsync(string system, string user, CancellationToken ct)
    {
        var messages = new[]
        {
            new ChatMessage(ChatRole.System, system),
            new ChatMessage(ChatRole.User, user),
        };

        const int maxAttempts = 5;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                ChatResponse response = await _chat.GetResponseAsync(messages, cancellationToken: ct);

                var text = response.Text ??
                           response.Messages?.LastOrDefault()?.Text;

                return string.IsNullOrWhiteSpace(text) ? "{}" : text;
            }
            catch (ApiException ex) when (IsRetryable(ex))
            {
                if (attempt == maxAttempts) throw;

                var delay = TryParseRetryDelay(ex.Message)
                            ?? TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1) + Random.Shared.Next(0, 200));

                await Task.Delay(delay, ct);
            }
        }

        throw new InvalidOperationException("Gemini call failed after retries.");
    }

    private static bool IsRetryable(ApiException ex)
    {
        var msg = ex.Message ?? "";
        return ex.ErrorCode is 429 or 503
               || msg.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("quota", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("overloaded", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("UNAVAILABLE", StringComparison.OrdinalIgnoreCase);
    }

    private static TimeSpan? TryParseRetryDelay(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;

        // example: "Please retry in 1.008816236s."
        var m = Regex.Match(message, @"retry in\s+([0-9]+(\.[0-9]+)?)s", RegexOptions.IgnoreCase);
        if (!m.Success) return null;

        if (double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            // small safety margin
            return TimeSpan.FromMilliseconds(seconds * 1000 + 150);
        }

        return null;
    }
}
