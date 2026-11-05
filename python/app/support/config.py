"""Every setting in one place. Each value can be overridden by an environment
variable with the same name, e.g. MAX_TOOL_CALLS=3 run-agent"""
import os


def _text(name, fallback):
    return os.environ.get(name) or fallback


def _number(name, fallback):
    try:
        return int(os.environ[name])
    except (KeyError, ValueError):
        return fallback


MODEL = _text("MODEL", "claude-haiku-5-5")
MAX_TOKENS = _number("MAX_TOKENS", 4000)
MAX_TOOL_CALLS = _number("MAX_TOOL_CALLS", 20)
MAX_REVISIONS = _number("MAX_REVISIONS", 2)
WORKSPACE = _text("WORKSPACE", "/workspace")
TEST_COMMAND = _text("TEST_COMMAND", "pytest -q")
COMMAND_TIMEOUT_S = _number("COMMAND_TIMEOUT_S", 30)
OUTPUT_LIMIT = _number("OUTPUT_LIMIT", 10_000)  # keep the first fifth and the last four fifths
FILE_LIMIT = _number("FILE_LIMIT", 100 * 1024)  # bytes
LOG_DIR = _text("LOG_DIR", "/home/agent/logs")


def command_backstop_s():
    """Our own wait on a command, in case GNU timeout itself hangs."""
    return COMMAND_TIMEOUT_S + 5
