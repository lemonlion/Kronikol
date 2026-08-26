using System.Text;
using System.Text.Json;

namespace Kronikol.Tool.Query;

/// <summary>
/// The one place path semantics live: parsing the dotted grammar, walking a body, rendering concrete
/// paths. Every command that mentions a JSON path — <c>--path</c>, <c>values</c>, <c>--where</c>,
/// <c>grep --values</c> — goes through here, so <c>$.a.b[2]</c> means the same thing everywhere and
/// every emitted row is itself a valid <c>--path</c> input.
///
/// <para>Grammar (a superset of the original <c>$.a.b[2].c</c>):
/// <c>.name</c> object property · <c>[2]</c> array index · <c>[*]</c> every element (fans the traversal
/// out; on an object, every property value) · <c>['a.b']</c> bracket-quoted property, for keys containing
/// dots · <c>.length()</c> terminal only — array element count, object property count, string char
/// count.</para>
/// </summary>
internal static class PathEngine
{
    internal readonly record struct Segment(string? Name, int? Index, bool Wildcard = false, bool Length = false);

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static bool TryParse(string path, out List<Segment> segments, out string? error)
    {
        segments = [];
        error = null;
        var text = path.Trim();
        if (text.StartsWith('$'))
            text = text[1..];

        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '.')
            {
                i++;
                continue;
            }

            if (text[i] == '[')
            {
                if (i + 1 < text.Length && text[i + 1] == '\'')
                {
                    var closeQuote = text.IndexOf('\'', i + 2);
                    if (closeQuote < 0 || closeQuote + 1 >= text.Length || text[closeQuote + 1] != ']')
                    {
                        error = $"unclosed ['…'] at position {i} of {path}";
                        return false;
                    }
                    segments.Add(new Segment(text[(i + 2)..closeQuote], null));
                    i = closeQuote + 2;
                    continue;
                }

                var close = text.IndexOf(']', i);
                if (close < 0)
                {
                    error = $"unclosed [ at position {i} of {path}";
                    return false;
                }
                var inside = text[(i + 1)..close];
                if (inside == "*")
                {
                    segments.Add(new Segment(null, null, Wildcard: true));
                }
                else if (int.TryParse(inside, out var index) && index >= 0)
                {
                    segments.Add(new Segment(null, index));
                }
                else
                {
                    error = $"[{inside}] is not an index — use [2], [*] or ['a key']";
                    return false;
                }
                i = close + 1;
                continue;
            }

            var end = i;
            while (end < text.Length && text[end] != '.' && text[end] != '[')
                end++;
            var name = text[i..end];
            i = end;

