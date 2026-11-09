package codingagent.support;

import com.anthropic.core.JsonValue;
import com.anthropic.models.messages.ContentBlock;
import com.anthropic.models.messages.Message;
import com.anthropic.models.messages.StopReason;
import com.anthropic.models.messages.Usage;
import com.fasterxml.jackson.core.JsonGenerator;
import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.io.IOException;
import java.io.PrintWriter;
import java.io.UncheckedIOException;
import java.math.BigDecimal;
import java.math.RoundingMode;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardOpenOption;
import java.time.Instant;
import java.time.LocalDateTime;
import java.time.format.DateTimeFormatter;
import java.util.LinkedHashMap;
import java.util.Locale;
import java.util.Map;

/**
 * Compact, colored console output plus one JSON line per event in
 * LOG_DIR/run-&lt;timestamp&gt;.jsonl. Provided: you call it, you don't change it.
 */
public final class Log {
    // Haiku 5.5 list prices in USD per million tokens, for the cost estimate.
    private static final double INPUT_PRICE = 0.10, OUTPUT_PRICE = 0.50, CACHE_READ_PRICE = 0.01, CACHE_WRITE_PRICE = 0.125;

    private static final ObjectMapper JSON = new ObjectMapper().enable(JsonGenerator.Feature.WRITE_BIGDECIMAL_AS_PLAIN);
    private static final boolean COLOR = System.getenv("NO_COLOR") == null;
    private static final Map<String, RoleStats> STATS = new LinkedHashMap<>();
    private static PrintWriter file;
    private static String filePath;

    private Log() {}

