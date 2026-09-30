using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ClientAgent.Service.Config;

/// <summary>Describes, in plain words, what changed between two versions of the "Setting" section.</summary>
public static partial class SettingsChanges
{
    private const int MaxValueLength = 40;
    private static readonly string[] Secret = ["Password", "ConnectionString", "Secret", "Token", "AccessKey"];
    private static readonly string[] Opaque = ["Icon"];

    public static List<string> Describe(JsonObject before, JsonObject after)
    {
        var changes = new List<string>();
        foreach (var key in before.Select(pair => pair.Key).Union(after.Select(pair => pair.Key)))
        {
            if (key == "MonitorPoints")
            {
                changes.AddRange(DescribePoints(before[key] as JsonArray, after[key] as JsonArray));
                continue;
            }

            var section = key == "General" ? string.Empty : $"{Humanize(key)} > ";
            changes.AddRange(DescribeValues(Flatten(before[key]), Flatten(after[key]), path => section + Label(path)));
        }

        return changes;
    }

    private static IEnumerable<string> DescribePoints(JsonArray? before, JsonArray? after)
    {
        var old = Points(before);
        var current = Points(after);
        foreach (var (id, point) in current.Where(pair => !old.ContainsKey(pair.Key)))
        {
            yield return $"Monitor point added: {Name(point, id)}";
        }

        foreach (var (id, point) in old.Where(pair => !current.ContainsKey(pair.Key)))
        {
            yield return $"Monitor point removed: {Name(point, id)}";
        }

        foreach (var (id, point) in current.Where(pair => old.ContainsKey(pair.Key)))
        {
            var name = Name(point, id);
            foreach (var change in DescribeValues(Flatten(old[id]), Flatten(point), Label))
            {
                yield return $"Monitor point \"{name}\": {change}";
            }
        }
    }

    private static IEnumerable<string> DescribeValues(
        Dictionary<string, string> before, Dictionary<string, string> after, Func<string, string> label)
    {
        foreach (var path in before.Keys.Union(after.Keys))
        {
            before.TryGetValue(path, out var oldValue);
            after.TryGetValue(path, out var newValue);
            if (oldValue == newValue)
            {
                continue;
            }

            if (Secret.Any(word => path.Contains(word, StringComparison.OrdinalIgnoreCase))
                || Opaque.Any(word => path.EndsWith(word, StringComparison.OrdinalIgnoreCase)))
            {
                yield return $"{label(path)} changed";
                continue;
            }

            yield return $"{label(path)}: {Format(oldValue)} -> {Format(newValue)}";
        }
    }

    private static Dictionary<string, JsonObject> Points(JsonArray? points)
    {
        var result = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        if (points is null)
        {
            return result;
        }

        for (var i = 0; i < points.Count; i++)
        {
            if (points[i] is JsonObject point)
            {
                var id = point["MonitorPointId"]?.GetValue<string>();
                result[string.IsNullOrWhiteSpace(id) ? $"#{i + 1}" : id] = point;
            }
        }

        return result;
    }

    private static string Name(JsonObject point, string id) =>
        point["DisplayName"]?.GetValue<string>() is { Length: > 0 } name ? name : id;

    private static Dictionary<string, string> Flatten(JsonNode? node)
    {
        var leaves = new Dictionary<string, string>();
        Flatten(node, string.Empty, leaves);
        return leaves;
    }

    private static void Flatten(JsonNode? node, string path, Dictionary<string, string> leaves)
    {
        switch (node)
        {
            case JsonObject obj when obj.Count > 0:
                foreach (var (key, child) in obj)
                {
                    Flatten(child, path.Length == 0 ? key : $"{path}.{key}", leaves);
                }
                break;
            case JsonArray array when array.Count > 0:
                for (var i = 0; i < array.Count; i++)
                {
                    Flatten(array[i], $"{path}[{i}]", leaves);
                }
                break;
            default:
                leaves[path] = node?.ToJsonString() ?? "null";
                break;
        }
    }

    private static string Label(string path) =>
        string.Join(" > ", path.Split('.').Select(Humanize));

    private static string Format(string? json)
    {
        if (json is null or "null" or "\"\"")
        {
            return "(empty)";
        }

        var node = JsonNode.Parse(json);
        var text = node is JsonValue value && value.TryGetValue<string>(out var s) ? s : json;
        return text.Length > MaxValueLength ? text[..MaxValueLength] + "..." : text;
    }

    /// <summary>"RefreshIntervalSeconds" becomes "Refresh Interval Seconds", "CpuMinCores" becomes "CPU Min Cores".</summary>
    private static string Humanize(string key)
    {
        var words = WordBoundary().Split(key).Select(word => word switch
        {
            "Cpu" => "CPU",
            "Ram" => "RAM",
            "Os" => "OS",
            "Gb" => "GB",
            "Url" => "URL",
            "Api" => "API",
            _ => word
        });
        return string.Join(' ', words);
    }

    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])")]
    private static partial Regex WordBoundary();
}
