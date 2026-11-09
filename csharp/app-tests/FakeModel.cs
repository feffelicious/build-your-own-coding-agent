using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using CodingAgent;

namespace CodingAgent.Tests;

// A stand-in for the Claude API, so the loop tests need no key: a local HTTP
// server that answers each request with the next scripted reply and records
// what your code sent. While it exists, AgentLoop.Client talks to it.
sealed class FakeModel : IDisposable
{
    readonly HttpListener _listener = new();
    readonly Queue<JsonArray> _script = new();
    readonly AnthropicClient _originalClient = AgentLoop.Client;
    int _ids;

    // Every request body your code sent, in order.
    public List<JsonObject> Requests { get; } = [];
    // Every reply's content blocks, in order (to compare with what you echo back).
    public List<JsonArray> Replies { get; } = [];
    // What to answer once the script runs out (default: a final "done").
    public Func<FakeModel, JsonArray>? WhenScriptEnds { get; set; }

    // The request of model call index + 1, or a failure that says how many calls your code made.
    public JsonObject Request(int index)
    {
        Expect.That(index < Requests.Count,
            $"This test needs model call {index + 1}, but your code made {Requests.Count}. After the model asks for tools, run them (or refuse them with an error result),"
            + " send the results back in one user message, and call the model again, until it stops asking.");
        return Requests[index];
    }

    public FakeModel()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _ = Task.Run(Serve);
        AgentLoop.Client = new AnthropicClient { BaseUrl = $"http://127.0.0.1:{port}", ApiKey = "test-key", MaxRetries = 0 };
    }

    // The model thinks, then finishes with this text (stop_reason "end_turn"), so the text is
    // not the first block: use FinalText, not Content[0].
    public FakeModel Says(string text)
    {
        _script.Enqueue([new JsonObject { ["type"] = "thinking", ["thinking"] = "", ["signature"] = $"sig_{++_ids}" }, TextBlock(text)]);
        return this;
    }

    // The model thinks, then asks for one or more tools (stop_reason "tool_use").
    public FakeModel CallsTools(params (string Name, object Input)[] calls)
    {
        _script.Enqueue(ToolCalls(calls));
        return this;
    }

    public JsonArray ToolCalls(params (string Name, object Input)[] calls)
    {
        var content = new JsonArray(new JsonObject { ["type"] = "thinking", ["thinking"] = "", ["signature"] = $"sig_{++_ids}" });
        foreach (var (name, input) in calls)
        {
            content.Add(new JsonObject
            {
                ["type"] = "tool_use",
                ["id"] = $"toolu_{++_ids:D3}",
                ["name"] = name,
                ["input"] = JsonSerializer.SerializeToNode(input),
            });
        }
        return content;
    }

    static JsonObject TextBlock(string text) => new() { ["type"] = "text", ["text"] = text };

    async Task Serve()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception) { return; }

            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var request = JsonNode.Parse(await reader.ReadToEndAsync())!.AsObject();
            lock (Requests) Requests.Add(request);

            // Emergency brake: a loop without a working cap would otherwise run forever.
            var content = Requests.Count > 40 ? [TextBlock("(the test stopped this loop after 40 calls)")]
                : _script.Count > 0 ? _script.Dequeue()
                : WhenScriptEnds?.Invoke(this) ?? [TextBlock("done")];
            lock (Requests) Replies.Add(content);
            var usesTools = content.Any(b => (string?)b!["type"] == "tool_use");
            var reply = new JsonObject
            {
                ["id"] = $"msg_{++_ids:D3}",
                ["type"] = "message",
                ["role"] = "assistant",
                ["model"] = (string?)request["model"] ?? "test-model",
                ["content"] = content.DeepClone(),
                ["stop_reason"] = usesTools ? "tool_use" : "end_turn",
                ["stop_sequence"] = null,
                ["usage"] = new JsonObject
                {
                    ["input_tokens"] = 10, ["output_tokens"] = 5,
                    ["cache_creation_input_tokens"] = 0, ["cache_read_input_tokens"] = 0,
                },
            };
            var bytes = Encoding.UTF8.GetBytes(reply.ToJsonString());
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }

    public void Dispose()
    {
        AgentLoop.Client = _originalClient;
        _listener.Stop();
        _listener.Close();
    }

    // Helpers for reading what was sent.

    // A "system" or "content" value as plain text (a string, or text blocks).
    public static string Text(JsonNode? node) => node switch
    {
        null => "",
        JsonValue value => (string?)value ?? "",
        JsonArray blocks => string.Join("\n", blocks.Select(b => (string?)b?["text"] ?? "")),
        _ => node.ToJsonString(),
    };

    public static JsonArray Messages(JsonObject request) => request["messages"]?.AsArray() ?? [];

    public static List<string> ToolNames(JsonObject request) =>
        request["tools"]?.AsArray().Select(t => (string?)t?["name"] ?? "").ToList() ?? [];

    // The parts of a content block that must survive the round trip unchanged.
    public static string Essentials(JsonNode? block)
    {
        var kept = new JsonObject();
        foreach (var key in new[] { "type", "id", "name", "input", "text", "thinking", "signature" })
        {
            if (block?[key] is { } value) kept[key] = value.DeepClone();
        }
        return kept.ToJsonString();
    }
}