    public static void start() {
        try {
            Files.createDirectories(Path.of(Config.LOG_DIR));
            filePath = Path.of(Config.LOG_DIR, "run-" + LocalDateTime.now().format(DateTimeFormatter.ofPattern("yyyyMMdd-HHmmss")) + ".jsonl").toString();
            file = new PrintWriter(Files.newBufferedWriter(Path.of(filePath), StandardCharsets.UTF_8,
                StandardOpenOption.CREATE, StandardOpenOption.APPEND), true);
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
        System.out.println(paint("1", "Coding agent · model " + Config.MODEL + " · log " + filePath));
    }

    public static void transition(String from, String to, int revision) {
        write("transition", fields("from", from, "to", to, "revision", revision));
        System.out.println();
        System.out.println(paint("1;" + roleColor(to), "── " + from + " → " + to + " · revision " + revision + " ──"));
    }

    public static void modelCall(String role, int messageCount, Message response) {
        Usage usage = response.usage();
        String stopReason = response.stopReason().map(StopReason::asString).orElse("none");
        long cacheRead = usage.cacheReadInputTokens().orElse(0L);
        long cacheWrite = usage.cacheCreationInputTokens().orElse(0L);
        RoleStats stats = statsFor(role);
        stats.modelCalls++;
        stats.inputTokens += usage.inputTokens();
        stats.outputTokens += usage.outputTokens();
        stats.cacheReadTokens += cacheRead;
        stats.cacheWriteTokens += cacheWrite;

        write("model_call", fields(
            "role", role,
            "model", Config.MODEL,
            "message_count", messageCount,
            "input_tokens", usage.inputTokens(),
            "output_tokens", usage.outputTokens(),
            "cache_read_tokens", cacheRead,
            "cache_creation_tokens", cacheWrite,
            "stop_reason", stopReason));
        System.out.println(paint("2", "[" + role + "] model call: " + messageCount + " messages, input " + usage.inputTokens()
            + " (+" + cacheRead + " cached), output " + usage.outputTokens() + ", stop_reason " + stopReason));

        for (ContentBlock block : response.content()) {
            block.text().ifPresent(text -> {
                if (text.text().isBlank()) return;
                String prefix = "[" + role + "] ";
                String body = text.text().strip().replace("\n", "\n" + " ".repeat(prefix.length()));
                System.out.println(paint(roleColor(role), prefix + body));
            });
        }
    }

    public static void toolCall(String role, String name, JsonValue input) {
        statsFor(role).toolCalls++;
        String inputJson = toJson(input);
        write("tool_call", fields("role", role, "name", name, "input", input));
        System.out.println(paint(roleColor(role), "[" + role + "] → " + name + " " + shorten(inputJson, 300)));
    }

    public static void toolResult(String role, String name, boolean isError, long durationMs, String output) {
        write("tool_result", fields(
            "role", role,
            "name", name,
            "is_error", isError,
            "duration_ms", durationMs,
            "preview", output.length() > 500 ? output.substring(0, 500) : output));
        String status = isError ? "ERROR" : "ok";
        System.out.println(paint(isError ? "31" : "2", "[" + role + "] ← " + name + " " + status + " in " + durationMs + " ms: "
            + shorten(output.strip().replace("\n", " ⏎ "), 200)));
    }

    public static void capReached(String role, int toolCalls) {
        write("cap_reached", fields("role", role, "tool_calls", toolCalls));
        System.out.println(paint("31", "[" + role + "] stopped: more than " + Config.MAX_TOOL_CALLS + " tool calls"));
    }

    public static void verdict(int revision, String verdict) {
        write("verdict", fields("revision", revision, "verdict", verdict));
        System.out.println(paint(verdict.equals("APPROVED") ? "1;32" : "1;31",
            "[orchestrator] verdict for revision " + revision + ": " + verdict));
    }

    public static void info(String message) {
        System.out.println(paint("32", "[orchestrator] " + message));
    }

    /** approved is null when no Reviewer ran (run-agent --coder or --plan). */
    public static void runSummary(Boolean approved, int revisions) {
        Map<String, Object> roles = new LinkedHashMap<>();
        double cost = 0;
        for (var entry : STATS.entrySet()) {
            RoleStats s = entry.getValue();
            cost += s.cost();
            roles.put(entry.getKey(), fields(
                "model_calls", s.modelCalls,
                "tool_calls", s.toolCalls,
                "input_tokens", s.inputTokens,
                "output_tokens", s.outputTokens,
                "cache_read_tokens", s.cacheReadTokens,
                "cache_creation_tokens", s.cacheWriteTokens));
        }
        write("run_summary", fields(
            "approved", approved,
            "revisions", revisions,
            "roles", roles,
            "estimated_cost_usd", BigDecimal.valueOf(cost).setScale(6, RoundingMode.HALF_UP).stripTrailingZeros()));

        System.out.println();
        System.out.println(paint("1", "── summary ──"));
        String verdict = approved == null ? "not reviewed" : approved ? "yes" : "no";
        System.out.println(paint(Boolean.FALSE.equals(approved) ? "1;31" : "1;32", "approved: " + verdict + "   revisions: " + revisions));
        for (var entry : STATS.entrySet()) {
            RoleStats s = entry.getValue();
            System.out.println(String.format(Locale.ROOT, "%-9s model calls %3d   tool calls %3d   input %7d   cached %7d   output %6d",
                entry.getKey(), s.modelCalls, s.toolCalls, s.inputTokens, s.cacheReadTokens, s.outputTokens));
        }
        System.out.println(String.format(Locale.ROOT, "estimated cost: $%.4f (Haiku 5.5 list prices)", cost));
        if (filePath != null) System.out.println("log: " + filePath);
    }

    private static void write(String event, Map<String, Object> fields) {
        if (file == null) return;
        Map<String, Object> line = new LinkedHashMap<>();
        line.put("ts", Instant.now().toString());
        line.put("event", event);
        line.putAll(fields);
        file.println(toJson(line));
    }

    private static Map<String, Object> fields(Object... keysAndValues) {
        Map<String, Object> map = new LinkedHashMap<>();
        for (int i = 0; i < keysAndValues.length; i += 2) map.put((String) keysAndValues[i], keysAndValues[i + 1]);
        return map;
    }

    private static String toJson(Object value) {
        try {
            return JSON.writeValueAsString(value);
        } catch (JsonProcessingException e) {
            throw new IllegalStateException(e);
        }
    }

    private static RoleStats statsFor(String role) {
        return STATS.computeIfAbsent(role, r -> new RoleStats());
    }

    private static String roleColor(String role) {
        return switch (role) {
            case "planner" -> "36";
            case "coder" -> "33";
            case "reviewer" -> "35";
            default -> "32";
        };
    }

    private static String paint(String code, String text) {
        return COLOR ? "\u001b[" + code + "m" + text + "\u001b[0m" : text;
    }

    private static String shorten(String text, int max) {
        return text.length() > max ? text.substring(0, max) + "…" : text;
    }

    private static final class RoleStats {
        long modelCalls, toolCalls, inputTokens, outputTokens, cacheReadTokens, cacheWriteTokens;

        double cost() {
            return (inputTokens * INPUT_PRICE + outputTokens * OUTPUT_PRICE
                + cacheReadTokens * CACHE_READ_PRICE + cacheWriteTokens * CACHE_WRITE_PRICE) / 1_000_000;
        }
    }
}
