#:package MongoDB.Bson@3.12.0
// Part A probe: how each MongoDB.Bson version writes BSON values as text.
// run-json-modes.sh copies this file once per Bson version (rewriting only the #:package line above) into a
// directory outside the repo (so the repo's Directory.Build.props is not imported) and runs each copy.
// Everything version-dependent is read by reflection, so the same source compiles against 2.10.0 .. 3.x.
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Bson.IO;

var asm = typeof(BsonDocument).Assembly;
var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
Console.WriteLine($"MongoDB.Bson assembly {asm.GetName().Version} (informational {info}); runtime {Environment.Version}");
Console.WriteLine($"JsonOutputMode members: {string.Join(", ", Enum.GetNames(typeof(JsonOutputMode)).Select(n => $"{n}={(int)Enum.Parse(typeof(JsonOutputMode), n)}"))}");
var hasRelaxed = Enum.TryParse<JsonOutputMode>("RelaxedExtendedJson", out var relaxed);
var hasCanonical = Enum.TryParse<JsonOutputMode>("CanonicalExtendedJson", out var canonical);
Console.WriteLine($"RelaxedExtendedJson exists: {hasRelaxed}; CanonicalExtendedJson exists: {hasCanonical}");
Console.WriteLine($"new JsonWriterSettings().OutputMode = {new JsonWriterSettings().OutputMode}");

// Every public instance property of a fresh JsonWriterSettings, and the static Defaults.
var fresh = new JsonWriterSettings();
foreach (var p in typeof(JsonWriterSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.Name))
{
    object? v;
    try { v = p.GetValue(fresh); } catch (Exception ex) { v = $"<{ex.GetType().Name}>"; }
    var obsolete = p.GetCustomAttribute<ObsoleteAttribute>() is { } o ? $" [Obsolete: {o.Message}]" : "";
    Console.WriteLine($"  new JsonWriterSettings().{p.Name} = {Show(v)}{obsolete}");
}
var defaultsProp = typeof(JsonWriterSettings).GetProperty("Defaults", BindingFlags.Public | BindingFlags.Static);
if (defaultsProp is null)
{
    Console.WriteLine("JsonWriterSettings.Defaults: ABSENT");
}
else
{
    var obsolete = defaultsProp.GetCustomAttribute<ObsoleteAttribute>() is { } o ? $" [Obsolete: {o.Message}]" : " (not obsolete)";
    var d = (JsonWriterSettings)defaultsProp.GetValue(null)!;
    Console.WriteLine($"JsonWriterSettings.Defaults: present, public getter={defaultsProp.GetMethod?.IsPublic == true}, public setter={defaultsProp.SetMethod?.IsPublic == true}{obsolete}");
    Console.WriteLine($"  Defaults.OutputMode = {d.OutputMode}, Defaults.Indent = {d.Indent}, Defaults.IsFrozen = {d.IsFrozen}");
}

