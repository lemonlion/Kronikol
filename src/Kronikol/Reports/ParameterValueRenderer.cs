using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace Kronikol.Reports;

internal static class ParameterValueRenderer
{
    private static readonly HashSet<Type> ScalarTypes =
    [
        typeof(string), typeof(bool), typeof(char),
        typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
        typeof(int), typeof(uint), typeof(long), typeof(ulong),
        typeof(float), typeof(double), typeof(decimal),
        typeof(DateTime), typeof(DateTimeOffset), typeof(DateOnly), typeof(TimeOnly), typeof(TimeSpan),
        typeof(Guid), typeof(Uri)
    ];

    internal static bool IsScalarType(Type type) =>
        type.IsPrimitive || type.IsEnum || ScalarTypes.Contains(type) ||
        Nullable.GetUnderlyingType(type) is { } underlying && IsScalarType(underlying);

    internal static bool IsScalarValue(object? value) =>
        value is null || IsScalarType(value.GetType());

    /// <summary>
    /// Gets the readable public instance properties of a type, excluding compiler-generated backing fields.
    /// </summary>
    internal static PropertyInfo[] GetReadableProperties(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToArray();
    }

    /// <summary>
    /// Returns true if the object is a "small complex" suitable for R3 sub-table rendering:
    /// ≤5 scalar properties, no nested complex objects.
    /// </summary>
    internal static bool IsSmallComplexObject(object? value, int maxProperties = 5)
    {
        if (value is null || IsScalarType(value.GetType()))
            return false;

        // Treat IDictionary<string, object?> as a complex object (not as IEnumerable)
        if (value is IDictionary<string, object?> dict)
            return dict.Count > 0 && dict.Count <= maxProperties &&
                   dict.Values.All(IsScalarValue);

        if (value is IEnumerable)
            return false;

        var props = GetReadableProperties(value.GetType());
        if (props.Length == 0 || props.Length > maxProperties)
            return false;

        return props.All(p =>
        {
            try { return IsScalarValue(p.GetValue(value)); }
            catch { return false; }
        });
    }

    /// <summary>
    /// Returns true if the object is complex (not scalar) — suitable for R4 expandable rendering.
    /// Dictionaries and lists of dictionaries are considered complex.
    /// </summary>
    internal static bool IsComplexValue(object? value)
    {
        if (value is null)
            return false;
        if (value is IDictionary<string, object?>)
            return true;
        return !IsScalarType(value.GetType());
    }

    /// <summary>
    /// Tries to flatten a single complex parameter object into column names and per-scenario values.
    /// Returns the property names if all public properties are scalar and count ≤ maxColumns.
    /// Returns null if the object is not suitable for flattening (R2).
    /// </summary>
    internal static string[]? TryGetFlattenableProperties(object? value, int maxColumns)
    {
        if (value is null || IsScalarType(value.GetType()))
            return null;

        // Treat IDictionary<string, object?> as flattenable if all values are scalar
        if (value is IDictionary<string, object?> dict)
        {
            if (dict.Count == 0 || dict.Count > maxColumns)
                return null;
            if (!dict.Values.All(IsScalarValue))
                return null;
            return dict.Keys.ToArray();
        }

        if (value is IEnumerable)
            return null;

        var props = GetReadableProperties(value.GetType());
        if (props.Length == 0 || props.Length > maxColumns)
            return null;

        if (!props.All(p => IsScalarType(p.PropertyType)))
            return null;

        return props.Select(p => p.Name).ToArray();
    }

    /// <summary>
    /// Flattens an object's scalar properties into a string dictionary.
    /// </summary>
    internal static Dictionary<string, string> FlattenToStringValues(object value, string[] propertyNames)
    {
        if (value is IDictionary<string, object?> dict)
        {
            var result = new Dictionary<string, string>();
            foreach (var name in propertyNames)
                result[name] = dict.TryGetValue(name, out var v) ? v?.ToString() ?? "null" : "null";
            return result;
        }

        {
            var result = new Dictionary<string, string>();
            var type = value.GetType();
            foreach (var name in propertyNames)
            {
                var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                result[name] = prop?.GetValue(value)?.ToString() ?? "null";
            }
            return result;
        }
    }

