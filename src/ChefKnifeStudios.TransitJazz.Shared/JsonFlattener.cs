using System.Collections.Generic;
using System.Text.Json;

namespace ChefKnifeStudios.TransitJazz.Shared;

public static class JsonFlattener
{
    public static Dictionary<string, object?> Flatten<T>(T? value)
    {
        var result = new Dictionary<string, object?>();
        if (value is null)
            return result;

        var json = JsonSerializer.SerializeToUtf8Bytes(value, JsonSettings.DefaultOptions);
        using var doc = JsonDocument.Parse(json);
        FlattenElement(result, string.Empty, doc.RootElement);
        return result;
    }

    private static void FlattenElement(Dictionary<string, object?> result, string prefix, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Null)
                        continue;
                    FlattenElement(result, string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}", prop.Value);
                }
                break;

            case JsonValueKind.Array:
                var arr = new List<object?>();
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Null)
                        arr.Add(null);
                    else if (item.ValueKind == JsonValueKind.Object || item.ValueKind == JsonValueKind.Array)
                        arr.Add(Flatten<object?>(JsonSerializer.Deserialize<object?>(item.GetRawText(), JsonSettings.DefaultOptions)));
                    else
                        arr.Add(JsonElementToObject(item));
                }
                result[prefix] = arr.ToArray();
                break;

            case JsonValueKind.Null:
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
