"""The provided tool dispatcher, and TODO 6 verdict parsing."""
import pytest

from explain import explain
from tools import tools
from agent.orchestrator import parse_verdict


def test_execute_tool_unknown_tool_is_an_error():
    result = tools.execute_tool("delete_everything", {})

    assert result.is_error
    assert "Unknown tool" in result.output


def test_execute_tool_missing_argument_is_an_error():
    result = tools.execute_tool("read_file", {"file": "x"})

    assert result.is_error
    assert "path" in result.output


@pytest.mark.parametrize("review, expected", [
    ("APPROVED", "APPROVED"),
    ("APPROVED\nAll criteria pass.", "APPROVED"),
    ("  APPROVED  \n", "APPROVED"),
    ("CHANGES_REQUIRED\n- divide truncates", "CHANGES_REQUIRED"),
    ("Approved", "CHANGES_REQUIRED"),
    ("APPROVED.", "CHANGES_REQUIRED"),
    ("I think this is APPROVED", "CHANGES_REQUIRED"),
    ("CHANGES_REQUIRED\nAPPROVED", "CHANGES_REQUIRED"),
    ("", "CHANGES_REQUIRED"),
])
def test_todo6_parse_verdict(review, expected):
    verdict = parse_verdict(review)

    assert verdict == expected, explain(
        f"parse_verdict({review!r}) should be {expected}: only a first line that is exactly APPROVED after "
        "trimming counts as approval; anything else, including no text at all, is CHANGES_REQUIRED.",
        verdict)