    /// <summary>
    /// Flattens an object's properties into a raw object dictionary.
    /// </summary>
    internal static Dictionary<string, object?> FlattenToRawValues(object value, string[] propertyNames)
    {
        if (value is IDictionary<string, object?> dict)
        {
            var result = new Dictionary<string, object?>();
            foreach (var name in propertyNames)
                result[name] = dict.TryGetValue(name, out var v) ? v : null;
            return result;
        }

        {
            var result = new Dictionary<string, object?>();
            var type = value.GetType();
            foreach (var name in propertyNames)
            {
                var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                result[name] = prop?.GetValue(value);
            }
            return result;
        }
    }

    /// <summary>
    /// Renders a cell value as a sub-table (R3) for small complex objects.
    /// </summary>
    internal static void RenderSubTable(StringBuilder body, object value)
    {
        body.Append("<table class=\"cell-subtable\">");

        if (value is IDictionary<string, object?> dict)
        {
            foreach (var kvp in dict)
            {
                var propValue = kvp.Value?.ToString() ?? "null";
                body.Append($"<tr><th>{System.Net.WebUtility.HtmlEncode(kvp.Key)}</th><td>{System.Net.WebUtility.HtmlEncode(propValue)}</td></tr>");
            }
        }
        else
        {
            var props = GetReadableProperties(value.GetType());
            foreach (var prop in props)
            {
                var propValue = prop.GetValue(value)?.ToString() ?? "null";
                body.Append($"<tr><th>{System.Net.WebUtility.HtmlEncode(prop.Name)}</th><td>{System.Net.WebUtility.HtmlEncode(propValue)}</td></tr>");
            }
        }

        body.Append("</table>");
    }

    /// <summary>
    /// Renders a cell value as an expandable details/summary (R4) for deeply complex objects.
    /// </summary>
    internal static void RenderExpandable(StringBuilder body, object value)
    {
        var preview = GeneratePreview(value);
        if (preview.Length > 300)
            preview = preview[..297] + "...";
        var jsonBody = GenerateHighlightedJson(value, 0);
        if (jsonBody.Length > 10000)
            jsonBody = jsonBody[..10000] + "<span class=\"prop-val\">… (truncated)</span>";

        body.Append("<details class=\"param-expand\">");
        body.Append($"<summary title=\"Show or hide the whole value\">{System.Net.WebUtility.HtmlEncode(preview)}</summary>");
        body.Append($"<div class=\"expand-body\">{jsonBody}</div>");
        body.Append("</details>");
    }

    /// <summary>
    /// Generates a short preview string for the summary line.
    /// Format: "{ Prop1: val1, Prop2: val2, ... }"
    /// </summary>
    internal static string GeneratePreview(object value)
    {
        // Handle dictionaries as object-like previews
        if (value is IDictionary<string, object?> dict)
            return GenerateDictionaryPreview(dict);

        // Handle lists/collections of dictionaries (e.g. from ReqNRoll multi-row tables)
        if (value is IEnumerable<IDictionary<string, object?>> dictEnumerable)
        {
            var count = dictEnumerable.Count();
            return $"{count} {(count == 1 ? "item" : "items")}";
        }

        // Handle arrays/lists/collections (before property-reflection, which would show Length/Rank/etc.)
        if (value is IEnumerable enumerable and not string)
            return GenerateEnumerablePreview(enumerable);

        var type = value.GetType();
        var props = GetReadableProperties(type);

        if (props.Length == 0)
            return type.Name;

        // Show up to 3 properties in the preview
        var previewParts = new List<string>();
        foreach (var prop in props.Take(3))
        {
            try
            {
                var propValue = prop.GetValue(value);
                string formatted;
                if (propValue is ICollection col)
                    formatted = $"{col.Count}";
                else if (propValue is IEnumerable and not string)
                    formatted = "[...]";
                else
                    formatted = FormatPreviewValue(propValue);
                previewParts.Add($"{prop.Name}: {formatted}");
            }
            catch
            {
                previewParts.Add($"{prop.Name}: ?");
            }
        }

        var suffix = props.Length > 3 ? ", ..." : "";
        return $"{{ {string.Join(", ", previewParts)}{suffix} }}";
    }

