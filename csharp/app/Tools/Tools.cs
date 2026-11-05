using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodingAgent;

public record ToolResult(string Output, bool IsError);

// The code behind the tools: you write read_file, write_file and the output
// truncation; run_tests, run_command, glob and grep are provided. Every tool returns a
// ToolResult and never throws: problems go back to the model as error results it can react to.
public static class Tools
{
    // Runs the tool the model asked for. Provided.
    public static ToolResult ExecuteTool(string name, IReadOnlyDictionary<string, JsonElement> input)
    {
        try
        {
            return name switch
            {
                "read_file" => ReadFile(Args.String(input, "path")),
                "write_file" => WriteFile(Args.String(input, "path"), Args.String(input, "content")),
                "run_tests" => RunTests(),
                "run_command" => RunCommand(Args.String(input, "command")),
                "glob" => Glob(Args.String(input, "pattern")),
                "grep" => Grep(Args.String(input, "pattern")),
                _ => new ToolResult($"Unknown tool: {name}", true),
            };
        }
        catch (Exception e)
        {
            return new ToolResult($"{e.GetType().Name}: {e.Message}", true);
        }
    }

    // Returns the full path for a path the model gave us, or null if it points
    // outside the workspace (after following symlinks).
    // Test: run-tests --filter Todo1_ResolveInWorkspace
    public static string? ResolveInWorkspace(string path)
    {
        // TODO 1: implement the workspace path check
        // Hint: Path.Combine(Config.Workspace, path) first (a relative path would otherwise resolve against the current directory), resolve symlinks with RealPath (the workspace too), then check containment with Path.GetRelativePath (not a string prefix test).
        throw new NotImplementedException("TODO 1: implement the workspace path check");
    }

    // Test: run-tests --filter Todo1_ReadFile
    public static ToolResult ReadFile(string path)
    {
        // TODO 1: implement read_file
        // Hint: Use ResolveInWorkspace; refuse files over Config.FileLimit bytes with an error that tells the model to read a smaller file; return the text.
        throw new NotImplementedException("TODO 1: implement read_file");
    }

    // Test: run-tests --filter Todo2_WriteFile
    public static ToolResult WriteFile(string path, string content)
    {
        // TODO 2: implement write_file
        // Hint: Same path check; refuse content over Config.FileLimit bytes; create parent directories; return the bytes written.
        throw new NotImplementedException("TODO 2: implement write_file");
    }

    // Runs the project's tests (Config.TestCommand, e.g. dotnet test) with RunCommand below.
    // Provided: read both before you describe run_tests (TODO 4). The model picks no command
    // here, but the tests run code the model wrote, so they need the same sandbox as any command.
    // Test: run-tests --filter Todo4_RunTests
    public static ToolResult RunTests() => RunCommand(Config.TestCommand);

    // Runs a shell command in the workspace. Provided. run_tests uses it; the model can ask
    // for any command only once you give a role run_command (an extra, see CHEATSHEET.md).
    // The sandbox is here, not in the model's good behaviour: the command runs as the user
    // "runner", who can't read the agent's code or write outside /workspace, and the real
    // API key lives only in the proxy.
    // Test: run-tests --filter Todo4_RunCommand
    public static ToolResult RunCommand(string command)
    {
        // timeout ends the command after Config.CommandTimeoutS seconds (and kills it 2 s later if it ignores that).
        var psi = new ProcessStartInfo("sudo")
        {
            WorkingDirectory = Config.Workspace,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "-u", "runner", "timeout", "-k", "2", Config.CommandTimeoutS.ToString(), "/bin/sh", "-c", command })
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        process.StandardInput.Close();   // commands that wait for input get end-of-file at once
        var output = new StringBuilder();
        var readers = new[] { Pump(process.StandardOutput, output), Pump(process.StandardError, output) };

        if (!process.WaitForExit(Config.CommandBackstopS * 1000))
        {
            return new ToolResult($"Command did not finish within {Config.CommandBackstopS} s", true);
        }
        // A background process can keep the pipes open after the shell exits; don't wait for it.
        Task.WaitAll(readers, TimeSpan.FromSeconds(2));
        string text;
        lock (output) text = output.ToString();

