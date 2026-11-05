"""The provided glob tool, on a small workspace of its own."""
import os

import pytest

from support import config
from tools import tools


@pytest.fixture(autouse=True)
def workspace(tmp_path, monkeypatch):
    ws = tmp_path / "ws"
    for file in ["README.md", "src/app.txt", "src/deep/more.txt", ".git/config", "bin/out.txt"]:
        (ws / file).parent.mkdir(parents=True, exist_ok=True)
        (ws / file).write_text("x")
    os.symlink("/etc", ws / "outside")
    monkeypatch.setattr(config, "WORKSPACE", str(ws))
    return ws


def test_glob_double_star_crosses_folders():
    result = tools.execute_tool("glob", {"pattern": "**/*.txt"})

    assert not result.is_error
    assert result.output == "src/app.txt\nsrc/deep/more.txt"


def test_glob_single_star_stays_in_one_folder():
    assert tools.glob("src/*").output == "src/app.txt"
    assert tools.glob("*").output == "README.md"


def test_glob_skips_hidden_and_build_folders_and_symlinked_folders():
    assert tools.glob("**").output == "README.md\nsrc/app.txt\nsrc/deep/more.txt"


def test_glob_no_match_is_not_an_error():
    result = tools.glob("*.cs")

    assert not result.is_error
    assert result.output == "No files match *.cs"


def test_glob_lists_at_most_200(workspace):
    (workspace / "many").mkdir()
    for i in range(205):
        (workspace / "many" / f"f{i:03}.txt").write_text("x")

    lines = tools.glob("many/*").output.split("\n")

    assert len(lines) == 201
    assert lines[199] == "many/f199.txt"
    assert lines[200] == "... and 5 more. Use a narrower pattern."