// ---------------------------------------------------------------------------------------------------------
// The values. Fixed so that every version's output can be diffed.
var guid = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
var values = new List<(string Name, BsonValue Value)>
{
    ("decimal128_1000_50", new BsonDecimal128(Decimal128.Parse("1000.50"))),
    ("decimal128_from_decimal_1000_50m", new BsonDecimal128(new Decimal128(1000.50m))),
    ("objectId", new BsonObjectId(ObjectId.Parse("65a1b2c3d4e5f60718293a4b"))),
    ("date_2024_01_01", new BsonDateTime(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc))),
    ("date_2024_03_15_millis", new BsonDateTime(new DateTime(2024, 3, 15, 10, 30, 45, 123, DateTimeKind.Utc))),
    ("date_1969_07_20", new BsonDateTime(new DateTime(1969, 7, 20, 20, 17, 40, DateTimeKind.Utc))),
    ("date_1970_01_01", new BsonDateTime(0)),
    ("date_9999_12_31_end", new BsonDateTime(253402300799999)),
    ("date_10000_01_01", new BsonDateTime(253402300800000)),
    ("date_long_min", new BsonDateTime(long.MinValue)),
    ("guid_subtype4_standard", new BsonBinaryData(guid, GuidRepresentation.Standard)),
    ("guid_subtype3_csharplegacy", new BsonBinaryData(guid, GuidRepresentation.CSharpLegacy)),
    ("binary_subtype0_small", new BsonBinaryData(new byte[] { 1, 2, 3, 250 })),
    ("int64_5", new BsonInt64(5)),
    ("int32_5", new BsonInt32(5)),
    ("int64_9007199254740993", new BsonInt64(9007199254740993)),
    ("double_1_5", new BsonDouble(1.5)),
    ("double_1_0", new BsonDouble(1.0)),
    ("double_1e300", new BsonDouble(1e300)),
    ("double_NaN", new BsonDouble(double.NaN)),
    ("double_PosInfinity", new BsonDouble(double.PositiveInfinity)),
    ("double_NegInfinity", new BsonDouble(double.NegativeInfinity)),
    ("double_neg_zero", new BsonDouble(-0.0)),
    ("regex", new BsonRegularExpression("^loan-[0-9]+$", "i")),
    ("timestamp", new BsonTimestamp(1704067200, 1)),
    ("javascript", new BsonJavaScript("function() { return this.amount > 1000; }")),
    ("javascript_with_scope", new BsonJavaScriptWithScope("function() { return x; }", new BsonDocument("x", 1))),
    ("minKey", BsonMinKey.Value),
    ("maxKey", BsonMaxKey.Value),
    ("symbol", BsonSymbolTable.Lookup("loanSymbol")),
    ("null", BsonNull.Value),
    ("undefined", BsonUndefined.Value),
    ("boolean", BsonBoolean.True),
    ("string_escapes", new BsonString("Caf\u00e9 \"quoted\"\nline2\t\u2713 </script> \u2028")),
    ("nested_array", new BsonArray { 1, "two", new BsonDocument("three", 3), new BsonArray { 4, new BsonDecimal128(Decimal128.Parse("5.5")) } }),
    ("nested_document", new BsonDocument { { "street", "1 Main St" }, { "geo", new BsonDocument { { "lat", 51.5 }, { "lng", -0.12 } } } }),
};

JsonWriterSettings Settings(JsonOutputMode? mode)
{
    // Exactly MongoDbResponseSummary.ExtractDetailed's settings, plus an explicit OutputMode when one is given.
    var s = new JsonWriterSettings { Indent = true, IndentChars = "  ", NewLineChars = "\n" };
    if (mode is { } m) s.OutputMode = m;
    return s;
}
JsonWriterSettings Compact(JsonOutputMode? mode)
{
    var s = new JsonWriterSettings();
    if (mode is { } m) s.OutputMode = m;
    return s;
}

var modes = new List<(string Label, Func<BsonDocument, string> Write)>
{
    ("ToString()", d => d.ToString()),
    ("ToJson(new JsonWriterSettings { Indent = true, IndentChars = \"  \", NewLineChars = \"\\n\" })  [Kronikol today]", d => d.ToJson(Settings(null))),
};
if (hasRelaxed) modes.Add(("ToJson(OutputMode = RelaxedExtendedJson, indented)", d => d.ToJson(Settings(relaxed))));
if (hasCanonical) modes.Add(("ToJson(OutputMode = CanonicalExtendedJson, indented)", d => d.ToJson(Settings(canonical))));

