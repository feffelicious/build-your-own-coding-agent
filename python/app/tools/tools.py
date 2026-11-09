"""The code behind the tools: you write read_file, write_file and the output
truncation; run_tests, run_command, find_files and search_text are provided. Every tool returns a
ToolResult and never raises: problems go back to the model as error results it can react to."""
import os
import re
import subprocess
import tempfile
from typing import NamedTuple

from tools import args
from support import config


class ToolResult(NamedTuple):
    output: str
    is_error: bool


def execute_tool(name, tool_input):
    """Runs the tool the model asked for. Provided."""
    try:
        if name == "read_file":
            return read_file(args.string(tool_input, "path"))
        if name == "write_file":
            return write_file(args.string(tool_input, "path"), args.string(tool_input, "content"))
        if name == "run_tests":
            return run_tests()
        if name == "run_command":
            return run_command(args.string(tool_input, "command"))
        if name == "find_files":
            return find_files(args.string(tool_input, "pattern"))
        if name == "search_text":
            return search_text(args.string(tool_input, "pattern"))
        return ToolResult(f"Unknown tool: {name}", True)
    except Exception as e:
        return ToolResult(f"{type(e).__name__}: {e}", True)


# Test: run-tests -k todo1_resolve_in_workspace
def resolve_in_workspace(path):
    """Returns the full path for a path the model gave us, or None if it points
    outside the workspace (after following symlinks)."""
    workspace = os.path.realpath(config.WORKSPACE)
    # An absolute path replaces the workspace in os.path.join; the check below catches it.
    full = os.path.realpath(os.path.join(workspace, path))
    if os.path.commonpath([workspace, full]) != workspace:
        return None
    return full


# Test: run-tests -k todo1_read_file
def read_file(path):
    full = resolve_in_workspace(path)
    if full is None:
        return ToolResult(f"Refused: {path} is outside the workspace", True)
    if not os.path.isfile(full):
        return ToolResult(f"File not found: {path}", True)
    size = os.path.getsize(full)
    if size > config.FILE_LIMIT:
        return ToolResult(f"{path} is {size} bytes, over the {config.FILE_LIMIT} byte limit. Read a smaller file.", True)
    try:
        with open(full, encoding="utf-8") as f:
            return ToolResult(f.read(), False)
    except UnicodeDecodeError:
        return ToolResult(f"{path} is not a UTF-8 text file", True)


# Test: run-tests -k todo2_write_file
def write_file(path, content):
    full = resolve_in_workspace(path)
    if full is None:
        return ToolResult(f"Refused: {path} is outside the workspace", True)
    size = len(content.encode("utf-8"))
    if size > config.FILE_LIMIT:
        return ToolResult(f"Content is {size} bytes, over the {config.FILE_LIMIT} byte limit.", True)
    if os.path.isdir(full):
        return ToolResult(f"{path} is a directory", True)
    os.makedirs(os.path.dirname(full), exist_ok=True)
    with open(full, "w", encoding="utf-8") as f:
        f.write(content)
    return ToolResult(f"Wrote {size} bytes to {path}", False)


# Test: run-tests -k todo4_run_tests
def run_tests():
    """Runs the project's tests (config.TEST_COMMAND, e.g. pytest) with run_command below.
    Provided: read both before you describe run_tests (TODO 4). The model picks no command
    here, but the tests run code the model wrote, so they need the same sandbox as any command."""
    return run_command(config.TEST_COMMAND)


# Test: run-tests -k todo4_run_command
def run_command(command):
    """Runs a shell command in the workspace. Provided. run_tests uses it; the model can ask
    for any command only once you give a role run_command (an extra, see CHEATSHEET.md).
    The sandbox is here, not in the model's good behaviour: the command runs as the user
    "runner", who can't read the agent's code or write outside /workspace, and the real
    API key lives only in the proxy."""
    # timeout ends the command after COMMAND_TIMEOUT_S seconds (and kills it 2 s later if it ignores that).
    argv = ["sudo", "-u", "runner", "timeout", "-k", "2", str(config.COMMAND_TIMEOUT_S), "/bin/sh", "-c", command]
    # Output goes to a temporary file, not a pipe: a background process that
    # keeps a pipe open would otherwise make us wait for it.
    with tempfile.TemporaryFile() as output:
        process = subprocess.Popen(argv, cwd=config.WORKSPACE, stdin=subprocess.DEVNULL,
                                   stdout=output, stderr=subprocess.STDOUT)
        try:
            exit_code = process.wait(timeout=config.command_backstop_s())
        except subprocess.TimeoutExpired:
            return ToolResult(f"Command did not finish within {config.command_backstop_s()} s", True)
        output.seek(0)
        text = output.read().decode("utf-8", errors="replace")

    if exit_code in (124, 137):
        return ToolResult(f"Timed out after {config.COMMAND_TIMEOUT_S} s (exit code {exit_code})\n{truncate(text)}", True)
    return ToolResult(f"exit code {exit_code}\n{truncate(text)}", False)


