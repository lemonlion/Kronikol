using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using Kronikol.Extensions.MongoDB;

namespace Kronikol.Tests.MongoDB;

/// <summary>
/// The writer every MongoDB note goes through (plans/MONGODB_DETAILED_PAYLOADS_PLAN.md, section 4.1). The V2 test
/// project links this file, so each fact runs on MongoDB.Bson 2.x and on 3.x. One fact sets
/// <c>JsonWriterSettings.Defaults</c>, which every other capture's <c>ToString()</c> reads, so the class runs alone.
/// </summary>
[Collection(ProcessWideStatics.Name)]
public class MongoJsonTests
{
    private static bool OnBson3 => typeof(BsonDocument).Assembly.GetName().Version!.Major >= 3;

    /// <summary>A lowercase <c>\u</c> escape, built so no source text holds one.</summary>
    private static string Esc(int code) => "\\" + "u" + code.ToString("x4", CultureInfo.InvariantCulture);

    /// <summary>
    /// A subtype-4 GUID as a reply carries it, read from BSON: one built in memory has no GUID representation, which
    /// MongoDB.Bson 3.x's writer refuses, and no reply holds one.
    /// </summary>
    private static BsonBinaryData Guid4() =>
        BsonDocument.Parse("{ \"g\" : { \"$binary\" : { \"base64\" : \"D4+tW9nLRp+hZXCGdyiVDg==\", \"subType\" : \"04\" } } }")["g"].AsBsonBinaryData;

    private static readonly Dictionary<string, (BsonValue Value, string Expected)> EveryType = new()
    {
        ["Double"] = (new BsonDouble(1.5), "1.5"),
        ["String"] = (new BsonString("Ready"), "\"Ready\""),
        ["Document"] = (new BsonDocument { { "status", "Ready" }, { "n", 1 } }, "{ \"status\" : \"Ready\", \"n\" : 1 }"),
        ["Array"] = (new BsonArray { 1, "a", new BsonDocument("x", 1) }, "[1, \"a\", { \"x\" : 1 }]"),
        ["Binary"] = (new BsonBinaryData(new byte[] { 1, 2, 3 }, BsonBinarySubType.Binary), "{ \"$binary\" : { \"base64\" : \"AQID\", \"subType\" : \"00\" } }"),
        ["Binary, a subtype-4 GUID"] = (Guid4(), "{ \"$uuid\" : \"0f8fad5b-d9cb-469f-a165-70867728950e\" }"),
        ["Undefined"] = (BsonUndefined.Value, "{ \"$undefined\" : true }"),
        ["ObjectId"] = (new BsonObjectId(ObjectId.Parse("65a1b2c3d4e5f60718293a4b")), "{ \"$oid\" : \"65a1b2c3d4e5f60718293a4b\" }"),
        ["Boolean"] = (BsonBoolean.True, "true"),
        ["DateTime"] = (new BsonDateTime(1704067200123), "{ \"$date\" : \"2024-01-01T00:00:00.123Z\" }"),
        ["DateTime, whole seconds"] = (new BsonDateTime(1704067200000), "{ \"$date\" : \"2024-01-01T00:00:00Z\" }"),
        ["DateTime, before 1970"] = (new BsonDateTime(-1000), "{ \"$date\" : { \"$numberLong\" : \"-1000\" } }"),
        ["DateTime, after 9999"] = (new BsonDateTime(253402300800000), "{ \"$date\" : { \"$numberLong\" : \"253402300800000\" } }"),
        ["Null"] = (BsonNull.Value, "null"),
        ["RegularExpression"] = (new BsonRegularExpression("^loan-", "im"), "{ \"$regularExpression\" : { \"pattern\" : \"^loan-\", \"options\" : \"im\" } }"),
        ["JavaScript"] = (new BsonJavaScript("f()"), "{ \"$code\" : \"f()\" }"),
        ["Symbol"] = (BsonSymbolTable.Lookup("sym"), "{ \"$symbol\" : \"sym\" }"),
        ["JavaScriptWithScope"] = (new BsonJavaScriptWithScope("g()", new BsonDocument("y", 2)), "{ \"$code\" : \"g()\", \"$scope\" : { \"y\" : 2 } }"),
        ["Int32"] = (new BsonInt32(-7), "-7"),
        ["Timestamp"] = (new BsonTimestamp(unchecked((int)0xF0000000), unchecked((int)0x80000001)), "{ \"$timestamp\" : { \"t\" : 4026531840, \"i\" : 2147483649 } }"),
        ["Int64"] = (new BsonInt64(9007199254740993), "9007199254740993"),
        ["Decimal128"] = (new BsonDecimal128(Decimal128.Parse("1000.50")), "{ \"$numberDecimal\" : \"1000.50\" }"),
        ["MinKey"] = (BsonMinKey.Value, "{ \"$minKey\" : 1 }"),
        ["MaxKey"] = (BsonMaxKey.Value, "{ \"$maxKey\" : 1 }"),
    };

