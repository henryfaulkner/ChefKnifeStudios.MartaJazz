using System.Collections.Generic;
using System.Text.Json;

namespace ChefKnifeStudios.MartaJazz.Shared;

public static class JsonFlattener
{
    public static Dictionary<string, object?> Flatten(object? value)
    {
        var result = new Dictionary<string, object?>();
        if (value is null) return result;

        // Use a JsonElement directly to avoid double-serialization overhead
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value));
        FlattenElement(result, "", doc.RootElement);
        return result;
    }

    private static void FlattenElement(Dictionary<string, object?> result, string prefix, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    string name = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                    FlattenElement(result, name, prop.Value);
                }
                break;
            case JsonValueKind.Array:
                int index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    FlattenElement(result, $"{prefix}[{index}]", item);
                    index++;
                }
                break;
            default:
                result[prefix] = JsonElementToObject(element);
                break;
        }
    }

    private static object? JsonElementToObject(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };
}
