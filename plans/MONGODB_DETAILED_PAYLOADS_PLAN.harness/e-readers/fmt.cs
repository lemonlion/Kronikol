// Verbatim copies of PlantUmlCreator.TryFormatAsJson / WriteElementWithoutNulls / IndentedWriterOptions /
// TruncationMarkerRegex (src/Kronikol/PlantUml/PlantUmlCreator.cs:1573-1632, 1817-1842) to observe their output.
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

var IndentedWriterOptions = new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

string? TryFormatAsJson(string? content)
{
    if (content is null || (!content.StartsWith('{') && !content.StartsWith('[')))
        return null;
    try
    {
        using var doc = JsonDocument.Parse(content);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, IndentedWriterOptions))
        {
            WriteElementWithoutNulls(writer, doc.RootElement);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    catch (JsonException) { return null; }
}

static void WriteElementWithoutNulls(Utf8JsonWriter writer, JsonElement element)
{
    switch (element.ValueKind)
    {
        case JsonValueKind.Object:
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Null)
                    continue;
                writer.WritePropertyName(property.Name);
                WriteElementWithoutNulls(writer, property.Value);
            }
            writer.WriteEndObject();
            break;
        case JsonValueKind.Array:
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray())
                WriteElementWithoutNulls(writer, item);
            writer.WriteEndArray();
            break;
        default:
            element.WriteTo(writer);
            break;
    }
}

var rx = new Regex(@"(?:\r?\n\r?\n…truncated \(\d+ chars total\)|\s…\[bulk string truncated: [^\]]*\]|\r?\n\.\.\. \(\d+ more(?: (?:rows|documents) not shown)?\))\s*$");

Console.WriteLine("=== TryFormatAsJson ===");
string[] inputs =
[
    "{\"big\":9007199254740993,\"d\":1.0,\"s\":\"José <b>\"}",
    "{ \"big\" : 9007199254740993, \"d\" : 1.0, \"s\" : \"José <b>\" }",
    "{\"s\":\"Jos\\u00e9 \\u003cb\\u003e\",\"e\":\"\\ud83d\\ude00 x\",\"amp\":\"a&b'c\",\"ls\":\"a\\u2028b\",\"nul\":null,\"arr\":[null,1]}",
    "{\"status\":\"Ready\",\"n\":1}",
    "{ \"status\" : \"Ready\", \"n\" : 1 }",
    "{ \"filter\" : { \"deletedAt\" : null } }",
    "{ \"updates\" : [{ \"q\" : { \"_id\" : 1 }, \"u\" : { \"$set\" : { \"closedAt\" : null } } }] }",
    "{ \"lastErrorObject\" : { \"n\" : 0, \"updatedExisting\" : false }, \"value\" : null }",
    "{\"amount\":{\"$numberDecimal\":\"1000.50\"},\"_id\":{\"$oid\":\"65a1b2c3d4e5f6a7b8c9d0e1\"},\"at\":{\"$date\":\"2024-01-01T00:00:00Z\"},\"u\":{\"$uuid\":\"0f8fad5b-d9cb-469f-a165-70867728950e\"},\"n\":9223372036854775807,\"e\":1E+2}",
];
foreach (var i in inputs)
{
    Console.WriteLine("IN : " + i);
    Console.WriteLine("OUT:\n" + TryFormatAsJson(i));
    Console.WriteLine();
}
Console.WriteLine("compact == spaced: " + (TryFormatAsJson(inputs[3]) == TryFormatAsJson(inputs[4])));
Console.WriteLine("chars: " + TryFormatAsJson("[\"nbsp\\u00a0x\",\"zwsp\\u200bx\",\"bom\\ufeffx\",\"nel\\u0085x\",\"cjk\\u4e2d\",\"ctl\\u0001\",\"gt>amp&apos'plus+\",\"bs\\\\q\\\"\"]"));

Console.WriteLine("\n=== TruncationMarkerRegex (C#) ===");
foreach (var s in new[] { "[1,2]\n... (3 more)", "[1,2]\n... (3 more documents not shown)", "{\"a\":1}\n... (3 more)", "[1,2]\n... (3 more statements not shown)", "[1,2]\r\n... (3 more)\r\n" })
{
    var m = rx.Match(s);
    Console.WriteLine($"{Q(s)} => success={m.Success} body={Q(m.Success ? s[..m.Index] : s)} marker={Q(m.Success ? m.Value.Trim() : null)}");
}

static string Q(string? v) => v is null ? "null" : "\"" + v.Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
