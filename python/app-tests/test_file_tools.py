"""TODO 1 and 2. Each test gets its own temporary workspace."""
import os

import pytest

from explain import explain
from support import config
from tools import tools


@pytest.fixture
def workspace(tmp_path, monkeypatch):
    ws = tmp_path / "ws"
    ws.mkdir()
    monkeypatch.setattr(config, "WORKSPACE", str(ws))
    return ws


def test_todo1_resolve_in_workspace_accepts_nested_path(workspace):
    expected = os.path.join(workspace, "src", "deep", "a.txt")

    full = tools.resolve_in_workspace("src/deep/a.txt")

    assert full == expected, explain(
        f'resolve_in_workspace("src/deep/a.txt") should return {expected}. The model\'s paths are relative to '
        "config.WORKSPACE, not to the current directory: join them first, e.g. os.path.join(config.WORKSPACE, path).",
        full)


def test_todo1_resolve_in_workspace_refuses_parent_traversal(workspace):
    full = tools.resolve_in_workspace("../../../../etc/passwd")

    assert full is None, explain(
        'resolve_in_workspace("../../../../etc/passwd") should return None: after resolving the ..s it points '
        "outside the workspace.",
        full)

    parent = tools.resolve_in_workspace("sub/../..")

    assert parent is None, explain(
        'resolve_in_workspace("sub/../..") should return None: it is the folder above the workspace. Its relative '
        'path is exactly "..", without a slash, so a check for "../" alone misses it.',
        parent)


def test_todo1_resolve_in_workspace_refuses_absolute_path_outside(workspace):
    full = tools.resolve_in_workspace("/etc/passwd")

    assert full is None, explain(
        'resolve_in_workspace("/etc/passwd") should return None. Note that os.path.join(workspace, "/etc/passwd") '
        'returns "/etc/passwd" itself, so your containment check has to catch it.',
        full)


def test_todo1_resolve_in_workspace_refuses_symlink_pointing_outside(workspace):
    os.symlink("/etc/passwd", workspace / "innocent.txt")
    os.symlink("/etc", workspace / "etc-link")

    via_file = tools.resolve_in_workspace("innocent.txt")
    via_directory = tools.resolve_in_workspace("etc-link/passwd")

    assert via_file is None, explain(
        'resolve_in_workspace("innocent.txt") should return None: it is a symlink inside the workspace that points '
        "to /etc/passwd. Resolve symlinks with os.path.realpath before checking where the path really is.",
        via_file)
    assert via_directory is None, explain(
        'resolve_in_workspace("etc-link/passwd") should return None: etc-link is a symlinked folder pointing to /etc. '
        "Resolve symlinks with os.path.realpath before checking where the path really is.",
        via_directory)


def test_todo1_resolve_in_workspace_refuses_sibling_with_same_prefix(workspace):
    evil = workspace.parent / (workspace.name + "-evil")
    evil.mkdir()

    absolute = tools.resolve_in_workspace(str(evil / "x"))
    relative = tools.resolve_in_workspace(f"../{evil.name}/x")

    assert absolute is None and relative is None, explain(
        f"resolve_in_workspace should return None for {evil}/x: a sibling folder whose name starts with the "
        "workspace's name. A string test like full.startswith(workspace) wrongly accepts it; check with "
        "os.path.commonpath([workspace, full]) instead.",
        absolute or relative)


def test_todo1_resolve_in_workspace_accepts_name_starting_with_dots(workspace):
    expected = os.path.join(workspace, "..notes.md")

    full = tools.resolve_in_workspace("..notes.md")

    assert full == expected, explain(
        f'resolve_in_workspace("..notes.md") should return {expected}: a name that starts with two dots is still '
        'inside the workspace. Only a relative path that is exactly ".." or starts with "../" leaves it, so a test '
        'like relpath.startswith("..") is too broad; os.path.commonpath([workspace, full]) gets it right.',
        full)


def test_todo1_read_file_reads_nested_file(workspace):
    (workspace / "src" / "deep").mkdir(parents=True)
    (workspace / "src" / "deep" / "a.txt").write_text("hello")

    result = tools.read_file("src/deep/a.txt")

    assert not result.is_error and result.output == "hello", explain(
        'read_file("src/deep/a.txt") should succeed (is_error False) with the file\'s text "hello" as the output.',
        result)


def test_todo1_read_file_refuses_path_outside(workspace):
    result = tools.read_file("../../../../etc/passwd")

    assert result.is_error and "root:" not in result.output, explain(
        'read_file("../../../../etc/passwd") should return an error result (is_error True) without the file\'s '
        "content: check the path with resolve_in_workspace first.",
        result)


