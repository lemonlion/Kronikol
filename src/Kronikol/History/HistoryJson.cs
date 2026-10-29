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

    // A fragment's raw calls are SQL first lines and paths as written: the default encoder wrote every quote, angle bracket
    // and plus sign as six bytes, which grew the largest fragments measured by a third again. The file is never embedded in
    // a page, and this encoder is as deterministic as the other.
    private static readonly JsonWriterOptions RawCallOptions = new() { Indented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

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

    /// <summary>A shapes line, without a terminator.</summary>
    public static string ShapesLine(HistoryShapes shapes)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, LineOptions))
            WriteShapes(writer, shapes);
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
    public static string Fragment(HistoryRoster roster, HistoryRun run, string generator, HistoryShapes? shapes = null) =>
        Fragment(roster, run, generator, shapes, rawCalls: null);

    /// <summary>A fragment document, indented, with the run's calls before any rule (<see cref="HistoryRawCalls"/>).</summary>
    internal static string Fragment(HistoryRoster roster, HistoryRun run, string generator, HistoryShapes? shapes, HistoryRawCalls? rawCalls)
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
            if (shapes is not null)
            {
                writer.WritePropertyName("shapes");
                WriteShapes(writer, shapes);
            }
            // A bridge's calls go beside the run as the run's own do: record writes each as a shapes line.
            var bridgeShapes = BridgeShapes(run).ToArray();
            if (bridgeShapes.Length > 0)
            {
                writer.WritePropertyName("bridgeShapes");
                writer.WriteStartArray();
                foreach (var bridgeShapesLine in bridgeShapes)
                    WriteShapes(writer, bridgeShapesLine);
                writer.WriteEndArray();
            }
            // The run's calls before any rule, for history record to fingerprint under the window's earlier patterns
            // (#192 R1, Q18): a member an older reader passes over, since it reads a fragment's members by name. One call,
            // and one position's order, per line.
            if (rawCalls is not null)
            {
                writer.WritePropertyName("rawCalls");
                writer.WriteStartObject();
                writer.WritePropertyName("calls");
                writer.WriteStartArray();
                foreach (var call in rawCalls.Calls)
                    writer.WriteRawValue(RawCall(call), skipInputValidation: true);
                writer.WriteEndArray();
                writer.WritePropertyName("order");
                writer.WriteStartArray();
                foreach (var order in rawCalls.Order)
                    writer.WriteRawValue("[" + string.Join(",", order.Select(i => i.ToString(CultureInfo.InvariantCulture))) + "]", skipInputValidation: true);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static string RawCall(HistoryRawCall call)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, RawCallOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("caller", call.Caller);
            writer.WriteString("service", call.Service);
            writer.WriteString("method", call.Method);
            writer.WriteString("path", call.Path);
            if (call.Operation is not null) writer.WriteString("operation", call.Operation);
            if (call.Head is not null) writer.WriteString("head", call.Head);
            if (call.Status is not null) writer.WriteString("status", call.Status);
            if (call.Service6 is not null) writer.WriteString("service6", call.Service6);
            if (call.Method7 is not null) writer.WriteString("method7", call.Method7);
            if (call.Path7 is not null) writer.WriteString("path7", call.Path7);
            if (call.Head7 is not null) writer.WriteString("head7", call.Head7);
            if (call.Absent7) writer.WriteBoolean("absent7", true);
            if (call.Method8 is not null) writer.WriteString("method8", call.Method8);
            if (call.Path8 is not null) writer.WriteString("path8", call.Path8);
            if (call.Absent8) writer.WriteBoolean("absent8", true);
            if (call.Unanswered8) writer.WriteBoolean("unanswered8", true);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// A fragment's raw calls, or null when it carries none or they do not read: the run is recorded without them, as a
    /// fragment from before #192 R1 is, rather than not at all.
    /// </summary>
    private static HistoryRawCalls? ParseRawCalls(JsonElement root, int positions)
    {
        if (!root.TryGetProperty("rawCalls", out var element) || element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("calls", out var callsElement) || callsElement.ValueKind != JsonValueKind.Array
            || !element.TryGetProperty("order", out var orderElement) || orderElement.ValueKind != JsonValueKind.Array
            || orderElement.GetArrayLength() != positions)
            return null;

        var calls = new List<HistoryRawCall>();
        foreach (var call in callsElement.EnumerateArray())
        {
            if (call.ValueKind != JsonValueKind.Object
                || Text(call, "caller") is not { } caller || Text(call, "service") is not { } service
                || Text(call, "method") is not { } method || Text(call, "path") is not { } path)
                return null;
            calls.Add(new HistoryRawCall(caller, service, method, path, Text(call, "operation"), Text(call, "head"), Text(call, "status"))
            {
                Service6 = Text(call, "service6"),
                Method7 = Text(call, "method7"),
                Path7 = Text(call, "path7"),
                Head7 = Text(call, "head7"),
                Absent7 = call.TryGetProperty("absent7", out var absent) && absent.ValueKind == JsonValueKind.True,
                Method8 = Text(call, "method8"),
                Path8 = Text(call, "path8"),
                Absent8 = call.TryGetProperty("absent8", out var absent8) && absent8.ValueKind == JsonValueKind.True,
                Unanswered8 = call.TryGetProperty("unanswered8", out var unanswered8) && unanswered8.ValueKind == JsonValueKind.True
            });
        }

        var order = new List<IReadOnlyList<int>>();
        foreach (var position in orderElement.EnumerateArray())
        {
            if (position.ValueKind != JsonValueKind.Array)
                return null;
            var indices = new List<int>();
            foreach (var index in position.EnumerateArray())
            {
                if (index.ValueKind != JsonValueKind.Number || !index.TryGetInt32(out var at) || at < 0 || at >= calls.Count)
                    return null;
                indices.Add(at);
            }
            order.Add(indices);
        }
        return new HistoryRawCalls(calls, order);

        static string? Text(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    /// <summary>The calls a run's bridges name and still hold in memory, one list per distinct hash, in hash order.</summary>
    internal static IEnumerable<HistoryShapes> BridgeShapes(HistoryRun run) =>
        (run.Bridge?.Values ?? []).Select(b => b.Shapes).OfType<HistoryShapes>()
            .DistinctBy(s => s.Hash, StringComparer.Ordinal).OrderBy(s => s.Hash, StringComparer.Ordinal);

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

    private static void WriteShapes(Utf8JsonWriter writer, HistoryShapes shapes)
    {
        writer.WriteStartObject();
        writer.WriteString("t", "shapes");
        writer.WriteString("hash", shapes.Hash);
        WriteStrings(writer, "calls", shapes.Calls);
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
        if (run.Generator is { Length: > 0 } generator) writer.WriteString("generator", generator);
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
        if (run.ShapeRules is { Length: > 0 } shapeRules) writer.WriteString("shapeRules", shapeRules);
        if (run.ShapeTemplates is { Count: > 0 } templates)
        {
            writer.WritePropertyName("shapeTemplates");
            writer.WriteStartArray();
            foreach (var template in templates)
            {
                writer.WriteStartObject();
                writer.WriteString("pattern", template.Pattern);
                writer.WriteString("placeholder", template.Placeholder);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        if (run.ShapesHash is { } shapesHash) writer.WriteString("shapes", shapesHash);
        if (run.CallSets is { } callSets)
        {
            writer.WritePropertyName("callSets");
            writer.WriteStartArray();
            foreach (var set in callSets)
            {
                writer.WriteStartArray();
                foreach (var index in set) writer.WriteNumberValue(index);
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
        }

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

        if (run.Bridge is { Count: > 0 } bridge)
        {
            writer.WritePropertyName("bridge");
            writer.WriteStartObject();
            foreach (var (rule, entry) in bridge.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(rule);
                WriteBridge(writer, entry);
            }
            writer.WriteEndObject();
        }

        if (run.Aliases is { Count: > 0 } aliases)
        {
            writer.WritePropertyName("aliases");
            writer.WriteStartObject();
            foreach (var pair in aliases.OrderBy(p => p.Key, StringComparer.Ordinal))
                writer.WriteString(pair.Key, pair.Value);
            writer.WriteEndObject();
        }

        if (run.ServiceId is { } serviceId) writer.WriteString("serviceId", serviceId);

        // A newer writer's members, written again by this writer rather than copied: one read from a fragment, which is
        // written indented, spans lines as it was read, and a run line is one line, ASCII-escaped.
        foreach (var (name, json) in run.Unknown ?? [])
        {
            writer.WritePropertyName(name);
            using var member = JsonDocument.Parse(json);
            member.RootElement.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    /// <summary>The run line's members this build writes; any other is a newer writer's and is carried through.</summary>
    private static readonly HashSet<string> RunMembers = new(StringComparer.Ordinal)
    {
        "t", "id", "suite", "partial", "at", "branch", "commit", "provider", "url", "generator", "shards", "roster", "results",
        "attempts", "durations", "calls", "shapeSet", "shapeOrdered", "shapeVersion", "shapeRules", "shapeTemplates", "shapes",
        "callSets", "errors", "errorText", "deps", "bridge", "aliases", "serviceId"
    };

    private static void WriteBridge(Utf8JsonWriter writer, HistoryBridge bridge)
    {
        writer.WriteStartObject();
        if (bridge.ShapeSet is { } set) WriteStrings(writer, "set", set);
        if (bridge.ShapeOrdered is { } ordered) WriteStrings(writer, "ordered", ordered);
        if (bridge.ShapesHash is { } shapesHash) writer.WriteString("shapes", shapesHash);
        if (bridge.CallSets is { } callSets)
        {
            writer.WritePropertyName("callSets");
            writer.WriteStartArray();
            foreach (var callSet in callSets)
            {
                writer.WriteStartArray();
                foreach (var index in callSet) writer.WriteNumberValue(index);
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
        }
        if (bridge.Deps is { Count: > 0 } deps)
        {
            writer.WritePropertyName("deps");
            writer.WriteStartObject();
            foreach (var pair in deps.OrderBy(p => p.Key, StringComparer.Ordinal))
                writer.WriteString(pair.Key, pair.Value);
            writer.WriteEndObject();
        }
        if (bridge.Calls is { } calls)
        {
            writer.WritePropertyName("calls");
            writer.WriteStartArray();
            foreach (var count in calls) writer.WriteNumberValue(count);
            writer.WriteEndArray();
        }
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
        var (kind, id, suite, suiteNull, _) = Peek(line, throughBranch: false);
        return (kind, id, suite, suiteNull);
    }

    /// <summary>
    /// <see cref="Peek(string)"/>, reading on to a run line's branch. A run is read against the runs of one
    /// stream that were appended before it, so the reader indexes every run line by id and by stream, and
    /// both sit ahead of the positional arrays that are the bulk of the line.
    /// </summary>
    internal static (HistoryLineKind? Kind, string? Id, string? Suite, bool SuiteIsNull, string? Branch) PeekRun(string line) =>
        Peek(line, throughBranch: true);

    private static (HistoryLineKind? Kind, string? Id, string? Suite, bool SuiteIsNull, string? Branch) Peek(string line, bool throughBranch)
    {
        ArgumentNullException.ThrowIfNull(line);
        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(line), new JsonReaderOptions { AllowTrailingCommas = false });
            HistoryLineKind? kind = null;
            string? id = null;
            string? suite = null;
            string? branch = null;
            var suiteNull = false;
            var suiteSeen = false;
            var branchSeen = false;
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
                        // A kind this build does not know is a newer writer's line, read past; a "t" that is no string
                        // makes no ledger line (until #192 R1 it threw out of the read).
                        if (reader.TokenType != JsonTokenType.String)
                            return (null, null, null, false, null);
                        kind = reader.GetString() switch
                        {
                            "header" => HistoryLineKind.Header,
                            "roster" => HistoryLineKind.Roster,
                            "run" => HistoryLineKind.Run,
                            "shapes" => HistoryLineKind.Shapes,
                            _ => HistoryLineKind.Other
                        };
                        if (kind == HistoryLineKind.Other)
                        {
                            // Read to its end, as nothing parses it later: torn, it is damage, as a torn line of a known
                            // kind is when it is parsed, and not a newer writer's line to keep for good.
                            while (reader.Read()) { }
                            return (kind, null, null, false, null);
                        }
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
                        suiteSeen = true;
                        // Everything the reader needs to bucket the line has been seen, unless it is a run
                        // being indexed by stream as well and its branch is still ahead.
                        if (!throughBranch || kind != HistoryLineKind.Run || branchSeen)
                            return (kind, id, suite, suiteNull, branch);
                        break;
                    case "branch" when throughBranch:
                        if (reader.TokenType == JsonTokenType.String) branch = reader.GetString();
                        else reader.TrySkip();
                        branchSeen = true;
                        if (suiteSeen)
                            return (kind, id, suite, suiteNull, branch);
                        break;
                    default:
                        reader.TrySkip();
                        break;
                }
            }

            return (kind, id, suite, suiteNull, branch);
        }
        catch (JsonException)
        {
            return (null, null, null, false, null);
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
            var shapes = root.TryGetProperty("shapes", out var shapesElement) && shapesElement.ValueKind == JsonValueKind.Object
                ? ParseElement(shapesElement).Shapes ?? throw new FormatException("The fragment's shapes are not a shapes line.")
                : null;
            if (run.Bridge is { Count: > 0 } && root.TryGetProperty("bridgeShapes", out var bridgeShapesElement) && bridgeShapesElement.ValueKind == JsonValueKind.Array)
            {
                // The bridges' calls, which the run line names by hash, are held on the bridges again as they were when
                // the run made them.
                var byHash = bridgeShapesElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object)
                    .Select(e => ParseElement(e).Shapes).OfType<HistoryShapes>()
                    .GroupBy(s => s.Hash, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                run = run with
                {
                    Bridge = run.Bridge.ToDictionary(p => p.Key,
                        p => p.Value.ShapesHash is { } hash && byHash.TryGetValue(hash, out var held) ? p.Value with { Shapes = held } : p.Value,
                        StringComparer.Ordinal)
                };
            }
            return new HistoryFragment(version, roster, run, shapes)
            {
                Generator = root.TryGetProperty("generator", out var generator) && generator.ValueKind == JsonValueKind.String ? generator.GetString() : null,
                RawCalls = ParseRawCalls(root, roster.Ids.Count)
            };
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
            "shapes" => new HistoryLine(HistoryLineKind.Shapes, Shapes: new HistoryShapes { Hash = RequiredString(element, "hash"), Calls = Strings(element, "calls") ?? [] }),
            // A newer writer's kind: read past, and kept as it is by a rewrite (#192 R1).
            _ => new HistoryLine(HistoryLineKind.Other)
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
            ShapeRules = element.TryGetProperty("shapeRules", out var shapeRules) && shapeRules.ValueKind == JsonValueKind.String ? shapeRules.GetString() : null,
            Errors = element.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array
                ? errors.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : null).ToArray()
                : null,
            ErrorText = errorText,
            Deps = Strings(element, "deps"),
            ShapesHash = OptionalString(element, "shapes"),
            CallSets = CallSetsOf(element, "callSets"),
            Bridge = element.TryGetProperty("bridge", out var bridge) && bridge.ValueKind == JsonValueKind.Object ? ParseBridges(bridge) : null,
            ServiceId = element.TryGetProperty("serviceId", out var serviceId) && serviceId.ValueKind == JsonValueKind.String ? serviceId.GetString() : null,
            Generator = OptionalString(element, "generator"),
            ShapeTemplates = element.TryGetProperty("shapeTemplates", out var templates) && templates.ValueKind == JsonValueKind.Array
                ? templates.EnumerateArray()
                    .Where(t => t.ValueKind == JsonValueKind.Object)
                    .Select(t => new HistoryShapeTemplate(OptionalString(t, "pattern") ?? "", OptionalString(t, "placeholder") ?? ""))
                    .ToArray()
                : null,
            Aliases = element.TryGetProperty("aliases", out var aliases) && aliases.ValueKind == JsonValueKind.Object ? AliasesOf(aliases) : null,
            Unknown = UnknownMembers(element, RunMembers)
        };
    }

    /// <summary>
    /// A run line's aliases, old id to new; null when none. A name given twice is read once, its later value, as a JSON
    /// reader reads one; a member that is no string, or whose name or value is empty, maps nothing.
    /// </summary>
    private static IReadOnlyDictionary<string, string>? AliasesOf(JsonElement aliases)
    {
        Dictionary<string, string>? read = null;
        foreach (var alias in aliases.EnumerateObject())
            if (alias.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(alias.Name) && alias.Value.GetString() is { } to
                && !string.IsNullOrWhiteSpace(to))
                (read ??= new Dictionary<string, string>(StringComparer.Ordinal))[alias.Name] = to;
        return read;
    }

    /// <summary>The members of <paramref name="element"/> outside <paramref name="known"/>, as raw JSON; null when there are none.</summary>
    private static IReadOnlyList<KeyValuePair<string, string>>? UnknownMembers(JsonElement element, HashSet<string> known)
    {
        List<KeyValuePair<string, string>>? unknown = null;
        foreach (var member in element.EnumerateObject())
            if (!known.Contains(member.Name))
                (unknown ??= []).Add(new KeyValuePair<string, string>(member.Name, member.Value.GetRawText()));
        return unknown;
    }

    private static IReadOnlyList<IReadOnlyList<int>>? CallSetsOf(JsonElement element, string name) =>
        element.TryGetProperty(name, out var callSets) && callSets.ValueKind == JsonValueKind.Array
            ? callSets.EnumerateArray().Select(set => (IReadOnlyList<int>)(set.ValueKind == JsonValueKind.Array
                ? set.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Number).Select(i => i.GetInt32()).ToArray()
                : [])).ToArray()
            : null;

    /// <summary>A run line's bridges; an entry that is not an object is skipped, as a line's other members are.</summary>
    private static Dictionary<string, HistoryBridge> ParseBridges(JsonElement element)
    {
        var bridges = new Dictionary<string, HistoryBridge>(StringComparer.Ordinal);
        foreach (var rule in element.EnumerateObject())
        {
            if (rule.Value.ValueKind != JsonValueKind.Object) continue;
            var entry = rule.Value;
            Dictionary<string, string>? deps = null;
            if (entry.TryGetProperty("deps", out var depsElement) && depsElement.ValueKind == JsonValueKind.Object)
            {
                deps = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var pair in depsElement.EnumerateObject())
                    if (pair.Value.ValueKind == JsonValueKind.String)
                        deps[pair.Name] = pair.Value.GetString()!;
            }
            bridges[rule.Name] = new HistoryBridge
            {
                ShapeSet = Strings(entry, "set"),
                ShapeOrdered = Strings(entry, "ordered"),
                ShapesHash = OptionalString(entry, "shapes"),
                CallSets = CallSetsOf(entry, "callSets"),
                Deps = deps is { Count: > 0 } ? deps : null,
                Calls = entry.TryGetProperty("calls", out var callsElement) && callsElement.ValueKind == JsonValueKind.Array ? CountsOf(callsElement) : null
            };
        }
        return bridges;
    }

    /// <summary>
    /// A bridge's call counts, one per position; null when any entry is no whole number, since leaving that one out would
    /// read every count after it at the position before.
    /// </summary>
    private static IReadOnlyList<int>? CountsOf(JsonElement calls)
    {
        var counts = new int[calls.GetArrayLength()];
        var i = 0;
        foreach (var count in calls.EnumerateArray())
            if (count.ValueKind != JsonValueKind.Number || !count.TryGetInt32(out counts[i++]))
                return null;
        return counts;
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
