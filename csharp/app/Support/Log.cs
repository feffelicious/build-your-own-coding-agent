using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.Models.Messages;

namespace CodingAgent;

// Compact, colored console output plus one JSON line per event in
// LOG_DIR/run-<timestamp>.jsonl. Provided: you call it, you don't change it.
public static class Log
{
    // Haiku 5.5 list prices in USD per million tokens, for the cost estimate.
    const double InputPrice = 0.10, OutputPrice = 0.50, CacheReadPrice = 0.01, CacheWritePrice = 0.125;

    static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    static readonly bool Color = Environment.GetEnvironmentVariable("NO_COLOR") is null;
    static readonly Dictionary<string, RoleStats> Stats = new();
    static StreamWriter? _file;
    static string? _path;

    public static string? FilePath => _path;

    public static void Start()
    {
        Directory.CreateDirectory(Config.LogDir);
        _path = Path.Combine(Config.LogDir, $"run-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
        _file = new StreamWriter(_path, append: true) { AutoFlush = true };
        Console.WriteLine(Paint("1", $"Coding agent · model {Config.Model} · log {_path}"));
    }

    public static void Transition(string from, string to, int revision)
    {
        Write("transition", new() { ["from"] = from, ["to"] = to, ["revision"] = revision });
        Console.WriteLine();
        Console.WriteLine(Paint("1;" + RoleColor(to), $"── {from} → {to} · revision {revision} ──"));
    }

    public static void ModelCall(string role, int messageCount, Message response)
    {
        var usage = response.Usage;
        var stopReason = response.StopReason?.Raw() ?? "none";
        var cacheRead = usage.CacheReadInputTokens ?? 0;
        var cacheWrite = usage.CacheCreationInputTokens ?? 0;
        var stats = StatsFor(role);
        stats.ModelCalls++;
        stats.InputTokens += usage.InputTokens;
        stats.OutputTokens += usage.OutputTokens;
        stats.CacheReadTokens += cacheRead;
        stats.CacheWriteTokens += cacheWrite;

        Write("model_call", new()
        {
            ["role"] = role,
            ["model"] = Config.Model,
            ["message_count"] = messageCount,
            ["input_tokens"] = usage.InputTokens,
            ["output_tokens"] = usage.OutputTokens,
            ["cache_read_tokens"] = cacheRead,
            ["cache_creation_tokens"] = cacheWrite,
            ["stop_reason"] = stopReason,
        });
        Console.WriteLine(Paint("2", $"[{role}] model call: {messageCount} messages, input {usage.InputTokens} "
            + $"(+{cacheRead} cached), output {usage.OutputTokens}, stop_reason {stopReason}"));

        foreach (var block in response.Content)
        {
            if (block.TryPickText(out TextBlock? text) && text.Text.Trim().Length > 0)
            {
                var prefix = $"[{role}] ";
                var body = text.Text.Trim().Replace("\n", "\n" + new string(' ', prefix.Length));
                Console.WriteLine(Paint(RoleColor(role), prefix + body));
            }
        }
    }

    public static void ToolCall(string role, string name, IReadOnlyDictionary<string, JsonElement> input)
    {
        StatsFor(role).ToolCalls++;
        var inputJson = JsonSerializer.Serialize(input, Json);
        Write("tool_call", new() { ["role"] = role, ["name"] = name, ["input"] = JsonNode.Parse(inputJson) });
        Console.WriteLine(Paint(RoleColor(role), $"[{role}] → {name} {Shorten(inputJson, 300)}"));
    }

    public static void ToolResult(string role, string name, bool isError, long durationMs, string output)
    {
        Write("tool_result", new()
        {
            ["role"] = role,
            ["name"] = name,
            ["is_error"] = isError,
            ["duration_ms"] = durationMs,
            ["preview"] = output.Length > 500 ? output[..500] : output,
        });
        var status = isError ? "ERROR" : "ok";
        Console.WriteLine(Paint(isError ? "31" : "2",
            $"[{role}] ← {name} {status} in {durationMs} ms: {Shorten(output.Trim().Replace("\n", " ⏎ "), 200)}"));
    }

    public static void CapReached(string role, int toolCalls)
    {
        Write("cap_reached", new() { ["role"] = role, ["tool_calls"] = toolCalls });
        Console.WriteLine(Paint("31", $"[{role}] stopped: more than {Config.MaxToolCalls} tool calls"));
    }

    public static void Verdict(int revision, string verdict)
    {
        Write("verdict", new() { ["revision"] = revision, ["verdict"] = verdict });
        Console.WriteLine(Paint(verdict == "APPROVED" ? "1;32" : "1;31",
            $"[orchestrator] verdict for revision {revision}: {verdict}"));
    }

    public static void Info(string message)
    {
        Console.WriteLine(Paint("32", $"[orchestrator] {message}"));
    }

    // approved is null when no Reviewer ran (run-agent --coder or --plan).
    public static void RunSummary(bool? approved, int revisions)
    {
        var perRole = new JsonObject();
        double cost = 0;
        foreach (var (role, s) in Stats)
        {
            cost += s.Cost();
            perRole[role] = new JsonObject
            {
                ["model_calls"] = s.ModelCalls,
                ["tool_calls"] = s.ToolCalls,
                ["input_tokens"] = s.InputTokens,
                ["output_tokens"] = s.OutputTokens,
                ["cache_read_tokens"] = s.CacheReadTokens,
                ["cache_creation_tokens"] = s.CacheWriteTokens,
            };
        }
        Write("run_summary", new()
        {
            ["approved"] = approved,
            ["revisions"] = revisions,
            ["roles"] = perRole,
            ["estimated_cost_usd"] = Math.Round(cost, 6),
        });

        Console.WriteLine();
        Console.WriteLine(Paint("1", "── summary ──"));
        var verdict = approved switch { true => "yes", false => "no", null => "not reviewed" };
        Console.WriteLine(Paint(approved == false ? "1;31" : "1;32", $"approved: {verdict}   revisions: {revisions}"));
        foreach (var (role, s) in Stats)
        {
            Console.WriteLine($"{role,-9} model calls {s.ModelCalls,3}   tool calls {s.ToolCalls,3}   "
                + $"input {s.InputTokens,7}   cached {s.CacheReadTokens,7}   output {s.OutputTokens,6}");
        }
        Console.WriteLine($"estimated cost: ${cost:F4} (Haiku 5.5 list prices)");
        if (_path is not null) Console.WriteLine($"log: {_path}");
    }

    static void Write(string evt, JsonObject fields)
    {
        if (_file is null) return;
        var line = new JsonObject { ["ts"] = DateTime.UtcNow.ToString("o"), ["event"] = evt };
        foreach (var (key, value) in fields) line[key] = value?.DeepClone();
        _file.WriteLine(line.ToJsonString(Json));
    }

    static RoleStats StatsFor(string role)
    {
        if (!Stats.TryGetValue(role, out var stats)) Stats[role] = stats = new RoleStats();
        return stats;
    }

    static string RoleColor(string role) => role switch
    {
        "planner" => "36",
        "coder" => "33",
        "reviewer" => "35",
        _ => "32",
    };

    static string Paint(string code, string text) => Color ? $"\u001b[{code}m{text}\u001b[0m" : text;

    static string Shorten(string text, int max) => text.Length > max ? text[..max] + "…" : text;

    class RoleStats
    {
        public long ModelCalls, ToolCalls, InputTokens, OutputTokens, CacheReadTokens, CacheWriteTokens;

        public double Cost() => (InputTokens * InputPrice + OutputTokens * OutputPrice
            + CacheReadTokens * CacheReadPrice + CacheWriteTokens * CacheWritePrice) / 1_000_000;
    }
}
