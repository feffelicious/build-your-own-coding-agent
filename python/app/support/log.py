"""Compact, colored console output plus one JSON line per event in
LOG_DIR/run-<timestamp>.jsonl. Provided: you call it, you don't change it."""
import json
import os
from datetime import datetime, timezone

from support import config

# Haiku 5.5 list prices in USD per million tokens, for the cost estimate.
INPUT_PRICE, OUTPUT_PRICE, CACHE_READ_PRICE, CACHE_WRITE_PRICE = 0.10, 0.50, 0.01, 0.125

_color = "NO_COLOR" not in os.environ
_stats = {}
_file = None
file_path = None


def start():
    global _file, file_path
    os.makedirs(config.LOG_DIR, exist_ok=True)
    file_path = os.path.join(config.LOG_DIR, f"run-{datetime.now():%Y%m%d-%H%M%S}.jsonl")
    _file = open(file_path, "a", encoding="utf-8", buffering=1)
    print(_paint("1", f"Coding agent · model {config.MODEL} · log {file_path}"))


def transition(from_, to, revision):
    _write("transition", {"from": from_, "to": to, "revision": revision})
    print()
    print(_paint("1;" + _role_color(to), f"── {from_} → {to} · revision {revision} ──"))


def model_call(role, message_count, response):
    usage = response.usage
    stop_reason = response.stop_reason or "none"
    cache_read = usage.cache_read_input_tokens or 0
    cache_write = usage.cache_creation_input_tokens or 0
    stats = _stats_for(role)
    stats["model_calls"] += 1
    stats["input_tokens"] += usage.input_tokens
    stats["output_tokens"] += usage.output_tokens
    stats["cache_read_tokens"] += cache_read
    stats["cache_creation_tokens"] += cache_write

    _write("model_call", {
        "role": role,
        "model": config.MODEL,
        "message_count": message_count,
        "input_tokens": usage.input_tokens,
        "output_tokens": usage.output_tokens,
        "cache_read_tokens": cache_read,
        "cache_creation_tokens": cache_write,
        "stop_reason": stop_reason,
    })
    print(_paint("2", f"[{role}] model call: {message_count} messages, input {usage.input_tokens} "
                      f"(+{cache_read} cached), output {usage.output_tokens}, stop_reason {stop_reason}"))

    for block in response.content:
        if block.type == "text" and block.text.strip():
            prefix = f"[{role}] "
            body = block.text.strip().replace("\n", "\n" + " " * len(prefix))
            print(_paint(_role_color(role), prefix + body))


def tool_call(role, name, tool_input):
    _stats_for(role)["tool_calls"] += 1
    input_json = json.dumps(tool_input, ensure_ascii=False, separators=(",", ":"))
    _write("tool_call", {"role": role, "name": name, "input": tool_input})
    print(_paint(_role_color(role), f"[{role}] → {name} {_shorten(input_json, 300)}"))


def tool_result(role, name, is_error, duration_ms, output):
    _write("tool_result", {
        "role": role,
        "name": name,
        "is_error": is_error,
        "duration_ms": duration_ms,
        "preview": output[:500],
    })
    status = "ERROR" if is_error else "ok"
    preview = _shorten(output.strip().replace("\n", " ⏎ "), 200)
    print(_paint("31" if is_error else "2", f"[{role}] ← {name} {status} in {duration_ms} ms: {preview}"))


def cap_reached(role, tool_calls):
    _write("cap_reached", {"role": role, "tool_calls": tool_calls})
    print(_paint("31", f"[{role}] stopped: more than {config.MAX_TOOL_CALLS} tool calls"))


def verdict(revision, verdict_):
    _write("verdict", {"revision": revision, "verdict": verdict_})
    print(_paint("1;32" if verdict_ == "APPROVED" else "1;31",
                 f"[orchestrator] verdict for revision {revision}: {verdict_}"))


def info(message):
    print(_paint("32", f"[orchestrator] {message}"))


def run_summary(approved, revisions):
    """approved is None when no Reviewer ran (run-agent --coder or --plan)."""
    cost = sum(_cost(s) for s in _stats.values())
    _write("run_summary", {
        "approved": approved,
        "revisions": revisions,
        "roles": _stats,
        "estimated_cost_usd": round(cost, 6),
    })

    print()
    print(_paint("1", "── summary ──"))
    verdict_text = {True: "yes", False: "no", None: "not reviewed"}[approved]
    print(_paint("1;31" if approved is False else "1;32", f"approved: {verdict_text}   revisions: {revisions}"))
    for role, s in _stats.items():
        print(f"{role:<9} model calls {s['model_calls']:>3}   tool calls {s['tool_calls']:>3}   "
              f"input {s['input_tokens']:>7}   cached {s['cache_read_tokens']:>7}   output {s['output_tokens']:>6}")
    print(f"estimated cost: ${cost:.4f} (Haiku 5.5 list prices)")
    if file_path:
        print(f"log: {file_path}")


def _write(event, fields):
    if _file is None:
        return
    ts = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%fZ")
    _file.write(json.dumps({"ts": ts, "event": event, **fields}, ensure_ascii=False, separators=(",", ":")) + "\n")


def _stats_for(role):
    return _stats.setdefault(role, {
        "model_calls": 0, "tool_calls": 0, "input_tokens": 0, "output_tokens": 0,
        "cache_read_tokens": 0, "cache_creation_tokens": 0,
    })


def _cost(s):
    return (s["input_tokens"] * INPUT_PRICE + s["output_tokens"] * OUTPUT_PRICE
            + s["cache_read_tokens"] * CACHE_READ_PRICE + s["cache_creation_tokens"] * CACHE_WRITE_PRICE) / 1_000_000


def _role_color(role):
    return {"planner": "36", "coder": "33", "reviewer": "35"}.get(role, "32")


def _paint(code, text):
    return f"\x1b[{code}m{text}\x1b[0m" if _color else text


def _shorten(text, limit):
    return text[:limit] + "…" if len(text) > limit else text
