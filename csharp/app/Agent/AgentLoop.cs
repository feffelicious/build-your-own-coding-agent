using System.Diagnostics;
using Anthropic;
using Anthropic.Models.Messages;

namespace CodingAgent;

// The agent loop: send the conversation, run the tools the model asks for,
// send the results back, repeat until the model stops asking.
public static class AgentLoop
{
    // Reads ANTHROPIC_API_KEY and ANTHROPIC_BASE_URL from the environment.
    // The tests swap in a scripted model here, so they need no API key.
    public static AnthropicClient Client = new() { MaxRetries = 4 };

    // One fresh conversation per call, with the tools this role may use. Returns the model's final text.
    // Test: run-tests --filter Todo3_RunAgent
    public static async Task<string> RunAgent(string role, string systemPrompt, List<ToolUnion> tools, string userMessage)
    {
        List<MessageParam> messages = [new() { Role = Role.User, Content = userMessage }];
        var toolCalls = 0;
        while (true)
        {
            var response = await Client.Messages.Create(new MessageCreateParams
            {
                Model = Config.Model,
                MaxTokens = Config.MaxTokens,
                System = systemPrompt,
                Tools = tools,
                Messages = messages,
                CacheControl = new CacheControlEphemeral(),   // cache the conversation prefix between calls
            });
            Log.ModelCall(role, messages.Count, response);

            // The assistant turn goes back into the history exactly as received.
            messages.Add(new() { Role = Role.Assistant, Content = ToParams(response.Content) });

            if (response.StopReason != StopReason.ToolUse) return FinalText(response);

            List<ContentBlockParam> results = [];
            foreach (var block in response.Content)
            {
                if (!block.TryPickToolUse(out ToolUseBlock? toolUse)) continue;
                if (toolCalls == Config.MaxToolCalls)
                {
                    Log.CapReached(role, toolCalls);
                    return $"[stopped: more than {Config.MaxToolCalls} tool calls]";
                }
                toolCalls++;
                Log.ToolCall(role, toolUse.Name, toolUse.Input);
                var timer = Stopwatch.StartNew();
                // The tools we send only tell the model what it may use; this is where it's enforced.
                var result = Offers(tools, toolUse.Name)
                    ? Tools.ExecuteTool(toolUse.Name, toolUse.Input)
                    : new ToolResult($"Refused: {toolUse.Name} is not one of your tools", true);
                Log.ToolResult(role, toolUse.Name, result.IsError, timer.ElapsedMilliseconds, result.Output);
                results.Add(new ToolResultBlockParam
                {
                    ToolUseID = toolUse.ID,
                    Content = result.Output,
                    IsError = result.IsError,
                });
            }
            // Tool results go back as one *user* message, matched to the calls by ID.
            messages.Add(new() { Role = Role.User, Content = results });
        }
    }

    // Whether tools has a tool with this name. Provided.
    public static bool Offers(List<ToolUnion> tools, string name) =>
        tools.Any(t => t.TryPickTool(out Tool? tool) && tool.Name == name);

    // The text blocks of a response, joined. Provided.
    public static string FinalText(Message response) =>
        string.Join("\n", response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text)).Trim();

    // Turns response blocks into request blocks for the history. Python and Java
    // do this in one call; the C# SDK needs it spelled out. Provided.
    public static List<ContentBlockParam> ToParams(IReadOnlyList<ContentBlock> content)
    {
        List<ContentBlockParam> blocks = [];
        foreach (var block in content)
        {
            if (block.TryPickText(out TextBlock? text))
                blocks.Add(new TextBlockParam { Text = text.Text });
            else if (block.TryPickThinking(out ThinkingBlock? thinking))
                blocks.Add(new ThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
            else if (block.TryPickRedactedThinking(out RedactedThinkingBlock? redacted))
                blocks.Add(new RedactedThinkingBlockParam { Data = redacted.Data });
            else if (block.TryPickToolUse(out ToolUseBlock? toolUse))
                blocks.Add(new ToolUseBlockParam { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });
        }
        return blocks;
    }
}