    private static string GenerateDictionaryPreview(IDictionary<string, object?> dict, bool isNested = false)
    {
        if (dict.Count == 0)
            return "{ }";

        var parts = new List<string>();
        var separator = isNested ? " = " : ": ";

        foreach (var kvp in dict.Take(3))
        {
            string formatted;
            if (kvp.Value is IDictionary<string, object?> nestedDict)
                formatted = GenerateDictionaryPreview(nestedDict, isNested: true);
            else if (kvp.Value is IEnumerable<IDictionary<string, object?>> dictList)
                formatted = $"{dictList.Count()}";
            else
                formatted = FormatNestedPreviewValue(kvp.Value);
            parts.Add($"{kvp.Key}{separator}{formatted}");
        }
        var trail = dict.Count > 3 ? ", ..." : "";
        return $"{{ {string.Join(", ", parts)}{trail} }}";
    }

    private static string GenerateEnumerablePreview(IEnumerable enumerable)
    {
        var items = new List<object?>();
        foreach (var item in enumerable)
            items.Add(item);

        if (items.Count == 0)
            return "[]";

        // If all items are scalar, inline them: ["A", "B", "C"]
        var allScalar = items.All(IsScalarValue);
        if (!allScalar)
            return $"{items.Count} {(items.Count == 1 ? "item" : "items")}";

        const int maxInline = 10;
        var parts = new List<string>();
        foreach (var item in items.Take(maxInline))
            parts.Add(FormatPreviewValue(item));

        var suffix = items.Count > maxInline ? ", ..." : "";
        return $"[{string.Join(", ", parts)}{suffix}]";
    }

    private static string FormatPreviewValue(object? value)
    {
        if (value is null) return "null";
        if (value is string s) return $"\"{(s.Length > 30 ? s[..27] + "..." : s)}\"";
        if (value is bool b) return b ? "true" : "false";
        if (IsScalarType(value.GetType()))
            return value.ToString() ?? "null";

        // For complex nested objects (classes without meaningful ToString), generate a recursive preview
        var type = value.GetType();
        var toString = value.ToString();
        // If ToString() returns just the type name (default behavior for classes), recurse
        if (toString == type.FullName || toString == type.Name || toString == type.ToString())
        {
            var props = GetReadableProperties(type);
            if (props.Length > 0)
                return GenerateNestedPreview(value, type, props);
        }
        // Strip type name prefix from record-style ToString (e.g. "TypeName { Prop = Val, ... }")
        var recordPrefix = type.Name + " { ";
        if (toString != null && toString.StartsWith(recordPrefix) && toString.EndsWith(" }"))
            return toString[type.Name.Length..];
        return toString ?? "null";
    }

    private static string GenerateNestedPreview(object value, Type type, PropertyInfo[] props)
    {
        var parts = new List<string>();
        foreach (var prop in props.Take(3))
        {
            try
            {
                var propValue = prop.GetValue(value);
                string formatted;
                if (propValue is ICollection col)
                    formatted = $"{col.Count}";
                else if (propValue is IEnumerable and not string)
                    formatted = "[...]";
                else
                    formatted = FormatNestedPreviewValue(propValue);
                parts.Add($"{prop.Name} = {formatted}");
            }
            catch
            {
                parts.Add($"{prop.Name} = ?");
            }
        }
        var suffix = props.Length > 3 ? ", ..." : "";
        return $"{{ {string.Join(", ", parts)}{suffix} }}";
    }

    /// <summary>
    /// Formats a value for nested preview display (matching record ToString() style — no string quoting).
    /// </summary>
    private static string FormatNestedPreviewValue(object? value)
    {
        if (value is null) return "null";
        if (value is string s) return s.Length > 30 ? s[..27] + "..." : s;
        if (value is bool b) return b ? "True" : "False";
        if (IsScalarType(value.GetType()))
            return value.ToString() ?? "null";

        var type = value.GetType();
        var toString = value.ToString();
        if (toString == type.FullName || toString == type.Name || toString == type.ToString())
        {
            var props = GetReadableProperties(type);
            if (props.Length > 0)
                return GenerateNestedPreview(value, type, props);
        }
        // Strip type name prefix from record-style ToString (e.g. "TypeName { Prop = Val, ... }")
        var recordPrefix = type.Name + " { ";
        if (toString != null && toString.StartsWith(recordPrefix) && toString.EndsWith(" }"))
            return toString[type.Name.Length..];
        return toString ?? "null";
    }

