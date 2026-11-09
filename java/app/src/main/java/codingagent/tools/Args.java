package codingagent.tools;

import com.anthropic.core.JsonValue;
import java.util.Map;
import java.util.Optional;

/** Reads values out of a tool call's input, so you never touch the JsonValue API yourself. */
public final class Args {
    private Args() {}

    /**
     * Args.string(toolUse._input(), "path") -> the "path" value, or an exception the
     * tool dispatcher turns into an error result for the model.
     */
    public static String string(JsonValue input, String key) {
        Optional<Map<String, JsonValue>> object = input.asObject();
        Optional<String> value = object.map(o -> o.get(key)).flatMap(JsonValue::asString);
        return value.orElseThrow(() -> new IllegalArgumentException("Missing string argument '" + key + "'"));
    }
}
