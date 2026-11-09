package codingagent.tools;

import codingagent.support.Config;

import com.anthropic.core.JsonValue;
import java.io.File;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.FileVisitResult;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.SimpleFileVisitor;
import java.nio.file.attribute.BasicFileAttributes;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Set;
import java.util.concurrent.TimeUnit;
import java.util.regex.Pattern;
import java.util.regex.PatternSyntaxException;

/**
 * The code behind the tools: you write read_file, write_file and the output
 * truncation; run_tests, run_command, find_files and search_text are provided. Every tool returns a
 * ToolResult and never throws: problems go back to the model as error results it can react to.
 */
public final class Tools {
    public record ToolResult(String output, boolean isError) {}

    private Tools() {}

    /** Runs the tool the model asked for. Provided. */
    public static ToolResult executeTool(String name, JsonValue input) {
        try {
            return switch (name) {
                case "read_file" -> readFile(Args.string(input, "path"));
                case "write_file" -> writeFile(Args.string(input, "path"), Args.string(input, "content"));
                case "run_tests" -> runTests();
                case "run_command" -> runCommand(Args.string(input, "command"));
                case "find_files" -> findFiles(Args.string(input, "pattern"));
                case "search_text" -> searchText(Args.string(input, "pattern"));
                default -> new ToolResult("Unknown tool: " + name, true);
            };
        } catch (Exception e) {
            return new ToolResult(e.getClass().getSimpleName() + ": " + e.getMessage(), true);
        }
    }

    /**
     * Returns the full path for a path the model gave us, or null if it points
     * outside the workspace (after following symlinks).
     */
    // Test: run-tests -Dtest='*#todo1ResolveInWorkspace*'
    public static Path resolveInWorkspace(String path) throws IOException {
        Path workspace = realPath(Path.of(Config.WORKSPACE));
        // An absolute path replaces the workspace in resolve(); the check below catches it.
        Path full = realPath(workspace.resolve(path));
        return full.startsWith(workspace) ? full : null;
    }

    // Test: run-tests -Dtest='*#todo1ReadFile*'
    public static ToolResult readFile(String path) throws IOException {
        Path full = resolveInWorkspace(path);
        if (full == null) return new ToolResult("Refused: " + path + " is outside the workspace", true);
        if (!Files.isRegularFile(full)) return new ToolResult("File not found: " + path, true);
        long size = Files.size(full);
        if (size > Config.FILE_LIMIT) {
            return new ToolResult(path + " is " + size + " bytes, over the " + Config.FILE_LIMIT + " byte limit. Read a smaller file.", true);
        }
        try {
            return new ToolResult(Files.readString(full), false);
        } catch (java.nio.charset.CharacterCodingException e) {
            return new ToolResult(path + " is not a UTF-8 text file", true);
        }
    }

    // Test: run-tests -Dtest='*#todo2WriteFile*'
    public static ToolResult writeFile(String path, String content) throws IOException {
        Path full = resolveInWorkspace(path);
        if (full == null) return new ToolResult("Refused: " + path + " is outside the workspace", true);
        byte[] bytes = content.getBytes(StandardCharsets.UTF_8);
        if (bytes.length > Config.FILE_LIMIT) {
            return new ToolResult("Content is " + bytes.length + " bytes, over the " + Config.FILE_LIMIT + " byte limit.", true);
        }
        if (Files.isDirectory(full)) return new ToolResult(path + " is a directory", true);
        Files.createDirectories(full.getParent());
        Files.write(full, bytes);
        return new ToolResult("Wrote " + bytes.length + " bytes to " + path, false);
    }

    /**
     * Runs the project's tests (Config.TEST_COMMAND, e.g. mvn test) with runCommand below.
     * Provided: read both before you describe run_tests (TODO 4). The model picks no command
     * here, but the tests run code the model wrote, so they need the same sandbox as any command.
     */
    // Test: run-tests -Dtest='*#todo4RunTests*'
    public static ToolResult runTests() throws IOException, InterruptedException {
        return runCommand(Config.TEST_COMMAND);
    }