    /// <summary>
    /// Generates JSON-like representation with HTML span highlights for prop-key and prop-val.
    /// </summary>
    internal static string GenerateHighlightedJson(object? value, int indent)
    {
        if (value is null)
            return "<span class=\"prop-val\">null</span>";

        if (IsScalarType(value.GetType()))
            return FormatHighlightedScalar(value);

        // Handle dictionaries as objects (before the IEnumerable check)
        if (value is IDictionary<string, object?> dict)
            return GenerateHighlightedDictionary(dict, indent);

        if (value is IEnumerable enumerable and not string)
            return GenerateHighlightedArray(enumerable, indent);

        return GenerateHighlightedObject(value, indent);
    }

    private static string FormatHighlightedScalar(object value)
    {
        if (value is string s)
            return $"<span class=\"prop-val\">\"{System.Net.WebUtility.HtmlEncode(s)}\"</span>";
        if (value is bool b)
            return $"<span class=\"prop-val\">{(b ? "true" : "false")}</span>";
        if (value is DateTime dt)
            return $"<span class=\"prop-val\">\"{dt:O}\"</span>";
        if (value is DateTimeOffset dto)
            return $"<span class=\"prop-val\">\"{dto:O}\"</span>";
        if (value is DateOnly d)
            return $"<span class=\"prop-val\">\"{d:O}\"</span>";
        if (value is TimeOnly t)
            return $"<span class=\"prop-val\">\"{t:O}\"</span>";
        if (value is TimeSpan ts)
            return $"<span class=\"prop-val\">\"{ts}\"</span>";
        if (value is Guid g)
            return $"<span class=\"prop-val\">\"{g}\"</span>";
        return $"<span class=\"prop-val\">{System.Net.WebUtility.HtmlEncode(value.ToString() ?? "null")}</span>";
    }

