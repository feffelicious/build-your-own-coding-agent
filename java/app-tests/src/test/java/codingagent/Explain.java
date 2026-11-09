package codingagent;

/** Failure messages that say what your code should do, and show what it returned. */
final class Explain {
    private Explain() {}

    static String explain(String why) {
        return why;
    }

    static String explain(String why, Object actual) {
        String shown = actual instanceof String text ? "\"" + text + "\"" : String.valueOf(actual);   // "" shows as ""
        if (shown.length() > 400) shown = shown.substring(0, 400) + "…";
        return why + "\nYour code returned: " + shown;
    }
}
