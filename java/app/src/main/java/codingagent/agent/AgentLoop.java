package codingagent.agent;

import codingagent.support.Config;
import codingagent.support.Log;
import codingagent.tools.Tools;

import com.anthropic.client.AnthropicClient;
import com.anthropic.client.okhttp.AnthropicOkHttpClient;
import com.anthropic.models.messages.CacheControlEphemeral;
import com.anthropic.models.messages.ContentBlock;
import com.anthropic.models.messages.ContentBlockParam;
import com.anthropic.models.messages.Message;
import com.anthropic.models.messages.MessageCreateParams;
import com.anthropic.models.messages.MessageParam;
import com.anthropic.models.messages.StopReason;
import com.anthropic.models.messages.TextBlock;
import com.anthropic.models.messages.ToolResultBlockParam;
import com.anthropic.models.messages.ToolUnion;
import com.anthropic.models.messages.ToolUseBlock;
import java.util.ArrayList;
import java.util.List;
import java.util.Optional;
import java.util.stream.Collectors;

/**
 * The agent loop: send the conversation, run the tools the model asks for,
 * send the results back, repeat until the model stops asking.
 */
public final class AgentLoop {
    // Reads ANTHROPIC_API_KEY and ANTHROPIC_BASE_URL from the environment.
    // The tests swap in a scripted model here, so they need no API key.
    public static AnthropicClient client = AnthropicOkHttpClient.builder().fromEnv().maxRetries(4).build();

    private AgentLoop() {}

    /** One fresh conversation per call, with the tools this role may use. Returns the model's final text. */
    // Test: run-tests -Dtest='*#todo3RunAgent*'
    public static String runAgent(String role, String systemPrompt, List<ToolUnion> tools, String userMessage) {
        List<MessageParam> messages = new ArrayList<>();
        messages.add(MessageParam.builder().role(MessageParam.Role.USER).content(userMessage).build());
        int toolCalls = 0;
        while (true) {
            Message response = client.messages().create(MessageCreateParams.builder()
                .model(Config.MODEL)
                .maxTokens(Config.MAX_TOKENS)
                .system(systemPrompt)
                .tools(tools)
                .messages(messages)
                .cacheControl(CacheControlEphemeral.builder().build())   // cache the conversation prefix between calls
                .build());
            Log.modelCall(role, messages.size(), response);

            // The assistant turn goes back into the history exactly as received.
            messages.add(response.toParam());

            if (!response.stopReason().equals(Optional.of(StopReason.TOOL_USE))) return finalText(response);

            List<ContentBlockParam> results = new ArrayList<>();
            for (ContentBlock block : response.content()) {
                Optional<ToolUseBlock> toolUse = block.toolUse();
                if (toolUse.isEmpty()) continue;
                if (toolCalls == Config.MAX_TOOL_CALLS) {
                    Log.capReached(role, toolCalls);
                    return "[stopped: more than " + Config.MAX_TOOL_CALLS + " tool calls]";
                }
                toolCalls++;
                ToolUseBlock call = toolUse.get();
                Log.toolCall(role, call.name(), call._input());
                long started = System.nanoTime();
                // The tools we send only tell the model what it may use; this is where it's enforced.
                Tools.ToolResult result = offers(tools, call.name())
                    ? Tools.executeTool(call.name(), call._input())
                    : new Tools.ToolResult("Refused: " + call.name() + " is not one of your tools", true);
                Log.toolResult(role, call.name(), result.isError(), (System.nanoTime() - started) / 1_000_000, result.output());
                results.add(ContentBlockParam.ofToolResult(ToolResultBlockParam.builder()
                    .toolUseId(call.id())
                    .content(result.output())
                    .isError(result.isError())
                    .build()));
            }
            // Tool results go back as one *user* message, matched to the calls by ID.
            messages.add(MessageParam.builder().role(MessageParam.Role.USER).contentOfBlockParams(results).build());
        }
    }

    /** Whether tools has a tool with this name. Provided. */
    public static boolean offers(List<ToolUnion> tools, String name) {
        return tools.stream().anyMatch(t -> t.tool().map(tool -> tool.name().equals(name)).orElse(false));
    }

    /** The text blocks of a response, joined. Provided. */
    public static String finalText(Message response) {
        return response.content().stream()
            .flatMap(block -> block.text().stream())
            .map(TextBlock::text)
            .collect(Collectors.joining("\n"))
            .strip();
    }
}
