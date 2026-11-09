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
        // TODO 3: implement runAgent, the tool loop
        // Hint: Follow the steps below; the SDK calls are in CHEATSHEET.md.
        // 1. Start the conversation: a list with one user message, userMessage. Count tool calls from 0.
        // 2. Loop: send the whole list with client.messages().create(MessageCreateParams.builder()...): model,
        //    maxTokens, system, tools, messages, cacheControl. Then Log.modelCall(role, messages.size(), response).
        // 3. Append the assistant turn exactly as received: messages.add(response.toParam()).
        // 4. If response.stopReason() isn't StopReason.TOOL_USE, the model is done: return finalText(response).
        // 5. For each block with block.toolUse() present:
        //    - already ran Config.MAX_TOOL_CALLS tools? Log.capReached, return "[stopped: more than N tool calls]"
        //    - Log.toolCall, run Tools.executeTool(call.name(), call._input()), Log.toolResult (with how long it took)
        //    - but only run tools this role was given (offers(tools, call.name())); any other gets the error
        //      result "Refused: <name> is not one of your tools"
        //    - collect ContentBlockParam.ofToolResult(...) with toolUseId(call.id()), the output and isError
        // 6. Append ONE user message holding all the results, then go round again.
        throw new UnsupportedOperationException("TODO 3: implement runAgent, the tool loop");
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
