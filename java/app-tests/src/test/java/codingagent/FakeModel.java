package codingagent;

import static org.junit.jupiter.api.Assertions.assertTrue;

import codingagent.agent.AgentLoop;

import com.anthropic.client.AnthropicClient;
import com.anthropic.client.okhttp.AnthropicOkHttpClient;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ArrayNode;
import com.fasterxml.jackson.databind.node.ObjectNode;
import com.sun.net.httpserver.HttpServer;
import java.io.IOException;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Deque;
import java.util.List;
import java.util.Map;
import java.util.function.Function;
import java.util.stream.Collectors;
import java.util.stream.StreamSupport;

/**
 * A stand-in for the Claude API, so the loop tests need no key: a local HTTP
 * server that answers each request with the next scripted reply and records what
 * your code sent. While it is open, AgentLoop.client talks to it.
 */
final class FakeModel implements AutoCloseable {
    static final ObjectMapper JSON = new ObjectMapper();

    /** Every request body your code sent, in order. */
    final List<JsonNode> requests = new ArrayList<>();

    /** The request of model call index + 1, or a failure that says how many calls your code made. */
    JsonNode request(int index) {
        assertTrue(index < requests.size(), "This test needs model call " + (index + 1) + ", but your code made "
            + requests.size() + ". After the model asks for tools, run them (or refuse them with an error result),"
            + " send the results back in one user message, and call the model again, until it stops asking.");
        return requests.get(index);
    }
    /** Every reply's content blocks, in order (to compare with what you echo back). */
    final List<ArrayNode> replies = new ArrayList<>();
    /** What to answer once the script runs out (default: a final "done"). */
    Function<FakeModel, ArrayNode> whenScriptEnds;

    private final Deque<ArrayNode> script = new ArrayDeque<>();
    private final HttpServer server;
    private final AnthropicClient originalClient = AgentLoop.client;
    private int ids;

    FakeModel() throws IOException {
        server = HttpServer.create(new InetSocketAddress("127.0.0.1", 0), 0);
        server.createContext("/", exchange -> {
            JsonNode request = JSON.readTree(exchange.getRequestBody().readAllBytes());
            ArrayNode content;
            synchronized (this) {
                requests.add(request);
                content = nextReply();
                replies.add(content);
            }
            boolean usesTools = StreamSupport.stream(content.spliterator(), false)
                .anyMatch(block -> block.path("type").asText().equals("tool_use"));
            ObjectNode reply = JSON.createObjectNode()
                .put("id", nextId("msg")).put("type", "message").put("role", "assistant")
                .put("model", request.path("model").asText("test-model"))
                .put("stop_reason", usesTools ? "tool_use" : "end_turn")
                .putNull("stop_sequence");
            reply.set("content", content.deepCopy());
            reply.set("usage", JSON.createObjectNode().put("input_tokens", 10).put("output_tokens", 5)
                .put("cache_creation_input_tokens", 0).put("cache_read_input_tokens", 0));
            byte[] body = JSON.writeValueAsBytes(reply);
            exchange.getResponseHeaders().set("content-type", "application/json");
            exchange.sendResponseHeaders(200, body.length);
            exchange.getResponseBody().write(body);
            exchange.close();
        });
        server.start();
        AgentLoop.client = AnthropicOkHttpClient.builder()
            .baseUrl("http://127.0.0.1:" + server.getAddress().getPort())
            .apiKey("test-key")
            .maxRetries(0)
            .build();
    }

    /**
     * The model thinks, then finishes with this text (stop_reason "end_turn"), so the text is not
     * the first block: use finalText, not content().get(0).
     */
    FakeModel says(String text) {
        ArrayNode content = JSON.createArrayNode();
        content.addObject().put("type", "thinking").put("thinking", "").put("signature", nextId("sig"));
        script.add(content.add(textBlock(text)));
        return this;
    }

    /** The model thinks, then asks for one or more tools, given as name, input, name, input... */
    FakeModel callsTools(Object... namesAndInputs) {
        script.add(toolCalls(namesAndInputs));
        return this;
    }

    ArrayNode toolCalls(Object... namesAndInputs) {
        ArrayNode content = JSON.createArrayNode();
        content.addObject().put("type", "thinking").put("thinking", "").put("signature", nextId("sig"));
        for (int i = 0; i < namesAndInputs.length; i += 2) {
            ObjectNode call = content.addObject().put("type", "tool_use").put("id", nextId("toolu"))
                .put("name", (String) namesAndInputs[i]);
            call.set("input", JSON.valueToTree(namesAndInputs[i + 1]));
        }
        return content;
    }

    static ObjectNode textBlock(String text) {
        return JSON.createObjectNode().put("type", "text").put("text", text);
    }

    private ArrayNode nextReply() {
        // Emergency brake: a loop without a working cap would otherwise run forever.
        if (requests.size() > 40) return JSON.createArrayNode().add(textBlock("(the test stopped this loop after 40 calls)"));
        if (!script.isEmpty()) return script.poll();
        if (whenScriptEnds != null) return whenScriptEnds.apply(this);
        return JSON.createArrayNode().add(textBlock("done"));
    }

    private synchronized String nextId(String prefix) {
        return String.format("%s_%03d", prefix, ++ids);
    }

    @Override
    public void close() {
        AgentLoop.client = originalClient;
        server.stop(0);
    }

    // Helpers for reading what was sent.

    /** A "system" or "content" value as plain text (a string, or text blocks). */
    static String text(JsonNode value) {
        if (value == null || value.isMissingNode() || value.isNull()) return "";
        if (value.isTextual()) return value.asText();
        if (value.isArray()) {
            return StreamSupport.stream(value.spliterator(), false)
                .map(block -> block.path("text").asText(""))
                .collect(Collectors.joining("\n"));
        }
        return value.toString();
    }

    static List<String> toolNames(JsonNode request) {
        return StreamSupport.stream(request.path("tools").spliterator(), false)
            .map(tool -> tool.path("name").asText())
            .toList();
    }

    /** The parts of a content block that must survive the round trip unchanged. */
    static Map<String, JsonNode> essentials(JsonNode block) {
        Map<String, JsonNode> kept = new java.util.LinkedHashMap<>();
        for (String key : List.of("type", "id", "name", "input", "text", "thinking", "signature")) {
            if (block.has(key)) kept.put(key, block.get(key));
        }
        return kept;
    }
}
