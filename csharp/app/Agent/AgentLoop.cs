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
        // TODO 3: implement RunAgent, the tool loop
        // Hint: Follow the steps below; the SDK calls are in CHEATSHEET.md.
        // 1. Start the conversation: a list with one user message, userMessage. Count tool calls from 0.
        // 2. Loop: send the whole list with Client.Messages.Create: Model, MaxTokens, System, Tools,
        //    Messages and CacheControl = new CacheControlEphemeral(). Then Log.ModelCall(role, messages.Count, response).
        // 3. Append the assistant turn exactly as received: Role.Assistant, Content = ToParams(response.Content).
        // 4. If response.StopReason isn't StopReason.ToolUse, the model is done: return FinalText(response).
        // 5. For each tool_use block (block.TryPickToolUse(out ToolUseBlock? toolUse)):
        //    - already ran Config.MaxToolCalls tools? Log.CapReached, return "[stopped: more than N tool calls]"
        //    - Log.ToolCall, run Tools.ExecuteTool(toolUse.Name, toolUse.Input), Log.ToolResult (with how long it took)
        //    - but only run tools this role was given (Offers(tools, toolUse.Name)); any other gets the error
        //      result "Refused: <name> is not one of your tools"
        //    - collect a ToolResultBlockParam with ToolUseID = toolUse.ID, the output and IsError
        // 6. Append ONE user message holding all the results, then go round again.
        throw new NotImplementedException("TODO 3: implement RunAgent, the tool loop");
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
