package codingagent;

import codingagent.agent.AgentLoop;
import codingagent.agent.Orchestrator;
import codingagent.support.Log;

import com.anthropic.errors.AnthropicException;
import com.anthropic.errors.AnthropicServiceException;
import com.anthropic.errors.BadRequestException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Arrays;
import java.util.List;

/**
 * Usage: run-agent [--coder | --plan] [task-file | --task "text"]
 *   (no flag)  Planner, Coder and Reviewer with revisions (needs TODO 1-7)
 *   --coder    the Coder on its own (needs TODO 1-3)
 *   --plan     the Planner, then the Coder once (needs TODO 1-5)
 * Exit codes: 0 approved or finished, 1 not approved, 2 TODO not implemented,
 * 3 spend limit reached, 4 other API error.
 */
public final class Main {
    private Main() {}

    public static void main(String[] args) throws Exception {
        System.exit(run(args));
    }

    static int run(String[] args) throws Exception {
        String mode = Arrays.stream(args).filter(a -> a.startsWith("--")).findFirst().orElse("");
        String taskFile = Arrays.stream(args).filter(a -> !a.startsWith("--")).findFirst().orElse("task.md");
        if (!(mode.isEmpty() || mode.equals("--coder") || mode.equals("--plan"))) {
            System.err.println("Usage: run-agent [--coder | --plan] [task-file | --task \"text\"]   (default task file: task.md)");
            return 2;
        }
        if (!Files.isRegularFile(Path.of(taskFile))) {
            System.err.println("Task file not found: " + taskFile);
            return 2;
        }
        // Everything above a "## Review" line is the task; the lines below it are the review
        // criteria, which only the Reviewer gets. No such line: no criteria.
        List<String> lines = Files.readString(Path.of(taskFile)).lines().toList();
        int split = -1;
        for (int i = 0; i < lines.size() && split < 0; i++) {
            if (lines.get(i).strip().equalsIgnoreCase("## Review")) split = i;
        }
        String task = String.join("\n", split < 0 ? lines : lines.subList(0, split)).strip();
        String criteria = split < 0 ? "" : String.join("\n", lines.subList(split + 1, lines.size())).strip();

        Log.start();
        try {
            switch (mode) {
                case "--coder" -> {
                    Log.transition("start", "coder", 0);
                    Orchestrator.code(task, "(no plan: the Coder is running on its own)", null);
                    Log.transition("coder", "end", 0);
                    Log.runSummary(null, 0);
                    return 0;
                }
                case "--plan" -> {
                    Log.transition("start", "planner", 0);
                    String plan = Orchestrator.plan(task);
                    Log.transition("planner", "coder", 0);
                    Orchestrator.code(task, plan, null);
                    Log.transition("coder", "end", 0);
                    Log.runSummary(null, 0);
                    return 0;
                }
                default -> {
                    return Orchestrator.run(task, criteria) ? 0 : 1;
                }
            }
        } catch (UnsupportedOperationException e) {
            // Only the TODO stubs mean "not implemented"; List.of(...).add(...) throws this too.
            if (e.getMessage() == null || !e.getMessage().startsWith("TODO ")) throw e;
            System.err.println("\nNot implemented yet: " + e.getMessage());
            return 2;
        } catch (BadRequestException e) {
            if (!String.valueOf(e.getMessage()).toLowerCase().contains("usage limit")) {
                System.err.println("\nAPI error: " + e.getMessage());
                return 4;
            }
            System.err.println("\nSpend limit reached for the workshop API key. Tell the facilitator.");
            return 3;
        } catch (AnthropicServiceException e) {
            System.err.println("\nAPI error: " + e.getMessage());
            return 4;
        } catch (AnthropicException e) { // no answer at all, e.g. the proxy isn't running
            System.err.println("\nAPI error: " + e.getMessage());
            return 4;
        }
    }
}