    private static string GenerateHighlightedArray(IEnumerable enumerable, int indent)
    {
        var sb = new StringBuilder();
        var indentStr = new string(' ', indent * 2);
        var innerIndent = new string(' ', (indent + 1) * 2);

        sb.Append('[');
        var items = new List<string>();
        foreach (var item in enumerable)
            items.Add(GenerateHighlightedJson(item, indent + 1));

        if (items.Count == 0)
        {
            sb.Append(']');
        }
        else
        {
            sb.Append('\n');
            for (var i = 0; i < items.Count; i++)
            {
                sb.Append(innerIndent);
                sb.Append(items[i]);
                if (i < items.Count - 1) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append(indentStr);
            sb.Append(']');
        }

        return sb.ToString();
    }

    private static string GenerateHighlightedObject(object value, int indent)
    {
        var sb = new StringBuilder();
        var indentStr = new string(' ', indent * 2);
        var innerIndent = new string(' ', (indent + 1) * 2);
        var props = GetReadableProperties(value.GetType());

        sb.Append('{');
        if (props.Length == 0)
        {
            sb.Append('}');
        }
        else
        {
            sb.Append('\n');
            for (var i = 0; i < props.Length; i++)
            {
                var prop = props[i];
                object? propValue;
                try { propValue = prop.GetValue(value); }
                catch { propValue = null; }

                sb.Append(innerIndent);
                sb.Append($"<span class=\"prop-key\">\"{System.Net.WebUtility.HtmlEncode(prop.Name)}\"</span>: ");
                sb.Append(GenerateHighlightedJson(propValue, indent + 1));
                if (i < props.Length - 1) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append(indentStr);
            sb.Append('}');
        }

        return sb.ToString();
    }

    private static string GenerateHighlightedDictionary(IDictionary<string, object?> dict, int indent)
    {
        var sb = new StringBuilder();
        var indentStr = new string(' ', indent * 2);
        var innerIndent = new string(' ', (indent + 1) * 2);

        sb.Append('{');
        if (dict.Count == 0)
        {
            sb.Append('}');
        }
        else
        {
            sb.Append('\n');
            var entries = dict.ToArray();
            for (var i = 0; i < entries.Length; i++)
            {
                sb.Append(innerIndent);
                sb.Append($"<span class=\"prop-key\">\"{System.Net.WebUtility.HtmlEncode(entries[i].Key)}\"</span>: ");
                sb.Append(GenerateHighlightedJson(entries[i].Value, indent + 1));
                if (i < entries.Length - 1) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append(indentStr);
            sb.Append('}');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Attempts string-based R3/R4 rendering for a cell value when no raw object is available.
    /// Returns true and appends HTML if the string matches a record ToString() pattern.
    /// R3 (sub-table) is used for ≤5 properties, R4 (expandable) for more.
    /// </summary>
    internal static bool TryRenderFromParsedString(StringBuilder body, string? value, int maxSubTableProperties = 5)
    {
        try
        {
            var parsed = ParameterParser.TryParseRecordToString(value);
            if (parsed is null)
                return false;

            if (parsed.Count <= maxSubTableProperties)
            {
                RenderSubTableFromParsed(body, parsed);
                return true;
            }

            RenderExpandableFromParsed(body, value!, parsed);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Kronikol] Warning: String-based R3/R4 rendering failed for value '{value}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Renders a sub-table (R3) from parsed string key-value pairs.
    /// Recursively renders nested record values and cleans up collection type names.
    /// </summary>
    internal static void RenderSubTableFromParsed(StringBuilder body, Dictionary<string, string> parsed)
    {
        body.Append("<table class=\"cell-subtable\">");
        foreach (var kvp in parsed)
        {
            body.Append($"<tr><th>{System.Net.WebUtility.HtmlEncode(kvp.Key)}</th><td>");
            RenderParsedValue(body, kvp.Value);
            body.Append("</td></tr>");
        }
        body.Append("</table>");
    }

    /// <summary>
    /// Renders a single parsed value intelligently: nested records become sub-tables,
    /// collection type names become readable labels, and scalars are HTML-encoded.
    /// </summary>
    internal static void RenderParsedValue(StringBuilder body, string value)
    {
        // Try to parse as a nested record (e.g. "IngredientSet { Flour = Plain, ... }")
        var nestedParsed = ParameterParser.TryParseRecordToString(value);
        if (nestedParsed is { Count: > 0 })
        {
            RenderSubTableFromParsed(body, nestedParsed);
            return;
        }

        // Clean up .NET collection type names
        var cleaned = TryCleanCollectionTypeName(value);
        if (cleaned is not null)
        {
            body.Append($"<span class=\"mono\">{System.Net.WebUtility.HtmlEncode(cleaned)}</span>");
            return;
        }

        // Scalar: plain HTML-encoded text
        body.Append(System.Net.WebUtility.HtmlEncode(value));
    }

    /// <summary>
    /// Detects and cleans .NET collection type names like
    /// "System.Collections.Generic.List`1[Namespace.Type]" into "List&lt;Type&gt;".
    /// Returns null if the value is not a collection type name.
    /// </summary>
    internal static string? TryCleanCollectionTypeName(string value)
    {
        if (!value.StartsWith("System.Collections.", StringComparison.Ordinal) &&
            !value.StartsWith("System.Linq.", StringComparison.Ordinal))
            return null;

        // Extract the simple type name and generic argument
        // Pattern: "System.Collections.Generic.List`1[Namespace.TypeName]"
        var backtickIdx = value.IndexOf('`');
        if (backtickIdx < 0)
        {
            // Non-generic collection (e.g. "System.Collections.ArrayList")
            var lastDot = value.LastIndexOf('.');
            return lastDot >= 0 ? value[(lastDot + 1)..] : value;
        }

        // Get the simple collection name (e.g. "List" from "System.Collections.Generic.List`1[...]")
        var nameStart = value.LastIndexOf('.', backtickIdx - 1) + 1;
        var collectionName = value[nameStart..backtickIdx];

        // Extract the generic argument type name
        var bracketStart = value.IndexOf('[', backtickIdx);
        var bracketEnd = value.LastIndexOf(']');
        if (bracketStart < 0 || bracketEnd <= bracketStart)
            return collectionName;

        var fullArgType = value[(bracketStart + 1)..bracketEnd];
        // Get just the simple type name (after last dot, strip nested class + prefix)
        var argLastDot = fullArgType.LastIndexOf('.');
        var simpleArgType = argLastDot >= 0 ? fullArgType[(argLastDot + 1)..] : fullArgType;
        // Strip nested class prefix (e.g. "OuterClass+InnerType" → "InnerType")
        var plusIdx = simpleArgType.LastIndexOf('+');
        if (plusIdx >= 0)
            simpleArgType = simpleArgType[(plusIdx + 1)..];

        return $"List<{simpleArgType}>";
    }

    /// <summary>
    /// Renders an expandable details/summary (R4) from parsed string key-value pairs.
    /// </summary>
    internal static void RenderExpandableFromParsed(StringBuilder body, string originalValue, Dictionary<string, string> parsed)
    {
        var preview = GeneratePreviewFromParsed(originalValue, parsed);
        if (preview.Length > 300)
            preview = preview[..297] + "...";
        var jsonBody = GenerateHighlightedJsonFromParsed(parsed);
        if (jsonBody.Length > 10000)
            jsonBody = jsonBody[..10000] + "<span class=\"prop-val\">… (truncated)</span>";

        body.Append("<details class=\"param-expand\">");
        body.Append($"<summary title=\"Show or hide the whole value\">{System.Net.WebUtility.HtmlEncode(preview)}</summary>");
        body.Append($"<div class=\"expand-body\">{jsonBody}</div>");
        body.Append("</details>");
    }

    /// <summary>
    /// Generates a short preview for the parsed record (shows up to 3 properties).
    /// Cleans up nested record values and collection type names in the preview.
    /// </summary>
    internal static string GeneratePreviewFromParsed(string originalValue, Dictionary<string, string> parsed)
    {
        // Extract the type name from original string (everything before " { ")
        var braceIdx = originalValue.IndexOf(" { ", StringComparison.Ordinal);
        var typeName = braceIdx >= 0 ? originalValue[..braceIdx] : "Object";

        var previewParts = parsed.Take(3).Select(kvp =>
        {
            var val = TryCleanCollectionTypeName(kvp.Value) ?? kvp.Value;
            // Shorten nested records in preview to just "TypeName {...}"
            var nestedBrace = val.IndexOf(" { ", StringComparison.Ordinal);
            if (nestedBrace > 0)
                val = val[..nestedBrace] + " {...}";
            return $"{kvp.Key}: {val}";
        });
        var suffix = parsed.Count > 3 ? ", ..." : "";
        return $"{typeName} {{ {string.Join(", ", previewParts)}{suffix} }}";
    }

    /// <summary>
    /// Generates JSON-like highlighted HTML from parsed string key-value pairs.
    /// Recursively renders nested record values.
    /// </summary>
    internal static string GenerateHighlightedJsonFromParsed(Dictionary<string, string> parsed)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        var entries = parsed.ToArray();
        for (var i = 0; i < entries.Length; i++)
        {
            var (key, value) = entries[i];
            sb.Append($"  <span class=\"prop-key\">\"{System.Net.WebUtility.HtmlEncode(key)}\"</span>: ");

            // Try nested record rendering
            var nestedParsed = ParameterParser.TryParseRecordToString(value);
            if (nestedParsed is { Count: > 0 })
            {
                sb.Append(GenerateHighlightedJsonFromParsed(nestedParsed));
            }
            else
            {
                var cleaned = TryCleanCollectionTypeName(value);
                sb.Append($"<span class=\"prop-val\">{System.Net.WebUtility.HtmlEncode(cleaned ?? value)}</span>");
            }
            if (i < entries.Length - 1) sb.Append(',');
            sb.Append('\n');
        }
        sb.Append('}');
        return sb.ToString();
    }
}