// 1. Per value, one line per mode (compact: no indentation), each checked with System.Text.Json.
Console.WriteLine();
Console.WriteLine("=== Per value (compact, no Indent). 'json' = JsonDocument.Parse accepted the text.");
var compactModes = new List<(string Label, Func<BsonDocument, string> Write)>
{
    ("ToString()      ", d => d.ToString()),
    ("default mode    ", d => d.ToJson(Compact(null))),
};
foreach (var name in Enum.GetNames(typeof(JsonOutputMode)))
{
    var m = (JsonOutputMode)Enum.Parse(typeof(JsonOutputMode), name);
    compactModes.Add(($"{name,-16}", d => d.ToJson(Compact(m))));
}
foreach (var (name, value) in values)
{
    Console.WriteLine($"-- {name} ({value.BsonType}{(value is BsonBinaryData b ? $", subtype {b.SubType}" : "")}); BsonValue.ToString() of the value itself: {Show(TryRun(() => value.ToString() ?? "null"))}");
    var doc = new BsonDocument("v", value);
    foreach (var (label, write) in compactModes)
    {
        var text = TryRun(() => write(doc));
        Console.WriteLine($"   {label} {Validity(text)} {text}");
    }
}

// 2. The whole document in the four modes the plan names, indented.
var whole = new BsonDocument(values.Select(v => new BsonElement(v.Name, v.Value)));
Console.WriteLine();
Console.WriteLine("=== The whole document");
foreach (var (label, write) in modes)
{
    var text = TryRun(() => write(whole));
    Console.WriteLine($"--- {label}: {Validity(text)}");
    Console.WriteLine(text);
}

// 3. A 300 KB binary: sizes and the head of each output (the outputs themselves are 400 KB).
var blob = new byte[300 * 1024];
new Random(168).NextBytes(blob);
var blobDoc = new BsonDocument { { "filename", "statement.pdf" }, { "data", new BsonBinaryData(blob) } };
Console.WriteLine();
Console.WriteLine($"=== 300 KB binary ({blob.Length} bytes, BSON document {blobDoc.ToBson().Length} bytes)");
foreach (var (label, write) in modes)
{
    var sw = Stopwatch.StartNew();
    var text = TryRun(() => write(blobDoc));
    sw.Stop();
    Console.WriteLine($"--- {label}: {text.Length} chars, {Validity(text)}, {sw.Elapsed.TotalMilliseconds:F1} ms; head: {text[..Math.Min(110, text.Length)].Replace("\n", "\\n")}");
}

// 4. Does the static JsonWriterSettings.Defaults reach ToString(), ToJson() and new JsonWriterSettings()?
Console.WriteLine();
Console.WriteLine("=== Setting JsonWriterSettings.Defaults (global, last because it mutates process state)");
var probe = new BsonDocument { { "amount", new BsonDecimal128(Decimal128.Parse("1000.50")) }, { "at", new BsonDateTime(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)) } };
if (defaultsProp?.SetMethod is { IsPublic: true })
{
    var original = (JsonWriterSettings)defaultsProp.GetValue(null)!;
    var other = new JsonWriterSettings();
    var target = other.OutputMode == JsonOutputMode.Shell && hasRelaxed ? relaxed : JsonOutputMode.Shell;
    other.OutputMode = target;
    Console.WriteLine($"before: ToString() = {probe}");
    Console.WriteLine($"before: ToJson() = {probe.ToJson()}");
    defaultsProp.SetValue(null, other);
    Console.WriteLine($"set Defaults.OutputMode = {target}");
    Console.WriteLine($"after: new JsonWriterSettings().OutputMode = {new JsonWriterSettings().OutputMode}");
    Console.WriteLine($"after: ToString() = {TryRun(() => probe.ToString())}");
    Console.WriteLine($"after: ToJson() = {TryRun(() => probe.ToJson())}");
    Console.WriteLine($"after: ToJson(new JsonWriterSettings {{ Indent = true }}) = {TryRun(() => probe.ToJson(new JsonWriterSettings { Indent = true })).Replace("\n", "\\n").Replace("\r", "\\r")}");
    defaultsProp.SetValue(null, original);
}
else
{
    Console.WriteLine("Defaults has no public setter: nothing to set.");
}

