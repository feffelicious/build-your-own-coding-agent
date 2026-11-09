"""Reads values out of a tool call's input."""


def string(tool_input, key):
    """args.string(block.input, "path") -> the "path" value, or an exception the
    tool dispatcher turns into an error result for the model."""
    value = tool_input.get(key) if isinstance(tool_input, dict) else None
    if not isinstance(value, str):
        raise ValueError(f"Missing string argument '{key}'")
    return value