        var exitCode = process.ExitCode;
        if (exitCode is 124 or 137)
        {
            return new ToolResult($"Timed out after {Config.CommandTimeoutS} s (exit code {exitCode})\n{Truncate(text)}", true);
        }
        return new ToolResult($"exit code {exitCode}\n{Truncate(text)}", false);
    }

    // Keeps the first fifth and the last four fifths of long output.
    // Test: run-tests --filter Todo4_Truncate
    public static string Truncate(string text)
    {
        // TODO 4: implement output truncation
        // Hint: If text is longer than Config.OutputLimit, keep the first 2,000 and last 8,000 characters with a [truncated] marker between them.
        throw new NotImplementedException("TODO 4: implement output truncation");
    }

    // Folders glob and grep never look into, besides hidden ones like .git: build output.
    static readonly HashSet<string> SkippedFolders = ["bin", "obj", "target", "__pycache__", "node_modules"];
    const int GlobLimit = 200;
    const int GrepLimit = 100;

    // Lists the workspace files whose relative path matches the pattern, sorted. Provided.
    public static ToolResult Glob(string pattern)
    {
        var regex = new Regex(GlobToRegex(pattern));
        var matches = WorkspaceFiles().Where(file => regex.IsMatch(file)).ToList();
        if (matches.Count == 0) return new ToolResult($"No files match {pattern}", false);
        return Listed(matches, GlobLimit);
    }

    // Lists the lines of the workspace's text files that match a regular expression, as
    // path:line: text. Provided. Skips symlinks (they may point outside the workspace),
    // files over Config.FileLimit and files that aren't UTF-8 text.
    public static ToolResult Grep(string pattern)
    {
        Regex regex;
        try { regex = new Regex(pattern); }
        catch (ArgumentException) { return new ToolResult($"Invalid regular expression: {pattern}", true); }
        var workspace = RealPath(Config.Workspace);
        var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
        List<string> matches = [];
        foreach (var file in WorkspaceFiles())
        {
            var info = new FileInfo(Path.Combine(workspace, file));
            if (info.LinkTarget is not null || info.Length > Config.FileLimit) continue;
            string text;
            try { text = strict.GetString(File.ReadAllBytes(info.FullName)); }
            catch (Exception e) when (e is DecoderFallbackException or IOException or UnauthorizedAccessException) { continue; }
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                if (regex.IsMatch(line)) matches.Add($"{file}:{i + 1}: {line}");
            }
        }
        if (matches.Count == 0) return new ToolResult($"No lines match {pattern}", false);
        return Listed(matches, GrepLimit);
    }

    // The first limit items, one per line, and how many more there are.
    static ToolResult Listed(List<string> items, int limit)
    {
        var shown = string.Join("\n", items.Take(limit));
        return new ToolResult(items.Count <= limit ? shown
            : $"{shown}\n... and {items.Count - limit} more. Use a narrower pattern.", false);
    }

    // Every file in the workspace, as a path relative to it, sorted. Doesn't enter hidden,
    // build or symlinked folders (a symlinked folder may point outside the workspace).
    static List<string> WorkspaceFiles()
    {
        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true };
        List<string> files = [];
        Walk(new DirectoryInfo(RealPath(Config.Workspace)), "");
        files.Sort(string.CompareOrdinal);
        return files;

        void Walk(DirectoryInfo folder, string prefix)
        {
            foreach (var entry in folder.EnumerateFileSystemInfos("*", options))
            {
                var relative = prefix + entry.Name;
                if (entry is not DirectoryInfo sub) files.Add(relative);
                else if (sub.LinkTarget is null && !sub.Name.StartsWith('.') && !SkippedFolders.Contains(sub.Name))
                    Walk(sub, relative + "/");
            }
        }
    }

    // * stays within one folder, ** crosses folders ("**/" also matches no folder), ? is one character.
    static string GlobToRegex(string pattern)
    {
        if (pattern.StartsWith("./")) pattern = pattern[2..];
        var regex = new StringBuilder("^");
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                var slash = i + 2 < pattern.Length && pattern[i + 2] == '/';
                regex.Append(slash ? "(?:.*/)?" : ".*");
                i += slash ? 2 : 1;
            }
            else if (pattern[i] == '*') regex.Append("[^/]*");
            else if (pattern[i] == '?') regex.Append("[^/]");
            else regex.Append(Regex.Escape(pattern[i].ToString()));
        }
        return regex.Append('$').ToString();
    }

    // Copies everything a stream produces into a shared buffer. Provided.
    static Task Pump(StreamReader reader, StringBuilder output) => Task.Run(async () =>
    {
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer)) > 0)
        {
            lock (output) output.Append(buffer, 0, count);
        }
    });

    // Like realpath(3): an absolute path with every symlink resolved, also for
    // paths that don't exist yet (the missing tail is kept as is). Provided.
    public static string RealPath(string path, int depth = 0)
    {
        if (depth > 40) throw new IOException($"Too many levels of symbolic links: {path}");
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        var current = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var next = Path.Combine(current, part);
            var target = new FileInfo(next).LinkTarget;
            current = target is null ? next : RealPath(Path.Combine(current, target), depth + 1);
        }
        return current;
    }
}