// 5. 2.x only: the writer re-encodes a UUID binary with JsonWriterSettings.GuidRepresentation (CSharpLegacy by
// default in GuidRepresentationMode.V2). What it writes for subtype 4 and subtype 3 under each setting.
Console.WriteLine();
Console.WriteLine("=== UUID binaries under the 2.x Guid settings (global, last)");
var guidDoc = new BsonDocument
{
    { "std4", new BsonBinaryData(guid, GuidRepresentation.Standard) },
    { "legacy3", new BsonBinaryData(guid, GuidRepresentation.CSharpLegacy) }
};
var writerGuidProp = typeof(JsonWriterSettings).GetProperty("GuidRepresentation", BindingFlags.Public | BindingFlags.Instance);
var modeProp = typeof(BsonDefaults).GetProperty("GuidRepresentationMode", BindingFlags.Public | BindingFlags.Static);
var defaultsGuidProp = typeof(BsonDefaults).GetProperty("GuidRepresentation", BindingFlags.Public | BindingFlags.Static);
Console.WriteLine($"JsonWriterSettings.GuidRepresentation: {(writerGuidProp is null ? "ABSENT" : "present")}; BsonDefaults.GuidRepresentationMode: {(modeProp is null ? "ABSENT" : TryRun(() => modeProp.GetValue(null)!.ToString()!))}; BsonDefaults.GuidRepresentation: {(defaultsGuidProp is null ? "ABSENT" : TryRun(() => defaultsGuidProp.GetValue(null)!.ToString()!))}");
var jsonMode = hasRelaxed ? relaxed : (JsonOutputMode)Enum.Parse(typeof(JsonOutputMode), "Strict");
void WriteGuids(string when)
{
    Console.WriteLine($"-- {when}");
    Console.WriteLine($"   ToString()                      {guidDoc}");
    Console.WriteLine($"   {jsonMode,-31} {TryRun(() => guidDoc.ToJson(new JsonWriterSettings { OutputMode = jsonMode }))}");
    if (writerGuidProp is not null)
    {
        foreach (var rep in new[] { GuidRepresentation.Unspecified, GuidRepresentation.Standard })
        {
            var s = new JsonWriterSettings { OutputMode = jsonMode };
            var set = TryRun(() => { writerGuidProp.SetValue(s, rep); return "ok"; });
            Console.WriteLine($"   {jsonMode} + writer GuidRepresentation={rep} ({set}): {TryRun(() => guidDoc.ToJson(s))}");
            var shell = new JsonWriterSettings { OutputMode = JsonOutputMode.Shell };
            TryRun(() => { writerGuidProp.SetValue(shell, rep); return "ok"; });
            Console.WriteLine($"   Shell + writer GuidRepresentation={rep}: {TryRun(() => guidDoc.ToJson(shell))}");
        }
    }
}
WriteGuids("as the process starts");
if (modeProp is not null)
{
    var modeType = modeProp.PropertyType;
    var setV3 = TryRun(() => { modeProp.SetValue(null, Enum.Parse(modeType, "V3")); return "ok"; });
    Console.WriteLine($"set BsonDefaults.GuidRepresentationMode = V3: {setV3}; new JsonWriterSettings().GuidRepresentation now {(writerGuidProp is null ? "-" : TryRun(() => writerGuidProp.GetValue(new JsonWriterSettings())!.ToString()!))}");
    WriteGuids("after BsonDefaults.GuidRepresentationMode = V3");
}

static string TryRun(Func<string> f)
{
    try { return f(); }
    catch (Exception ex) { var e = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex; return $"<THREW {e.GetType().Name}: {e.Message}>"; }
}

static string Validity(string text)
{
    if (text.StartsWith("<THREW")) return "[threw]";
    try { using var _ = JsonDocument.Parse(text); return "[json]"; }
    catch (JsonException ex)
    {
        var first = ex.Message.Split(". ")[0];
        return $"[NOT JSON: {(first.Length > 90 ? first[..90] + "..." : first)}]";
    }
}

static string Show(object? v) => v switch
{
    null => "null",
    string s => "\"" + s.Replace("\r", "\\r").Replace("\n", "\\n") + "\"",
    _ => v.ToString() ?? "null"
};