    public static TheoryData<string> TypeNames => [.. EveryType.Keys];

    [Theory]
    [MemberData(nameof(TypeNames))]
    public void Every_bson_type_is_written_as_relaxed_extended_json(string name)
    {
        var (value, expected) = EveryType[name];

        var json = MongoJson.Write(value);

        Assert.Equal(expected, json);
        using (JsonDocument.Parse(json)) { }
        var readBack = BsonDocument.Parse("{ \"v\" : " + json + " }")["v"];
        Assert.Equal(value.BsonType, readBack.BsonType);
        Assert.Equal(value, readBack);
    }

    [Fact]
    public void Every_bson_type_has_a_case()
    {
        // DBPointer, deprecated, has no BsonType: MongoDB.Bson cannot read one, so it never reaches the writer.
        var covered = EveryType.Values.Select(entry => entry.Value.BsonType).ToHashSet();
        var missing = Enum.GetValues<BsonType>().Where(type => type != BsonType.EndOfDocument && !covered.Contains(type));
        Assert.Empty(missing);
    }

    [Fact]
    public void Regex_options_are_written_in_alphabetical_order()
    {
        // As the server could send them: { "r" : /^loan-/ with options "xmi" }, unsorted.
        byte[] bson = [19, 0, 0, 0, 0x0B, (byte)'r', 0, (byte)'^', (byte)'l', (byte)'o', (byte)'a', (byte)'n', (byte)'-', 0, (byte)'x', (byte)'m', (byte)'i', 0, 0];

        var json = MongoJson.Write(new RawBsonDocument(bson)["r"]);

        Assert.Equal("{ \"$regularExpression\" : { \"pattern\" : \"^loan-\", \"options\" : \"imx\" } }", json);
    }