            if (name == "length()")
            {
                segments.Add(new Segment(null, null, Length: true));
                continue;
            }
            if (name.EndsWith("()", StringComparison.Ordinal))
            {
                error = $"{name} is not a function — only .length() is";
                return false;
            }
            if (name.Length > 0)
                segments.Add(new Segment(name, null));
        }

        for (var s = 0; s < segments.Count - 1; s++)
            if (segments[s].Length)
            {
                error = ".length() must be the last segment";
                return false;
            }

        return true;
    }

    /// <summary>
    /// Every value the path selects, each with the concrete path that re-selects exactly it. A wildcard
    /// fans the traversal out; a miss on any branch simply yields nothing for that branch — absence is
    /// reported by the caller, which knows whether it is an answer or an error.
    /// </summary>
    public static IEnumerable<(string Path, PathValue Value)> SelectAll(JsonElement root, IReadOnlyList<Segment> segments) =>
        Walk(root, "$", segments, 0);

    private static IEnumerable<(string, PathValue)> Walk(JsonElement element, string path, IReadOnlyList<Segment> segments, int at)
    {
        if (at == segments.Count)
        {
            yield return (path, new PathValue(element));
            yield break;
        }

        var segment = segments[at];

        if (segment.Length)
        {
            int? count = element.ValueKind switch
            {
                JsonValueKind.Array => element.GetArrayLength(),
                JsonValueKind.Object => element.EnumerateObject().Count(),
                JsonValueKind.String => element.GetString()!.Length,
                _ => null
            };
            if (count is { } c)
                yield return (path + ".length()", PathValue.OfCount(c));
            yield break;
        }

        if (segment.Wildcard)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var found in Walk(item, $"{path}[{index}]", segments, at + 1))
                        yield return found;
                    index++;
                }
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                    foreach (var found in Walk(property.Value, Append(path, property.Name), segments, at + 1))
                        yield return found;
            }
            yield break;
        }

        if (segment.Index is { } wanted)
        {
            if (element.ValueKind == JsonValueKind.Array && wanted < element.GetArrayLength())
                foreach (var found in Walk(element[wanted], $"{path}[{wanted}]", segments, at + 1))
                    yield return found;
            yield break;
        }

        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(segment.Name!, out var next))
            foreach (var found in Walk(next, Append(path, segment.Name!), segments, at + 1))
                yield return found;
    }

    /// <summary>
    /// Why a path missed, told against this body: the nearest key when a property is a near-miss, the
    /// actual kind when <c>length()</c> or an index hit the wrong kind, the array length when an index
    /// overran. Null when the path did not miss on the first branch (a wildcard fan-out, say).
    /// </summary>
    public static string? MissMessage(JsonElement root, IReadOnlyList<Segment> segments)
    {
        var current = root;
        var path = "$";

        foreach (var segment in segments)
        {
            if (segment.Length)
                return current.ValueKind is JsonValueKind.Array or JsonValueKind.Object or JsonValueKind.String
                    ? null
                    : $"{path}.length() — length() of a {KindName(current)}; it counts arrays, objects and strings";

            if (segment.Wildcard)
            {
                if (current.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object))
                    return $"{path}[*] — {path} is a {KindName(current)}, not an array";
                return null;
            }

            if (segment.Index is { } index)
            {
                if (current.ValueKind != JsonValueKind.Array)
                    return $"{path}[{index}] — {path} is a {KindName(current)}, not an array";
                if (index >= current.GetArrayLength())
                    return $"{path}[{index}] is not in this body — the array has {current.GetArrayLength()} elements";
                current = current[index];
                path += $"[{index}]";
                continue;
            }

            var missed = Append(path, segment.Name!);
            if (current.ValueKind != JsonValueKind.Object)
                return $"{missed} is not in this body — {path} is a {KindName(current)}";
            if (!current.TryGetProperty(segment.Name!, out var next))
            {
                var nearest = Nearest(current, segment.Name!);
                return nearest is null
                    ? $"{missed} is not in this body"
                    : $"{missed} is not in this body — nearest: {Append(path, nearest)}";
            }
            current = next;
            path = missed;
        }

        return null;
    }

    /// <summary>
    /// The keys of a JSON body, one line per path, with each leaf's type and a short sample. The cheapest
    /// useful view of a payload: it answers "what is in here" for a fraction of the bytes the payload costs.
    /// </summary>
    public static List<string> Keys(string body, int maxDepth = 3)
    {
        var lines = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(body);
            Walk(document.RootElement, "$", 0);
        }
        catch (JsonException)
        {
            lines.Add("(not JSON — " + QueryWriter.Size(Encoding.UTF8.GetByteCount(body)) + " of text)");
        }

        return lines;

        void Walk(JsonElement element, string path, int depth)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    if (depth >= maxDepth)
                    {
                        lines.Add($"{path}  object ({element.EnumerateObject().Count()} keys)");
                        return;
                    }
                    foreach (var property in element.EnumerateObject())
                        Walk(property.Value, Append(path, property.Name), depth + 1);
                    break;

                case JsonValueKind.Array:
                    var length = element.GetArrayLength();
                    lines.Add($"{path}[]  array ({length})");
                    if (length > 0 && depth < maxDepth)
                        Walk(element[0], path + "[0]", depth + 1);
                    break;

                default:
                    lines.Add($"{path}  {element.ValueKind.ToString().ToLowerInvariant()} = {QueryWriter.OneLine(element.ToString(), 60)}");
                    break;
            }
        }
    }

    /// <summary>The JSON paths whose value contains the needle — what <c>grep --values</c> is for.</summary>
    public static IEnumerable<string> PathsContaining(string body, string needle)
    {
        List<string> found = [];
        try
        {
            using var document = JsonDocument.Parse(body);
            Walk(document.RootElement, "$");
        }
        catch (JsonException)
        {
            return [];
        }

        return found;

        void Walk(JsonElement element, string path)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                        Walk(property.Value, Append(path, property.Name));
                    break;
                case JsonValueKind.Array:
                    var i = 0;
                    foreach (var item in element.EnumerateArray())
                        Walk(item, $"{path}[{i++}]");
                    break;
                default:
                    var text = element.ToString();
                    if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                        found.Add($"{path} = {QueryWriter.OneLine(text, 60)}");
                    break;
            }
        }
    }

    /// <summary>
    /// Renders one more property on a concrete path, bracket-quoting keys the dotted form cannot express —
    /// so an emitted path always parses back to the same place.
    /// </summary>
    private static string Append(string path, string name) =>
        name.Contains('.') || name.Contains('[') || name.Contains(']') || name.Contains('\'')
            ? $"{path}['{name}']"
            : $"{path}.{name}";

    private static string KindName(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => "string",
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Null => "null",
        JsonValueKind.Object => "object",
        JsonValueKind.Array => "array",
        _ => element.ValueKind.ToString().ToLowerInvariant()
    };

    /// <summary>The closest key actually present: same letters first, then a spelling one or two edits away.</summary>
    private static string? Nearest(JsonElement obj, string name)
    {
        string? best = null;
        var bestDistance = int.MaxValue;
        foreach (var property in obj.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Name;
            var distance = Levenshtein(property.Name.ToLowerInvariant(), name.ToLowerInvariant(), 2);
            if (distance >= 0 && distance < bestDistance)
            {
                bestDistance = distance;
                best = property.Name;
            }
        }
        return best;
    }

    /// <summary>Edit distance, capped: returns -1 as soon as the answer would exceed <paramref name="max"/>.</summary>
    private static int Levenshtein(string a, string b, int max)
    {
        if (Math.Abs(a.Length - b.Length) > max)
            return -1;

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var rowBest = current[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                rowBest = Math.Min(rowBest, current[j]);
            }
            if (rowBest > max)
                return -1;
            (previous, current) = (current, previous);
        }

        return previous[b.Length] <= max ? previous[b.Length] : -1;
    }
}

