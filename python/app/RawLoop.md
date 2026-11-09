# The loop without an SDK

This is the program from the intro demo: a whole agent in plain HTTP, with no SDK and no framework. It has one loop and two tools, `list_lights` and `set_light`, that switch pretend lights in a dictionary. Your coding agent is the same loop with file and test tools instead. It's written in C#, but read it as pseudocode whatever your track is: your `RunAgent` (TODO 3) does the same steps with the SDK's types instead of raw JSON.

It is a demo, not a template: there's no tool-call cap, no output limit and no logging.

## What to look for

| In the demo | In your `RunAgent` |
| --- | --- |
| `messages` starts with one user message | the same: the `userMessage` argument |
| `POST /v1/messages` with model, tools and **all** messages | `messages.create(...)`: the model remembers nothing, so every call sends the whole conversation |
| `messages.Add(... "assistant" ... body["content"])` | append the assistant turn exactly as received, every block |
| `if (stopReason != "tool_use") break;` | stop and return the text once the model stops asking for tools |
| one `tool_result` per `tool_use`, with the same id (`RunTool`) | the same, using `ExecuteTool` and the cap |
| all results in **one** `"user"` message | the same |

## The program

```csharp
var http = new HttpClient { BaseAddress = new Uri(
    Environment.GetEnvironmentVariable("ANTHROPIC_BASE_URL") ?? "https://api.anthropic.com") };
http.DefaultRequestHeaders.Add("x-api-key", Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));
http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

// The "house": our code owns it. The model can only ask us to change it.
var lights = new Dictionary<string, bool> {
    ["kitchen"] = true, ["living room"] = true, ["hallway"] = false, ["bedroom"] = false, ["bathroom"] = true };

var tools = JsonNode.Parse("""
[{
  "name": "list_lights",
  "description": "List the lights in the house and whether each one is on or off.",
  "input_schema": { "type": "object", "properties": {} }
}, {
  "name": "set_light",
  "description": "Turn one light on or off.",
  "input_schema": {
    "type": "object",
    "properties": {
      "room": { "type": "string", "description": "A room name from list_lights" },
      "on": { "type": "boolean" }
    },
    "required": ["room", "on"]
  }
}]
""")!;

var messages = new JsonArray {
    new JsonObject { ["role"] = "user",
        ["content"] = args.Length > 0 ? args[0] : "I'm going to bed. Turn off every light that's on, and turn on the bedroom light." }
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
        var output = RunTool((string)block["name"]!, block["input"]!);
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

Console.WriteLine($"\nThe house now: {ListLights()}");

// Our code, not the model, does the work. The model only ever sees these strings.
string RunTool(string name, JsonNode input) => name switch {
    "list_lights" => ListLights(),
    "set_light" => SetLight((string?)input["room"] ?? "", (bool?)input["on"] ?? false),
    _ => $"error: there is no tool called {name}"
};

string ListLights() => string.Join(", ", lights.Select(l => $"{l.Key} {(l.Value ? "on" : "off")}"));

string SetLight(string room, bool on)
{
    if (!lights.ContainsKey(room)) return $"error: no light in '{room}'. Rooms: {string.Join(", ", lights.Keys)}";
    lights[room] = on;
    return $"{room} is now {(on ? "on" : "off")}";
}

// For the screen only: readable JSON, thinking signatures shortened and the "caller" field
// left out. What goes back to the API is the reply exactly as received (above).
static string Show(JsonNode content)
{
    var copy = content.DeepClone();
    foreach (var block in copy.AsArray())
    {
        block!.AsObject().Remove("caller");
        if ((string?)block["signature"] is { Length: > 12 } sig)
            block["signature"] = $"{sig[..8]}…({sig.Length:N0} chars)";
    }
    return copy.ToJsonString(new JsonSerializerOptions {
        WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
}
```
