# The loop without an SDK

This is the program from the intro demo: a whole coding agent in plain HTTP, with no SDK and no framework. It has one tool and one loop. It's written in C#, but read it as pseudocode whatever your track is: your `RunAgent` (TODO 3) does the same steps with the SDK's types instead of raw JSON.

It is a demo, not a template: there's no tool-call cap, no output limit and no logging.

## What to look for

| In the demo | In your `RunAgent` |
| --- | --- |
| `messages` starts with one user message | the same: the `userMessage` argument |
| `POST /v1/messages` with model, tools and **all** messages | `messages.create(...)`: the model remembers nothing, so every call sends the whole conversation |
| `messages.Add(... "assistant" ... body["content"])` | append the assistant turn exactly as received, every block |
| `if (stopReason != "tool_use") break;` | stop and return the text once the model stops asking for tools |
| one `tool_result` per `tool_use`, with the same id | the same, using `ExecuteTool` and the cap |
| all results in **one** `"user"` message | the same |

## The program

```csharp
var http = new HttpClient { BaseAddress = new Uri(
    Environment.GetEnvironmentVariable("ANTHROPIC_BASE_URL") ?? "https://api.anthropic.com") };
http.DefaultRequestHeaders.Add("x-api-key", Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));
http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

var tools = JsonNode.Parse("""
[{
  "name": "run_command",
  "description": "Run a shell command in /workspace. Returns exit code and output.",
  "input_schema": {
    "type": "object",
    "properties": { "command": { "type": "string" } },
    "required": ["command"]
  }
}]
""")!;

var messages = new JsonArray {
    new JsonObject { ["role"] = "user",
        ["content"] = args.Length > 0 ? args[0] : "How many tests are in this project, and do they pass?" }
};

for (var step = 1; step <= 10; step++)
{
    var request = new JsonObject {
        ["model"] = "claude-haiku-5-5",
        ["max_tokens"] = 2000,
        ["tools"] = tools.DeepClone(),
        ["messages"] = messages.DeepClone()
    };
    var response = await http.PostAsJsonAsync("/v1/messages", request);
    var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    if (!response.IsSuccessStatusCode) { Console.WriteLine(body); break; }

    var stopReason = (string?)body["stop_reason"];
    // The whole history went out with this call: the count grows by 2 per step.
    Console.WriteLine($"\n--- step {step}: sent {messages.Count} message(s), stop_reason = {stopReason} ---");
    Console.WriteLine(Show(body["content"]!));

    // The assistant turn goes back into history exactly as received.
    messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = body["content"]!.DeepClone() });

    if (stopReason != "tool_use") break;

    var results = new JsonArray();
    foreach (var block in body["content"]!.AsArray())
    {
        if ((string?)block!["type"] != "tool_use") continue;
        var output = RunCommand((string)block["input"]!["command"]!);
        Console.WriteLine($">>> tool_result: {output}");
        results.Add(new JsonObject {
            ["type"] = "tool_result",
            ["tool_use_id"] = (string)block["id"]!,
            ["content"] = output
        });
    }
    // Tool results go back as a *user* message.
    messages.Add(new JsonObject { ["role"] = "user", ["content"] = results });
}

// For the screen only: readable JSON, and thinking signatures shortened. What goes back
// to the API is the reply exactly as received (above).
static string Show(JsonNode content)
{
    var copy = content.DeepClone();
    foreach (var block in copy.AsArray())
        if ((string?)block!["signature"] is { Length: > 12 } sig)
            block["signature"] = $"{sig[..8]}…({sig.Length:N0} chars)";
    return copy.ToJsonString(new JsonSerializerOptions {
        WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
}

static string RunCommand(string command)
{
    // As runner, like the real agent: build output made as agent in /workspace would block it later.
    var psi = new ProcessStartInfo("sudo", new[] { "-u", "runner", "timeout", "30", "/bin/sh", "-c", command }) {
        RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = "/workspace" };
    using var p = Process.Start(psi)!;
    var stdout = p.StandardOutput.ReadToEndAsync();
    var stderr = p.StandardError.ReadToEndAsync();
    if (!p.WaitForExit(30_000)) { p.Kill(entireProcessTree: true); return "error: timed out after 30 s"; }
    return $"exit code {p.ExitCode}\n{stdout.Result}{stderr.Result}";
}
```