    [Theory]
    [InlineData("1.0", "1.0")]
    [InlineData("-0.0", "-0.0")]
    [InlineData("123456789.0", "123456789.0")]
    [InlineData("0.1", "0.1")]
    [InlineData("1E+300", "1E+300")]
    [InlineData("5E-324", "5E-324")]
    public void Doubles_keep_a_fraction_and_read_back_as_doubles(string literal, string expected)
    {
        var value = double.Parse(literal, CultureInfo.InvariantCulture);

        var json = MongoJson.Write(new BsonDouble(value));

        Assert.Equal(expected, json);
        var readBack = BsonDocument.Parse("{ \"v\" : " + json + " }")["v"];
        Assert.Equal(BsonType.Double, readBack.BsonType);
        // Equal as doubles: the driver's own reader reads -0.0 as 0.0, though the text keeps the sign.
        Assert.Equal(value, readBack.AsDouble);
        Assert.Equal(
            BitConverter.DoubleToInt64Bits(value),
            BitConverter.DoubleToInt64Bits(double.Parse(json, CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData(double.NaN, "NaN")]
    [InlineData(double.PositiveInfinity, "Infinity")]
    [InlineData(double.NegativeInfinity, "-Infinity")]
    public void A_double_json_cannot_hold_is_wrapped(double value, string text)
    {
        var json = MongoJson.Write(new BsonDouble(value));

        Assert.Equal("{ \"$numberDouble\" : \"" + text + "\" }", json);
        Assert.Equal(value, BsonDocument.Parse("{ \"v\" : " + json + " }")["v"].AsDouble);
    }

    [Fact]
    public void Strings_are_escaped_as_the_driver_escapes_them()
    {
        // As themselves: a letter with an accent, <, & and a non-breaking space. A quote and a backslash take a
        // backslash. As lowercase \u escapes: U+2028, an emoji's two halves, DEL and U+0001.
        var text = "é<&" + (char)0xA0 + "\"\\" + (char)0x2028 + char.ConvertFromUtf32(0x1F600) + (char)0x7F + (char)0x01 + "\n";

        var json = MongoJson.Write(new BsonString(text));

        var expected = "\"é<&" + (char)0xA0 + "\\\"\\\\" + Esc(0x2028) + Esc(0xd83d) + Esc(0xde00) + Esc(0x7f) + Esc(0x01) + "\\n\"";
        Assert.Equal(expected, json);
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal(text, parsed.RootElement.GetString());
    }

    [Fact]
    public void Every_utf16_unit_is_escaped_as_the_driver_escapes_it()
    {
        // Each of the 65,536 UTF-16 units alone, as a value and as a name (BSON allows every one in a name but
        // U+0000), against the driver's relaxed writer.
        var settings = new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson };
        var differences = new List<string>();
        for (var code = 0; code <= 0xFFFF; code++)
        {
            var text = ((char)code).ToString();
            var document = new BsonDocument(code == 0 ? "nul" : text, text);
            var ours = MongoJson.Write(document);
            var drivers = document.ToJson(settings);
            if (ours != drivers)
                differences.Add($"U+{code:X4}: ours {ours}, the driver's {drivers}");
        }

        Assert.True(differences.Count == 0, $"{differences.Count} differ, the first: {string.Join("; ", differences.Take(5))}");
    }

    [Fact]
    public void A_binary_over_256_bytes_is_a_placeholder_only_when_asked()
    {
        var large = new BsonBinaryData(new byte[261_120], BsonBinarySubType.Binary);
        var small = new BsonBinaryData(new byte[MongoJson.MaxInlineBinaryBytes], BsonBinarySubType.Binary);

        Assert.Equal("\"[binary: 261,120 bytes, subtype 00]\"", MongoJson.Write(large, binaryPlaceholders: true));
        Assert.StartsWith("{ \"$binary\" : { \"base64\" : \"", MongoJson.Write(large));
        Assert.StartsWith("{ \"$binary\" : { \"base64\" : \"", MongoJson.Write(small, binaryPlaceholders: true));
    }

    [Fact]
    public void A_guid_is_written_whole_whatever_its_size_rule()
    {
        var guid = Guid4();

        Assert.Equal("{ \"$uuid\" : \"0f8fad5b-d9cb-469f-a165-70867728950e\" }", MongoJson.Write(guid, binaryPlaceholders: true));
    }

    [Fact]
    public void JsonWriterSettings_Defaults_do_not_reach_the_writer()
    {
        // MongoDB.Bson's JsonWriterSettings.Defaults is process-wide and changes ToString(); the writer reads none of it.
        var document = Corpus()[3];
        var before = MongoJson.Write(document);
        var defaults = JsonWriterSettings.Defaults;
        try
        {
            JsonWriterSettings.Defaults = new JsonWriterSettings { OutputMode = JsonOutputMode.CanonicalExtendedJson, Indent = true };
            Assert.Equal(before, MongoJson.Write(document));
        }
        finally
        {
            JsonWriterSettings.Defaults = defaults;
        }
    }

    /// <summary>
    /// Documents without a GUID, a large binary or a double, the three values the writer writes its own way: for
    /// these it writes byte for byte what MongoDB.Bson 3.x's relaxed <c>ToJson()</c> writes, which is the text users'
    /// redaction patterns were written against.
    /// </summary>
    internal static IReadOnlyList<BsonDocument> Corpus() =>
    [
        new BsonDocument(),
        new BsonDocument { { "empty", new BsonDocument() }, { "none", new BsonArray() } },
        new BsonDocument { { "status", "Ready" }, { "n", 1 }, { "big", 9007199254740993L }, { "least", int.MinValue }, { "most", long.MaxValue } },
        new BsonDocument
        {
            { "_id", ObjectId.Parse("65a1b2c3d4e5f60718293a4b") }, { "amount", Decimal128.Parse("1000.50") },
            { "negativeZero", Decimal128.Parse("-0") }, { "notANumber", Decimal128.Parse("NaN") },
            { "infinite", Decimal128.Parse("-Infinity") }, { "exponent", Decimal128.Parse("1E+3") },
        },
        new BsonDocument
        {
            { "at", new BsonDateTime(1704067200000) }, { "ms", new BsonDateTime(1704067200123) },
            { "epoch", new BsonDateTime(0) }, { "before", new BsonDateTime(-1) },
            { "last", new BsonDateTime(253402300799999) }, { "after", new BsonDateTime(253402300800000) },
        },
        new BsonDocument("nested", new BsonDocument("deeper", new BsonDocument("deepest", new BsonArray { 1, new BsonArray { 2, 3 }, new BsonDocument() }))),
        new BsonDocument
        {
            { "yes", true }, { "no", false }, { "nothing", BsonNull.Value }, { "undefined", BsonUndefined.Value },
            { "min", BsonMinKey.Value }, { "max", BsonMaxKey.Value },
        },
        new BsonDocument
        {
            { "re", new BsonRegularExpression("^loan-[0-9]+$", "mi") },
            { "ts", new BsonTimestamp(unchecked((int)0xF0000000), unchecked((int)0x80000001)) },
            { "code", new BsonJavaScript("function() { return \"x\"; }") },
            { "scoped", new BsonJavaScriptWithScope("g()", new BsonDocument("y", 2)) },
            { "sym", BsonSymbolTable.Lookup("sym") },
        },
        new BsonDocument
        {
            { "bytes", new BsonBinaryData(new byte[] { 0, 1, 2, 250, 255 }) },
            { "md5", new BsonBinaryData(new byte[16], BsonBinarySubType.MD5) },
            { "user", new BsonBinaryData(new byte[] { 1 }, BsonBinarySubType.UserDefined) },
        },
        new BsonDocument
        {
            { "é", "José ~<b> Élodie & Co <i>" },
            { "quote\"back\\slash", "tab\tnewline\ncr\rff\fbs\b" },
            { "$set", new BsonDocument("a.b", 1) },
            { "marks", "e" + (char)0x0301 + (char)0x02B0 + (char)0x200B + (char)0x2029 + (char)0xE000 + char.ConvertFromUtf32(0x1F600) + (char)0x7F },
        },
        new BsonDocument("email", "ada@example.com"),
    ];

    /// <summary>The corpus as MongoDB.Bson 3.12.0 writes it, one document a line.</summary>
    private static string GoldenPath([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "MongoJsonGolden.txt");

    [Fact]
    public void The_writer_matches_the_driver_on_bson_3()
    {
        var corpus = Corpus();
        var ours = corpus.Select(document => MongoJson.Write(document)).ToArray();

        if (OnBson3)
        {
            var settings = new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson };
            var drivers = corpus.Select(document => document.ToJson(settings)).ToArray();
            if (Environment.GetEnvironmentVariable("KRONIKOL_WRITE_MONGO_JSON_GOLDEN") == "1")
                File.WriteAllText(GoldenPath(), string.Join("\n", drivers) + "\n");
            Assert.Equal(drivers, ours);
        }

        // The V2 project has no 3.x driver to ask, so both projects hold the writer to the 3.x driver's text.
        var golden = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "MongoJsonGolden.txt"));
        Assert.Equal(golden, ours);
    }
}
