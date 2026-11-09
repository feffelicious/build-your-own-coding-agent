"""What the model sees of each tool: a name, a description and a JSON schema for
the input. The model never runs anything; it only asks us to (see tools.py).
The descriptions are prompts too: the model knows a tool only from them. FIND_FILES and
SEARCH_TEXT, at the bottom, are finished examples. Which role gets which tools is up to
you: you pass each role its list in orchestrator.py."""

# Test: run-tests -k todo1_description
READ_FILE = {
    "name": "read_file",
    # TODO 1: describe read_file for the model
    # Hint: Say what it does and returns, and what the model must know to call it right: the path is relative to the workspace root, text files only. FIND_FILES below is an example.
    "description": "TODO 1: describe read_file",
    "input_schema": {
        "type": "object",
        "properties": {
            # TODO 1: describe the path parameter
            # Hint: Say what the value is, e.g. relative to what.
            "path": {"type": "string", "description": "TODO 1: describe the path parameter"},
        },
        "required": ["path"],
    },
}

# Test: run-tests -k todo2_description
WRITE_FILE = {
    "name": "write_file",
    # TODO 2: describe write_file for the model
    # Hint: Say that it creates or overwrites the whole file (a model that thinks it patches sends only the changed part, and the rest of the file is gone), that parent folders are created, and that the path is relative to the workspace root.
    "description": "TODO 2: describe write_file",
    "input_schema": {
        "type": "object",
        "properties": {
            # TODO 2: describe the path and content parameters
            # Hint: Say what each value is; for content, that it is the complete new file content.
            "path": {"type": "string", "description": "TODO 2: describe the path parameter"},
            "content": {"type": "string", "description": "TODO 2: describe the content parameter"},
        },
        "required": ["path", "content"],
    },
}

# No parameters: the model can't choose the command. It writes the tests, though, and a test can do
# anything a command can: what keeps that in check is in tools.run_command (the runner user).
# Test: run-tests -k "todo4_description or todo4_code"
# Then try it: reset-workspace, then run-agent --coder, and compare the trace with your first run
RUN_TESTS = {
    "name": "run_tests",
    # TODO 4: describe run_tests for the model
    # Hint: Read tools.run_tests and tools.run_command first (provided), then say what it runs, what comes back (the exit code, 0 when all tests pass, and the output, shortened in the middle when long), that build and syntax errors show up there too, and when to use it. Then give it to the Coder: add tool_schemas.RUN_TESTS to its tools in orchestrator.code, and tell it in its prompt to run the tests before it finishes.
    "description": "TODO 4: describe run_tests",
    "input_schema": {"type": "object", "properties": {}},
}

# Provided, for the run_command extra (CHEATSHEET.md): no role has it until you add it.
RUN_COMMAND = {
    "name": "run_command",
    "description": "Run a shell command (/bin/sh) in the workspace root. Returns the exit code and the "
    "combined output. Commands time out after 30 seconds. Use it to build and run tests.",
    "input_schema": {
        "type": "object",
        "properties": {
            "command": {"type": "string", "description": "The shell command to run"},
        },
        "required": ["command"],
    },
}

# Provided, like its code in tools.py (the read-only roles have no other way to see which
# files exist), and an example of a description: what it does, how to call it, what comes back, when to use it.
FIND_FILES = {
    "name": "find_files",
    "description": "Find files in the workspace whose path matches a pattern: * matches within one folder, "
    "** across folders, ? one character. Returns the matching paths relative to the workspace root, one per "
    "line. Use it to see which files exist before reading them.",
    "input_schema": {
        "type": "object",
        "properties": {
            "pattern": {"type": "string", "description": "Pattern relative to the workspace root, e.g. **/*.md or src/**"},
        },
        "required": ["pattern"],
    },
}

# Provided, like FIND_FILES: finds where something is used or defined without reading every file.
SEARCH_TEXT = {
    "name": "search_text",
    "description": "Search the text files in the workspace for lines matching a regular expression. Returns "
    "the matching lines as path:line number: text, at most 100. Use it to find where something is "
    "defined or used before reading whole files.",
    "input_schema": {
        "type": "object",
        "properties": {
            "pattern": {"type": "string", "description": "Regular expression to search for, e.g. class \\w+Test or divide\\("},
        },
        "required": ["pattern"],
    },
}
