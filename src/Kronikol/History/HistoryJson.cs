using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Kronikol.History;

/// <summary>
/// The one serialisation of ledger lines and fragments, and its inverse.
///
/// <para><b>Deterministic by construction.</b> Keys are written in a fixed order by code rather than by a
/// dictionary, numbers and dates in the invariant culture, non-ASCII as <c>\uXXXX</c>, one line with no
/// terminator. Two platforms writing the same run therefore produce the same bytes, which is what lets git
/// see one line and <c>merge=union</c> de-duplicate an identical append (§3.1, §5.10).</para>
/// </summary>
public static class HistoryJson
{
    private static readonly JsonWriterOptions LineOptions = new() { Indented = false, SkipValidation = false };

    private static readonly JsonWriterOptions FragmentOptions = new() { Indented = true };

    private const string DateFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    /// <summary>The header line, without a terminator.</summary>
    public static string HeaderLine(string generator)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, LineOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("t", "header");
            writer.WriteNumber("historyFormatVersion", HistoryFormat.Version);
            writer.WriteString("generator", generator);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>A roster line, without a terminator.</summary>
    public static string RosterLine(HistoryRoster roster)
    {
        ArgumentNullException.ThrowIfNull(roster);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, LineOptions))
            WriteRoster(writer, roster);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>A run line, without a terminator.</summary>
    public static string RunLine(HistoryRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, LineOptions))
            WriteRun(writer, run);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>A fragment document, indented.</summary>
    public static string Fragment(HistoryRoster roster, HistoryRun run, string generator)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(run);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, FragmentOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("historyFormatVersion", HistoryFormat.Version);
            writer.WriteString("generator", generator);
            writer.WritePropertyName("roster");
            WriteRoster(writer, roster);
            writer.WritePropertyName("run");
            WriteRun(writer, run);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static void WriteRoster(Utf8JsonWriter writer, HistoryRoster roster)
    {
        writer.WriteStartObject();
        writer.WriteString("t", "roster");
        writer.WriteString("hash", roster.Hash);
        WriteNullable(writer, "suite", roster.Suite);
        WriteStrings(writer, "ids", roster.Ids);
        writer.WritePropertyName("slots");
        writer.WriteStartArray();
        foreach (var slot in roster.Slots) writer.WriteNumberValue(slot);
        writer.WriteEndArray();
        WriteStrings(writer, "names", roster.Names);
        WriteStrings(writer, "features", roster.Features);
        writer.WritePropertyName("sources");
        writer.WriteStartArray();
        foreach (var source in roster.Sources)
        {
            if (source is null) writer.WriteNullValue();
            else writer.WriteStringValue(source);
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteRun(Utf8JsonWriter writer, HistoryRun run)
    {
        writer.WriteStartObject();
        writer.WriteString("t", "run");
        writer.WriteString("id", run.Id);
        WriteNullable(writer, "suite", run.Suite);
        writer.WritePropertyName("partial");
        if (run.Partial is { } partial) writer.WriteBooleanValue(partial);
        else writer.WriteNullValue();
        writer.WriteString("at", run.At.ToUniversalTime().ToString(DateFormat, CultureInfo.InvariantCulture));
        WriteNullable(writer, "branch", run.Branch);
        WriteNullable(writer, "commit", run.Commit);
        WriteNullable(writer, "provider", run.Provider);
        WriteNullable(writer, "url", run.Url);
        writer.WriteNumber("shards", run.Shards);
        writer.WriteString("roster", run.RosterHash);
        writer.WriteString("results", run.Results);
        writer.WriteString("attempts", run.Attempts);

        if (run.Durations is { } durations)
        {
            writer.WritePropertyName("durations");
            writer.WriteStartArray();
            foreach (var duration in durations)
            {
                if (duration is { } ms) writer.WriteNumberValue(ms);
                else writer.WriteNullValue();
            }
            writer.WriteEndArray();
        }

        if (run.Calls is { } calls)
        {
            writer.WritePropertyName("calls");
            writer.WriteStartArray();
            foreach (var count in calls) writer.WriteNumberValue(count);
            writer.WriteEndArray();
        }

        if (run.ShapeSet is { } shapeSet) WriteStrings(writer, "shapeSet", shapeSet);
        if (run.ShapeOrdered is { } shapeOrdered) WriteStrings(writer, "shapeOrdered", shapeOrdered);
        if (run.ShapeVersion is { } shapeVersion) writer.WriteNumber("shapeVersion", shapeVersion);

        if (run.Errors is { } errors)
        {
            writer.WritePropertyName("errors");
            writer.WriteStartArray();
            foreach (var key in errors)
            {
                if (key is null) writer.WriteNullValue();
                else writer.WriteStringValue(key);
            }
            writer.WriteEndArray();

            writer.WritePropertyName("errorText");
            writer.WriteStartObject();
            foreach (var pair in run.ErrorText.OrderBy(p => p.Key, StringComparer.Ordinal))
                writer.WriteString(pair.Key, pair.Value);
            writer.WriteEndObject();
        }

        if (run.Deps is { } deps) WriteStrings(writer, "deps", deps);

        writer.WriteEndObject();
    }

    private static void WriteNullable(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null) writer.WriteNull(name);
        else writer.WriteString(name, value);
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (var value in values) writer.WriteStringValue(value);
        writer.WriteEndArray();
    }

    // ─── Parsing ───────────────────────────────────────────────

    /// <summary>Parses one ledger line. Throws <see cref="FormatException"/> for anything that is not one.</summary>
    public static HistoryLine Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        try
        {
            using var document = JsonDocument.Parse(line);
            return ParseElement(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new FormatException("Not a ledger line: " + exception.Message, exception);
        }
    }

    /// <summary>
    /// The kind, id and suite of a line without parsing the rest of it — the run line's bulk is its
    /// positional arrays, and the reader has to bucket a line by suite before it decides whether the
    /// window wants it parsed (§2.6).
    /// </summary>
    public static (HistoryLineKind? Kind, string? Id, string? Suite, bool SuiteIsNull) Peek(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(line), new JsonReaderOptions { AllowTrailingCommas = false });
            HistoryLineKind? kind = null;
            string? id = null;
            string? suite = null;
            var suiteNull = false;
            var depth = 0;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.StartObject) { depth++; continue; }
                if (reader.TokenType == JsonTokenType.EndObject) { depth--; continue; }
                if (depth != 1 || reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                var name = reader.GetString();
                if (!reader.Read()) break;
                switch (name)
                {
                    case "t":
                        kind = reader.GetString() switch
                        {
                            "header" => HistoryLineKind.Header,
                            "roster" => HistoryLineKind.Roster,
                            "run" => HistoryLineKind.Run,
                            _ => null
                        };
                        if (kind is null) return (null, null, null, false);
                        break;
                    case "id":
                        id = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                        break;
                    case "hash":
                        id = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                        break;
                    case "suite":
                        suiteNull = reader.TokenType == JsonTokenType.Null;
                        suite = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                        // Everything the reader needs to bucket the line has been seen.
                        return (kind, id, suite, suiteNull);
                    default:
                        reader.TrySkip();
                        break;
                }
            }

            return (kind, id, suite, suiteNull);
        }
        catch (JsonException)
        {
            return (null, null, null, false);
        }
    }

    /// <summary>Parses a fragment document.</summary>
    public static HistoryFragment ParseFragment(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("historyFormatVersion", out var versionElement)
                || !root.TryGetProperty("roster", out var rosterElement)
                || !root.TryGetProperty("run", out var runElement))
                throw new FormatException("Not a Kronikol history fragment: it needs historyFormatVersion, roster and run.");

            if (versionElement.ValueKind != JsonValueKind.Number || !versionElement.TryGetInt32(out var version))
                throw new FormatException("The fragment declares a historyFormatVersion that is not a number.");

            var roster = ParseElement(rosterElement).Roster ?? throw new FormatException("The fragment's roster is not a roster.");
            var run = ParseElement(runElement).Run ?? throw new FormatException("The fragment's run is not a run.");
            return new HistoryFragment(version, roster, run);
        }
        catch (JsonException exception)
        {
            throw new FormatException("Not a Kronikol history fragment: " + exception.Message, exception);
        }
    }

    private static HistoryLine ParseElement(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("t", out var kindElement) || kindElement.ValueKind != JsonValueKind.String)
            throw new FormatException("A ledger line is an object with a \"t\" property.");

        return kindElement.GetString() switch
        {
            "header" => new HistoryLine(HistoryLineKind.Header,
                Version: element.TryGetProperty("historyFormatVersion", out var version) && version.ValueKind == JsonValueKind.Number ? version.GetInt32() : null,
                Generator: OptionalString(element, "generator")),
            "roster" => new HistoryLine(HistoryLineKind.Roster, Roster: ParseRoster(element)),
            "run" => new HistoryLine(HistoryLineKind.Run, Run: ParseRun(element)),
            var other => throw new FormatException($"Unknown ledger line kind \"{other}\".")
        };
    }

    private static HistoryRoster ParseRoster(JsonElement element)
    {
        var ids = Strings(element, "ids") ?? throw new FormatException("A roster needs ids.");
        var slots = element.TryGetProperty("slots", out var slotsElement) && slotsElement.ValueKind == JsonValueKind.Array
            ? slotsElement.EnumerateArray().Select(s => s.GetInt32()).ToArray()
            : NumberSlots(ids);
        var names = Strings(element, "names") ?? new string[ids.Length];
        var features = Strings(element, "features") ?? new string[ids.Length];
        var sources = element.TryGetProperty("sources", out var sourcesElement) && sourcesElement.ValueKind == JsonValueKind.Array
            ? sourcesElement.EnumerateArray().Select(s => s.ValueKind == JsonValueKind.String ? s.GetString() : null).ToArray()
            : new string?[ids.Length];

        if (slots.Length != ids.Length || names.Length != ids.Length || features.Length != ids.Length || sources.Length != ids.Length)
            throw new FormatException("A roster's positional arrays do not agree in length.");

        return new HistoryRoster
        {
            Hash = RequiredString(element, "hash"),
            Suite = OptionalString(element, "suite"),
            Ids = ids,
            Slots = slots,
            Names = names.Select(n => n ?? "").ToArray(),
            Features = features.Select(f => f ?? "").ToArray(),
            Sources = sources
        };
    }

    private static int[] NumberSlots(string[] ids)
    {
        var slots = new int[ids.Length];
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < ids.Length; i++)
        {
            seen.TryGetValue(ids[i], out var holders);
            slots[i] = holders;
            seen[ids[i]] = holders + 1;
        }
        return slots;
    }

    private static HistoryRun ParseRun(JsonElement element)
    {
        var errorText = new Dictionary<string, string>(StringComparer.Ordinal);
        if (element.TryGetProperty("errorText", out var errorTextElement) && errorTextElement.ValueKind == JsonValueKind.Object)
            foreach (var pair in errorTextElement.EnumerateObject())
                if (pair.Value.ValueKind == JsonValueKind.String)
                    errorText[pair.Name] = pair.Value.GetString()!;

        var at = RequiredString(element, "at");
        if (!DateTimeOffset.TryParseExact(at, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedAt)
            && !DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsedAt))
            throw new FormatException($"A run's \"at\" is not a timestamp: {at}");

        var shapeSet = Strings(element, "shapeSet");
        return new HistoryRun
        {
            Id = RequiredString(element, "id"),
            Suite = OptionalString(element, "suite"),
            Partial = element.TryGetProperty("partial", out var partial) && partial.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? partial.GetBoolean()
                : null,
            At = parsedAt,
            Branch = OptionalString(element, "branch"),
            Commit = OptionalString(element, "commit"),
            Provider = OptionalString(element, "provider"),
            Url = OptionalString(element, "url"),
            Shards = element.TryGetProperty("shards", out var shards) && shards.ValueKind == JsonValueKind.Number ? shards.GetInt32() : 1,
            RosterHash = RequiredString(element, "roster"),
            Results = RequiredString(element, "results"),
            Attempts = OptionalString(element, "attempts") ?? "",
            Durations = element.TryGetProperty("durations", out var durations) && durations.ValueKind == JsonValueKind.Array
                ? durations.EnumerateArray().Select(d => d.ValueKind == JsonValueKind.Number ? (int?)d.GetInt32() : null).ToArray()
                : null,
            Calls = element.TryGetProperty("calls", out var calls) && calls.ValueKind == JsonValueKind.Array
                ? calls.EnumerateArray().Select(c => c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0).ToArray()
                : null,
            ShapeSet = shapeSet,
            ShapeOrdered = Strings(element, "shapeOrdered"),
            // Lines from before the rule was recorded (3.9.0 to 3.13.0) were made by rule 1.
            ShapeVersion = element.TryGetProperty("shapeVersion", out var shapeVersion) && shapeVersion.ValueKind == JsonValueKind.Number
                ? shapeVersion.GetInt32()
                : shapeSet is null ? null : 1,
            Errors = element.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array
                ? errors.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : null).ToArray()
                : null,
            ErrorText = errorText,
            Deps = Strings(element, "deps")
        };
    }

    private static string RequiredString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new FormatException($"A ledger line is missing \"{name}\".");

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string[]? Strings(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ? v.GetString()! : "").ToArray()
            : null;
}