/// <summary>
/// One selected value: a <see cref="JsonElement"/>, or the synthesized number <c>length()</c> produced.
/// Valid only while the <see cref="JsonDocument"/> it came from is alive.
/// </summary>
internal readonly struct PathValue
{
    private readonly JsonElement _element;
    private readonly int _count;
    private readonly bool _isCount;

    public PathValue(JsonElement element) => _element = element;

    private PathValue(int count)
    {
        _count = count;
        _isCount = true;
    }

    public static PathValue OfCount(int count) => new(count);

    public JsonElement Element => _element;
    public bool IsCount => _isCount;
    public JsonValueKind Kind => _isCount ? JsonValueKind.Number : _element.ValueKind;
    public bool IsContainer => !_isCount && _element.ValueKind is JsonValueKind.Object or JsonValueKind.Array;

    /// <summary>The value the way <c>--path</c> prints it alone: scalars bare, containers indented.</summary>
    public string Text() => _isCount
        ? _count.ToString()
        : _element.ValueKind switch
        {
            JsonValueKind.Object or JsonValueKind.Array =>
                JsonSerializer.Serialize(_element, new JsonSerializerOptions { WriteIndented = true }),
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => _element.GetRawText(),
            _ => _element.ToString()
        };

    /// <summary>The value the way a listing row prints it: JSON-shaped (strings quoted), one-lined.</summary>
    public string Row(int max = 60) => _isCount
        ? _count.ToString()
        : QueryWriter.OneLine(_element.GetRawText(), max);

    public bool TryNumber(out double value)
    {
        if (_isCount)
        {
            value = _count;
            return true;
        }
        if (!_isCount && _element.ValueKind == JsonValueKind.Number && _element.TryGetDouble(out value))
            return true;
        value = 0;
        return false;
    }
}
