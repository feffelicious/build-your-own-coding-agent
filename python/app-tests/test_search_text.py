"""The provided search_text tool, on a small workspace of its own."""
import os

import pytest

from support import config
from tools import tools


@pytest.fixture(autouse=True)
def workspace(tmp_path, monkeypatch):
    ws = tmp_path / "ws"
    files = {
        "README.md": "hello\nworld\n",
        "src/app.txt": "one\r\ntwo hello\r\n",
        ".git/config": "hello",
        "bin/out.txt": "hello",
        "big.txt": "hello" + "x" * config.FILE_LIMIT,
    }
    for file, content in files.items():
        (ws / file).parent.mkdir(parents=True, exist_ok=True)
        (ws / file).write_bytes(content.encode("utf-8"))
    (ws / "data.bin").write_bytes(b"\xffhello")
    os.symlink("/etc/passwd", ws / "passwd")
    monkeypatch.setattr(config, "WORKSPACE", str(ws))
    return ws


def test_search_text_lists_matching_lines_with_path_and_number():
    result = tools.execute_tool("search_text", {"pattern": "hello"})

    # Not .git, bin, a file over the limit, a binary file or a symlink.
    assert not result.is_error
    assert result.output == "README.md:1: hello\nsrc/app.txt:2: two hello"


def test_search_text_uses_regular_expressions():
    assert tools.search_text("^w.r").output == "README.md:2: world"


def test_search_text_skips_symlinked_files():
    assert tools.search_text("root:").output == "No lines match root:"


def test_search_text_no_match_is_not_an_error():
    result = tools.search_text("zzz")

    assert not result.is_error
    assert result.output == "No lines match zzz"


def test_search_text_invalid_pattern_is_an_error():
    result = tools.search_text("(")

    assert result.is_error
    assert result.output == "Invalid regular expression: ("


def test_search_text_lists_at_most_100(workspace):
    (workspace / "many").mkdir()
    for i in range(105):
        (workspace / "many" / f"f{i:03}.txt").write_text("hit")

    lines = tools.search_text("hit").output.split("\n")

    assert len(lines) == 101
    assert lines[99] == "many/f099.txt:1: hit"
    assert lines[100] == "... and 5 more. Use a narrower pattern."
