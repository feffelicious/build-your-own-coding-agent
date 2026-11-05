"""A stand-in for the Claude API, so the loop tests need no key: a local HTTP
server that answers each request with the next scripted reply and records what
your code sent. The `model` fixture in test_agent.py points agent_loop.client at it."""
import json
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


class FakeModel:
    def __init__(self):
        self.requests = []  # every request body your code sent, in order
        self.replies = []  # every reply's content blocks, in order
        self.when_script_ends = None  # what to answer once the script runs out
        self._script = []
        self._ids = 0
        fake = self

        class Handler(BaseHTTPRequestHandler):
            def do_POST(self):
                request = json.loads(self.rfile.read(int(self.headers.get("content-length", 0))))
                fake.requests.append(request)
                content = fake._next_reply()
                fake.replies.append(content)
                uses_tools = any(block["type"] == "tool_use" for block in content)
                body = json.dumps({
                    "id": fake._next_id("msg"), "type": "message", "role": "assistant",
                    "model": request.get("model", "test-model"), "content": content,
                    "stop_reason": "tool_use" if uses_tools else "end_turn", "stop_sequence": None,
                    "usage": {"input_tokens": 10, "output_tokens": 5,
                              "cache_creation_input_tokens": 0, "cache_read_input_tokens": 0},
                }).encode()
                self.send_response(200)
                self.send_header("content-type", "application/json")
                self.send_header("content-length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

            def log_message(self, *args):
                pass

        self._server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        threading.Thread(target=self._server.serve_forever, daemon=True).start()
        self.base_url = f"http://127.0.0.1:{self._server.server_port}"

    def says(self, text):
        """The model thinks, then finishes with this text (stop_reason "end_turn"), so the
        text is not the first block: use final_text, not content[0]."""
        self._script.append([{"type": "thinking", "thinking": "", "signature": self._next_id("sig")},
                             {"type": "text", "text": text}])
        return self

    def calls_tools(self, *calls):
        """The model thinks, then asks for one or more (name, input) tools (stop_reason "tool_use")."""
        self._script.append(self.tool_calls(*calls))
        return self

    def tool_calls(self, *calls):
        content = [{"type": "thinking", "thinking": "", "signature": self._next_id("sig")}]
        for name, tool_input in calls:
            content.append({"type": "tool_use", "id": self._next_id("toolu"), "name": name, "input": tool_input})
        return content

    def close(self):
        self._server.shutdown()

    def _next_reply(self):
        # Emergency brake: a loop without a working cap would otherwise run forever.
        if len(self.requests) > 40:
            return [{"type": "text", "text": "(the test stopped this loop after 40 calls)"}]
        if self._script:
            return self._script.pop(0)
        if self.when_script_ends:
            return self.when_script_ends(self)
        return [{"type": "text", "text": "done"}]

    def _next_id(self, prefix):
        self._ids += 1
        return f"{prefix}_{self._ids:03d}"


# Helpers for reading what was sent.

def text(value):
    """A "system" or "content" value as plain text (a string, or text blocks)."""
    if value is None:
        return ""
    if isinstance(value, str):
        return value
    return "\n".join(block.get("text", "") for block in value if isinstance(block, dict))


def tool_names(request):
    return [tool.get("name") for tool in request.get("tools", [])]


def essentials(block):
    """The parts of a content block that must survive the round trip unchanged."""
    keys = ("type", "id", "name", "input", "text", "thinking", "signature")
    return {key: block[key] for key in keys if isinstance(block, dict) and key in block}