# Test: run-tests -k todo4_truncate
def truncate(text):
    """Keeps the first fifth and the last four fifths of long output."""
    if len(text) <= config.OUTPUT_LIMIT:
        return text
    head = config.OUTPUT_LIMIT // 5
    tail = config.OUTPUT_LIMIT - head
    return text[:head] + f"\n[truncated {len(text) - head - tail} characters]\n" + text[-tail:]


# Folders find_files and search_text never look into, besides hidden ones like .git: build output.
SKIPPED_FOLDERS = {"bin", "obj", "target", "__pycache__", "node_modules"}
FIND_FILES_LIMIT = 200
SEARCH_TEXT_LIMIT = 100


def find_files(pattern):
    """Lists the workspace files whose relative path matches the pattern, sorted. Provided."""
    regex = re.compile(_pattern_to_regex(pattern))
    matches = [file for file in _workspace_files() if regex.fullmatch(file)]
    if not matches:
        return ToolResult(f"No files match {pattern}", False)
    return _listed(matches, FIND_FILES_LIMIT)


def search_text(pattern):
    """Lists the lines of the workspace's text files that match a regular expression, as
    path:line: text. Provided. Skips symlinks (they may point outside the workspace),
    files over config.FILE_LIMIT and files that aren't UTF-8 text."""
    try:
        regex = re.compile(pattern)
    except re.error:
        return ToolResult(f"Invalid regular expression: {pattern}", True)
    workspace = os.path.realpath(config.WORKSPACE)
    matches = []
    for file in _workspace_files():
        full = os.path.join(workspace, file)
        if os.path.islink(full) or os.path.getsize(full) > config.FILE_LIMIT:
            continue
        try:
            with open(full, "rb") as f:
                text = f.read().decode("utf-8")
        except (UnicodeDecodeError, OSError):
            continue
        for number, line in enumerate(text.split("\n"), start=1):
            line = line.rstrip("\r")
            if regex.search(line):
                matches.append(f"{file}:{number}: {line}")
    if not matches:
        return ToolResult(f"No lines match {pattern}", False)
    return _listed(matches, SEARCH_TEXT_LIMIT)


def _listed(items, limit):
    """The first limit items, one per line, and how many more there are."""
    shown = "\n".join(items[:limit])
    if len(items) > limit:
        shown += f"\n... and {len(items) - limit} more. Use a narrower pattern."
    return ToolResult(shown, False)


def _workspace_files():
    """Every file in the workspace, as a path relative to it, sorted. Doesn't enter hidden,
    build or symlinked folders (os.walk doesn't follow symlinks, which may point outside)."""
    workspace = os.path.realpath(config.WORKSPACE)
    files = []
    for folder, subfolders, names in os.walk(workspace):
        subfolders[:] = [d for d in subfolders if not d.startswith(".") and d not in SKIPPED_FOLDERS]
        prefix = os.path.relpath(folder, workspace)
        files += [name if prefix == "." else f"{prefix}/{name}" for name in names]
    return sorted(files)


def _pattern_to_regex(pattern):
    """* stays within one folder, ** crosses folders ("**/" also matches no folder), ? is one character."""
    if pattern.startswith("./"):
        pattern = pattern[2:]
    regex = ""
    i = 0
    while i < len(pattern):
        if pattern.startswith("**/", i):
            regex += "(?:.*/)?"
            i += 3
        elif pattern.startswith("**", i):
            regex += ".*"
            i += 2
        elif pattern[i] == "*":
            regex += "[^/]*"
            i += 1
        elif pattern[i] == "?":
            regex += "[^/]"
            i += 1
        else:
            regex += re.escape(pattern[i])
            i += 1
    return regex
