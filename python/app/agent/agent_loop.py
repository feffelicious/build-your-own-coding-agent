"""The agent loop: send the conversation, run the tools the model asks for,
send the results back, repeat until the model stops asking."""
import time

import anthropic

from support import config
from support import log
from agent import prompts
from tools import tool_schemas
from tools.tools import ToolResult, execute_tool

# Reads ANTHROPIC_API_KEY and ANTHROPIC_BASE_URL from the environment.
# The tests swap in a scripted model here, so they need no API key.
client = anthropic.Anthropic(max_retries=4)


# Test: run-tests -k todo3_run_agent
def run_agent(role, system_prompt, tools, user_message):
    """One fresh conversation per call, with the tools this role may use. Returns the model's final text."""
    messages = [{"role": "user", "content": user_message}]
    tool_calls = 0
    while True:
        response = client.messages.create(
            model=config.MODEL,
            max_tokens=config.MAX_TOKENS,
            system=system_prompt,
            tools=tools,
            messages=messages,
            cache_control={"type": "ephemeral"},  # cache the conversation prefix between calls
        )
        log.model_call(role, len(messages), response)

        # The assistant turn goes back into the history exactly as received.
        messages.append({"role": "assistant", "content": response.content})

        if response.stop_reason != "tool_use":
            return final_text(response)

        results = []
        for block in response.content:
            if block.type != "tool_use":
                continue
            if tool_calls == config.MAX_TOOL_CALLS:
                log.cap_reached(role, tool_calls)
                return f"[stopped: more than {config.MAX_TOOL_CALLS} tool calls]"
            tool_calls += 1
            log.tool_call(role, block.name, block.input)
            started = time.monotonic()
            # The tools we send only tell the model what it may use; this is where it's enforced.
            if offers(tools, block.name):
                result = execute_tool(block.name, block.input)
            else:
                result = ToolResult(f"Refused: {block.name} is not one of your tools", True)
            log.tool_result(role, block.name, result.is_error, int((time.monotonic() - started) * 1000), result.output)
            results.append({
                "type": "tool_result",
                "tool_use_id": block.id,
                "content": result.output,
                "is_error": result.is_error,
            })
        # Tool results go back as one *user* message, matched to the calls by ID.
        messages.append({"role": "user", "content": results})


def offers(tools, name):
    """Whether tools has a tool with this name. Provided."""
    return any(tool["name"] == name for tool in tools)


def final_text(response):
    """The text blocks of a response, joined. Provided."""
    return "\n".join(block.text for block in response.content if block.type == "text").strip()
