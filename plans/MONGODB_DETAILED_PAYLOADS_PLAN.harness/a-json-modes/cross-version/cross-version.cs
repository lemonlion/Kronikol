#:package MongoDB.Bson@3.12.0
#:package Kronikol.Extensions.TcpTap@4.18.1
#:project Bson2Writer/Bson2Writer.csproj
#:property NoWarn=$(NoWarn);NU1902;NU1903
// Code compiled against MongoDB.Bson 2.30.0 (the Bson2Writer library, and the published Kronikol.Extensions.TcpTap
// 4.18.1, whose shared MongoDbResponseSummary is compiled against Bson 2.30.0) running in a host whose Bson is the
// version on the first line. run-cross-version.sh runs it with 3.12.0 and with 2.30.0.
using System.Reflection;
using MongoDB.Bson;
using MongoDB.Bson.IO;

var asm = typeof(BsonDocument).Assembly;
Console.WriteLine($"Runtime MongoDB.Bson {asm.GetName().Version}; JsonOutputMode members: {string.Join(", ", Enum.GetNames(typeof(JsonOutputMode)).Select(n => $"{n}={(int)Enum.Parse(typeof(JsonOutputMode), n)}"))}");
Console.WriteLine($"Bson2Writer baked JsonOutputMode.RelaxedExtendedJson as {Bson2Writer.Writers.RelaxedConstantAsCompiled}; at run time that value is '{(JsonOutputMode)Bson2Writer.Writers.RelaxedConstantAsCompiled}' (defined: {Enum.IsDefined(typeof(JsonOutputMode), Bson2Writer.Writers.RelaxedConstantAsCompiled)})");

var guid = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
var doc = new BsonDocument
{
    { "amount", new BsonDecimal128(Decimal128.Parse("1000.50")) },
    { "_id", new BsonObjectId(ObjectId.Parse("65a1b2c3d4e5f60718293a4b")) },
    { "at", new BsonDateTime(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)) },
    { "count", new BsonInt64(5) },
    { "ratio", new BsonDouble(double.NaN) },
    { "id4", new BsonBinaryData(guid, GuidRepresentation.Standard) },
    { "pattern", new BsonRegularExpression("^loan-", "i") }
};

Run("Bson2Writer.Today (no OutputMode, as MongoDbResponseSummary does)", () => Bson2Writer.Writers.Today(doc));
Run("Bson2Writer.RelaxedByEnum (OutputMode = JsonOutputMode.RelaxedExtendedJson in 2.x-compiled source)", () => Bson2Writer.Writers.RelaxedByEnum(doc));
Run("Bson2Writer.RelaxedWithUnspecifiedGuid (also sets the 2.x-only JsonWriterSettings.GuidRepresentation)", () => Bson2Writer.Writers.RelaxedWithUnspecifiedGuid(doc));
Run("Bson2Writer.RelaxedByName (Enum.Parse(\"RelaxedExtendedJson\") at run time)", () => Bson2Writer.Writers.RelaxedByName(doc));

// The real tap code: Kronikol.Extensions.TcpTap 4.18.1's linked MongoDbResponseSummary, compiled against Bson 2.30.0.
var reply = new BsonDocument
{
    { "cursor", new BsonDocument { { "firstBatch", new BsonArray { doc } }, { "id", 0L }, { "ns", "loans.loans" } } },
    { "ok", 1.0 }
};
Run("Kronikol.Extensions.TcpTap 4.18.1 MongoDbResponseSummary.ExtractDetailed(reply, true, 10)",
    () => Kronikol.Extensions.TcpTap.Protocols.MongoDbResponseSummary.ExtractDetailed(reply, true, 10) ?? "<null>");

static void Run(string label, Func<string> f)
{
    Console.WriteLine($"--- {label}");
    try { Console.WriteLine(f()); }
    catch (Exception ex) { Console.WriteLine($"<THREW {ex.GetType().FullName}: {ex.Message}>"); }
}