    /**
     * Runs a shell command in the workspace. Provided. run_tests uses it; the model can ask
     * for any command only once you give a role run_command (an extra, see CHEATSHEET.md).
     * The sandbox is here, not in the model's good behaviour: the command runs as the user
     * "runner", who can't read the agent's code or write outside /workspace, and the real
     * API key lives only in the proxy.
     */
    // Test: run-tests -Dtest='*#todo4RunCommand*'
    public static ToolResult runCommand(String command) throws IOException, InterruptedException {
        // timeout ends the command after COMMAND_TIMEOUT_S seconds (and kills it 2 s later if it ignores that).
        // Output goes to a temporary file, not a pipe: a background process that
        // keeps a pipe open would otherwise make us wait for it.
        File output = File.createTempFile("run_command", ".log");
        try {
            Process process = new ProcessBuilder("sudo", "-u", "runner", "timeout", "-k", "2",
                    String.valueOf(Config.COMMAND_TIMEOUT_S), "/bin/sh", "-c", command)
                .directory(new File(Config.WORKSPACE))
                .redirectInput(ProcessBuilder.Redirect.from(new File("/dev/null")))
                .redirectErrorStream(true)
                .redirectOutput(output)
                .start();
            if (!process.waitFor(Config.commandBackstopS(), TimeUnit.SECONDS)) {
                return new ToolResult("Command did not finish within " + Config.commandBackstopS() + " s", true);
            }
            String text = new String(Files.readAllBytes(output.toPath()), StandardCharsets.UTF_8);
            int exitCode = process.exitValue();
            if (exitCode == 124 || exitCode == 137) {
                return new ToolResult("Timed out after " + Config.COMMAND_TIMEOUT_S + " s (exit code " + exitCode + ")\n" + truncate(text), true);
            }
            return new ToolResult("exit code " + exitCode + "\n" + truncate(text), false);
        } finally {
            output.delete();
        }
    }

    /** Keeps the first fifth and the last four fifths of long output. */
    // Test: run-tests -Dtest='*#todo4Truncate*'
    public static String truncate(String text) {
        // TODO 4: implement output truncation
        // Hint: If text is longer than Config.OUTPUT_LIMIT, keep the first 2,000 and last 8,000 characters with a [truncated] marker between them.
        throw new UnsupportedOperationException("TODO 4: implement output truncation");
    }

    /**
     * Like realpath(3): an absolute path with every symlink resolved, also for
     * paths that don't exist yet (the missing tail is kept as is). Provided.
     */
    public static Path realPath(Path path) throws IOException {
        return realPath(path.toAbsolutePath(), 0);
    }

    private static Path realPath(Path path, int depth) throws IOException {
        if (depth > 40) throw new IOException("Too many levels of symbolic links: " + path);
        Path current = path.getRoot();
        for (Path part : path) {
            String name = part.toString();
            if (name.equals(".")) continue;
            if (name.equals("..")) {
                current = current.getParent() == null ? current : current.getParent();
                continue;
            }
            Path next = current.resolve(name);
            current = Files.isSymbolicLink(next)
                ? realPath(current.resolve(Files.readSymbolicLink(next)), depth + 1)
                : next;
        }
        return current;
    }

    // Folders find_files and search_text never look into, besides hidden ones like .git: build output.
    private static final Set<String> SKIPPED_FOLDERS = Set.of("bin", "obj", "target", "__pycache__", "node_modules");
    private static final int FIND_FILES_LIMIT = 200;
    private static final int SEARCH_TEXT_LIMIT = 100;

    /** Lists the workspace files whose relative path matches the pattern, sorted. Provided. */
    public static ToolResult findFiles(String pattern) throws IOException {
        Pattern regex = Pattern.compile(patternToRegex(pattern));
        List<String> matches = new ArrayList<>();
        for (String file : workspaceFiles()) {
            if (regex.matcher(file).matches()) matches.add(file);
        }
        if (matches.isEmpty()) return new ToolResult("No files match " + pattern, false);
        return listed(matches, FIND_FILES_LIMIT);
    }