def test_todo1_read_file_reports_missing_file(workspace):
    result = tools.read_file("nope.txt")

    assert result.is_error, explain(
        'read_file("nope.txt") should return an error result (is_error True) for a file that doesn\'t exist, not raise.',
        result)


def test_todo1_read_file_says_which_problem_it_is(workspace):
    outside = tools.read_file("../outside.txt")
    missing = tools.read_file("missing.txt")

    assert outside.is_error and missing.is_error and outside.output != missing.output, explain(
        "A path outside the workspace and a file that doesn't exist need different errors: the model fixes the path "
        "in one case and looks for the file elsewhere in the other. Check resolve_in_workspace's None and "
        "os.path.isfile separately.",
        f"outside: {outside.output} | missing: {missing.output}")


def test_todo1_read_file_refuses_file_over_limit_and_suggests_smaller(workspace):
    (workspace / "big.txt").write_text("x" * (config.FILE_LIMIT + 1))

    result = tools.read_file("big.txt")

    assert result.is_error, explain(
        f"read_file should refuse a file over config.FILE_LIMIT ({config.FILE_LIMIT} bytes) with an error result "
        "(is_error True).",
        result)
    assert "smaller" in result.output, explain(
        "The model reads this error to decide what to do next, so tell it: suggest reading a smaller file "
        '(this test looks for the word "smaller").',
        result)
    assert str(config.FILE_LIMIT + 1) in result.output, explain(
        "Say how big the file is, so the model knows how far over the limit it is: the error should contain its "
        f"size in bytes ({config.FILE_LIMIT + 1}).",
        result)


def test_todo2_write_file_creates_parent_directories(workspace):
    result = tools.write_file("new/dir/file.txt", "content")
    file = workspace / "new" / "dir" / "file.txt"

    assert not result.is_error, explain(
        'write_file("new/dir/file.txt", ...) should succeed (is_error False) and create the missing folders new/dir.',
        result)
    assert file.exists() and file.read_text() == "content", explain(
        f"{file} should exist and contain exactly the content that was passed in.",
        result)


def test_todo2_write_file_overwrites_and_reports_bytes(workspace):
    tools.write_file("f.txt", "old content that is longer")

    result = tools.write_file("f.txt", "nyå")
    written = (workspace / "f.txt").read_text(encoding="utf-8")

    assert not result.is_error, explain(
        'write_file("f.txt", "nyå") should succeed (is_error False): replacing an existing file is a normal write.',
        result)
    assert "4 bytes" in result.output, explain(
        'write_file("f.txt", "nyå") should report the bytes written: "nyå" is 4 bytes in UTF-8 '
        '(å takes 2), so the output should contain "4 bytes".',
        result)
    assert written == "nyå", explain(
        "f.txt should be overwritten with exactly the new content, not appended to.",
        written)


def test_todo2_write_file_refuses_paths_outside(workspace):
    evil = workspace.parent / (workspace.name + "-evil")
    evil.mkdir()

    parent = tools.write_file(f"../{evil.name}/x", "bad")
    absolute = tools.write_file(str(evil / "y"), "bad")

    assert parent.is_error and absolute.is_error, explain(
        'write_file should return an error result for paths outside the workspace ("../..." and absolute): '
        "check them with resolve_in_workspace, the same as read_file.",
        absolute if parent.is_error else parent)
    assert list(evil.iterdir()) == [], explain(
        f"Nothing may be written outside the workspace, but {evil} now has files in it.")


def test_todo2_write_file_refuses_content_over_limit(workspace):
    result = tools.write_file("big.txt", "x" * (config.FILE_LIMIT + 1))

    assert result.is_error, explain(
        f"write_file should refuse content over config.FILE_LIMIT ({config.FILE_LIMIT} bytes, counted as UTF-8) "
        "with an error result (is_error True).",
        result)
    assert not (workspace / "big.txt").exists(), explain(
        "write_file should check the size before writing: big.txt should not have been created.")

    # Under the limit in characters, over it in bytes: å takes 2 bytes in UTF-8.
    wide = tools.write_file("wide.txt", "å" * (config.FILE_LIMIT // 2 + 1))

    assert wide.is_error and not (workspace / "wide.txt").exists(), explain(
        f"write_file should count the limit in bytes, not characters: {config.FILE_LIMIT // 2 + 1} å characters are "
        f'{config.FILE_LIMIT + 2} bytes in UTF-8, over the limit. Use len(content.encode("utf-8")), not len(content).',
        wide)
