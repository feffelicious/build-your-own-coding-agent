using System.Text.Json;

namespace CodingAgent;

// Reads values out of a tool call's input, so you never touch JsonElement yourself.
public static class Args
{
    // Args.String(toolUse.Input, "path") -> the "path" value, or an exception the
    // tool dispatcher turns into an error result for the model.
    public static string String(IReadOnlyDictionary<string, JsonElement> input, string key)
    {
        if (!input.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException($"Missing string argument '{key}'");
        }
        return value.GetString()!;
    }
}
