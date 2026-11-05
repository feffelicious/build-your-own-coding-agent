"""Failure messages that say what your code should do, and show what it returned."""

_NOTHING = object()


def explain(why, actual=_NOTHING):
    if actual is _NOTHING:
        return why
    shown = repr(actual)
    if len(shown) > 400:
        shown = shown[:400] + "…"
    return f"{why}\nYour code returned: {shown}"