    /**
     * Lists the lines of the workspace's text files that match a regular expression, as
     * path:line: text. Provided. Skips symlinks (they may point outside the workspace),
     * files over Config.FILE_LIMIT and files that aren't UTF-8 text.
     */
    public static ToolResult searchText(String pattern) throws IOException {
        Pattern regex;
        try {
            regex = Pattern.compile(pattern);
        } catch (PatternSyntaxException e) {
            return new ToolResult("Invalid regular expression: " + pattern, true);
        }
        Path workspace = realPath(Path.of(Config.WORKSPACE));
        List<String> matches = new ArrayList<>();
        for (String file : workspaceFiles()) {
            Path full = workspace.resolve(file);
            String text;
            try {
                if (Files.isSymbolicLink(full) || Files.size(full) > Config.FILE_LIMIT) continue;
                text = Files.readString(full);
            } catch (IOException e) {
                continue;   // not UTF-8 (MalformedInputException), or unreadable
            }
            String[] lines = text.split("\n", -1);
            for (int i = 0; i < lines.length; i++) {
                String line = lines[i].endsWith("\r") ? lines[i].substring(0, lines[i].length() - 1) : lines[i];
                if (regex.matcher(line).find()) matches.add(file + ":" + (i + 1) + ": " + line);
            }
        }
        if (matches.isEmpty()) return new ToolResult("No lines match " + pattern, false);
        return listed(matches, SEARCH_TEXT_LIMIT);
    }

    // The first limit items, one per line, and how many more there are.
    private static ToolResult listed(List<String> items, int limit) {
        String shown = String.join("\n", items.subList(0, Math.min(items.size(), limit)));
        if (items.size() > limit) {
            shown += "\n... and " + (items.size() - limit) + " more. Use a narrower pattern.";
        }
        return new ToolResult(shown, false);
    }

    // Every file in the workspace, as a path relative to it, sorted. Doesn't enter hidden,
    // build or symlinked folders (a symlinked folder may point outside the workspace).
    private static List<String> workspaceFiles() throws IOException {
        Path workspace = realPath(Path.of(Config.WORKSPACE));
        List<String> files = new ArrayList<>();
        Files.walkFileTree(workspace, new SimpleFileVisitor<>() {
            @Override
            public FileVisitResult preVisitDirectory(Path folder, BasicFileAttributes attrs) {
                String name = folder.getFileName().toString();
                boolean skip = !folder.equals(workspace) && (name.startsWith(".") || SKIPPED_FOLDERS.contains(name));
                return skip ? FileVisitResult.SKIP_SUBTREE : FileVisitResult.CONTINUE;
            }

            @Override
            public FileVisitResult visitFile(Path file, BasicFileAttributes attrs) {
                // Without FOLLOW_LINKS a symlinked folder arrives here; it may point outside the workspace.
                if (attrs.isSymbolicLink() && Files.isDirectory(file)) return FileVisitResult.CONTINUE;
                files.add(workspace.relativize(file).toString());
                return FileVisitResult.CONTINUE;
            }

            @Override
            public FileVisitResult visitFileFailed(Path file, IOException e) {
                return FileVisitResult.CONTINUE;
            }
        });
        Collections.sort(files);
        return files;
    }

    // * stays within one folder, ** crosses folders ("**/" also matches no folder), ? is one character.
    private static String patternToRegex(String pattern) {
        if (pattern.startsWith("./")) pattern = pattern.substring(2);
        StringBuilder regex = new StringBuilder();
        for (int i = 0; i < pattern.length(); i++) {
            char c = pattern.charAt(i);
            if (pattern.startsWith("**/", i)) {
                regex.append("(?:.*/)?");
                i += 2;
            } else if (pattern.startsWith("**", i)) {
                regex.append(".*");
                i += 1;
            } else if (c == '*') {
                regex.append("[^/]*");
            } else if (c == '?') {
                regex.append("[^/]");
            } else {
                regex.append(Pattern.quote(String.valueOf(c)));
            }
        }
        return regex.toString();
    }
}
