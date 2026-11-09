import os
import sys
import textwrap
import traceback

import pytest

# The tests import the app's modules directly.
APP = os.path.realpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "app"))
sys.path.insert(0, APP)


# A failing test shows its message and, if the error came from your code, where; not
# pytest's pages of source. run-tests --full sets RUN_TESTS_FULL for pytest's own report.
@pytest.hookimpl(hookwrapper=True)
def pytest_runtest_makereport(item, call):
    outcome = yield
    report = outcome.get_result()
    if report.failed and call.excinfo is not None and not os.environ.get("RUN_TESTS_FULL"):
        report.longrepr = _short(call.excinfo)


def _short(excinfo):
    error = excinfo.value
    if isinstance(error, (AssertionError, NotImplementedError)):
        text = str(error).split("\nassert ")[0]  # pytest adds its own "assert ..." explanation
    else:
        text = f"{type(error).__name__}: {error}"
    lines = ["      " + line.strip() for line in text.splitlines() if line.strip()]
    for frame in reversed(traceback.extract_tb(excinfo.tb)):
        path = os.path.realpath(frame.filename)
        if path.startswith(APP + os.sep):
            lines.append(f"      at {os.path.relpath(path, APP)}:{frame.lineno} ({frame.name})")
            break
    return "\n".join(lines)


_seen = set()


# Your code doesn't import (a syntax error, say): every test file fails to load with the same
# error. Show it once, without pytest's import machinery around it.
def pytest_collectreport(report):
    if report.failed and not os.environ.get("RUN_TESTS_FULL"):
        lines = [line[2:] for line in str(report.longrepr).splitlines() if line.startswith("E ")]
        if lines:
            short = textwrap.dedent("\n".join(lines)).replace(APP + os.sep, "")
            report.longrepr = "      (the same error as above)" if short in _seen else textwrap.indent(short, "      ")
            _seen.add(short)


def pytest_terminal_summary(terminalreporter):
    if (terminalreporter.stats.get("failed") or terminalreporter.stats.get("error")) and not os.environ.get("RUN_TESTS_FULL"):
        terminalreporter.write_line("run-tests --full shows the tracebacks.")
