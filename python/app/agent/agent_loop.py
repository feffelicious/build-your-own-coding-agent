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
    # TODO 3: implement run_agent, the tool loop
    # Hint: Follow the steps below; the SDK calls are in CHEATSHEET.md.
    # 1. Start the conversation: a list with one user message, user_message. Count tool calls from 0.
    # 2. Loop: send the whole list with client.messages.create(model=, max_tokens=, system=, tools=,
    #    messages=, cache_control={"type": "ephemeral"}). Then log.model_call(role, len(messages), response).
    # 3. Append the assistant turn exactly as received: {"role": "assistant", "content": response.content}.
    # 4. If response.stop_reason isn't "tool_use", the model is done: return final_text(response).
    # 5. For each block with block.type == "tool_use":
    #    - already ran config.MAX_TOOL_CALLS tools? log.cap_reached, return "[stopped: more than N tool calls]"
    #    - log.tool_call, run execute_tool(block.name, block.input), log.tool_result (with how long it took)
    #    - but only run tools this role was given (offers(tools, block.name)); any other gets the error
    #      result "Refused: <name> is not one of your tools"
    #    - collect {"type": "tool_result", "tool_use_id": block.id, "content": ..., "is_error": ...}
    # 6. Append ONE user message holding all the results, then go round again.
    raise NotImplementedError("TODO 3: implement run_agent, the tool loop")


def offers(tools, name):
    """Whether tools has a tool with this name. Provided."""
    return any(tool["name"] == name for tool in tools)


def final_text(response):
    """The text blocks of a response, joined. Provided."""
    return "\n".join(block.text for block in response.content if block.type == "text").strip()
