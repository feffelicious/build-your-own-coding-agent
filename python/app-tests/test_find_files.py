"""The provided find_files tool, on a small workspace of its own."""
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


def test_find_files_double_star_crosses_folders():
    result = tools.execute_tool("find_files", {"pattern": "**/*.txt"})

    assert not result.is_error
    assert result.output == "src/app.txt\nsrc/deep/more.txt"


def test_find_files_single_star_stays_in_one_folder():
    assert tools.find_files("src/*").output == "src/app.txt"
    assert tools.find_files("*").output == "README.md"


def test_find_files_skips_hidden_and_build_folders_and_symlinked_folders():
    assert tools.find_files("**").output == "README.md\nsrc/app.txt\nsrc/deep/more.txt"


def test_find_files_no_match_is_not_an_error():
    result = tools.find_files("*.cs")

    assert not result.is_error
    assert result.output == "No files match *.cs"


def test_find_files_lists_at_most_200(workspace):
    (workspace / "many").mkdir()
    for i in range(205):
        (workspace / "many" / f"f{i:03}.txt").write_text("x")

    lines = tools.find_files("many/*").output.split("\n")

    assert len(lines) == 201
    assert lines[199] == "many/f199.txt"
    assert lines[200] == "... and 5 more. Use a narrower pattern."
