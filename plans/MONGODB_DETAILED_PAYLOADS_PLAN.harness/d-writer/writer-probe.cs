#:package MongoDB.Bson@3.12.0
// What a Kronikol-owned BSON-to-JSON writer must match: the driver's string escaping and double formatting in relaxed
// mode, against System.Text.Json's Utf8JsonWriter (default encoder and UnsafeRelaxedJsonEscaping); whether {"$uuid"}
// reads back through BsonDocument.Parse; whether MongoDB.Bson can read a DBPointer (type 0x0C); and the gzip size of
// one insert body written compact and spaced.
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;

var relaxed = new JsonWriterSettings { OutputMode = (JsonOutputMode)Enum.Parse(typeof(JsonOutputMode), "RelaxedExtendedJson") };
Console.WriteLine($"MongoDB.Bson {typeof(BsonDocument).Assembly.GetName().Version}");

string Stj(Action<Utf8JsonWriter> w, JavaScriptEncoder? enc)
{
    using var ms = new MemoryStream();
    using (var jw = new Utf8JsonWriter(ms, new JsonWriterOptions { Encoder = enc })) w(jw);
    return Encoding.UTF8.GetString(ms.ToArray());
}

Console.WriteLine("\n== strings: driver relaxed | STJ default | STJ UnsafeRelaxed");
foreach (var s in new[] { "José", "<b>&'+", "line\u2028sep", "emoji 😀", "tab\there", "q\"uote\\", "ctrl\u0001", "del\u007f", "nbsp\u00a0" })
{
    var d = new BsonDocument("s", s).ToJson(relaxed);
    Console.WriteLine($"{d} | {Stj(w => w.WriteStringValue(s), null)} | {Stj(w => w.WriteStringValue(s), JavaScriptEncoder.UnsafeRelaxedJsonEscaping)}");
}

Console.WriteLine("\n== doubles: driver relaxed | STJ WriteNumberValue | Parse(driver) type");
foreach (var x in new[] { 1.0, -0.0, 0.1, 1e300, 5e-324, 1.2345678901234567e18, 123456789.0, 1e21, 1.5e-7 })
{
    var d = new BsonDocument("d", x).ToJson(relaxed);
    var back = BsonDocument.Parse(d)["d"].BsonType;
    Console.WriteLine($"{d} | {Stj(w => w.WriteNumberValue(x), null)} | {back}");
}

Console.WriteLine("\n== $uuid read back");
foreach (var j in new[] { "{\"u\":{\"$uuid\":\"0f8fad5b-d9cb-469f-a165-70867728950e\"}}" })
{
    try { var v = BsonDocument.Parse(j)["u"]; Console.WriteLine($"{j} -> {v.BsonType} subtype {(v as BsonBinaryData)?.SubType} -> {new BsonDocument("u", v).ToJson(relaxed)}"); }
    catch (Exception e) { Console.WriteLine($"{j} -> THROWS {e.GetType().Name}: {e.Message}"); }
}

Console.WriteLine("\n== DBPointer (type 0x0C) read by BsonSerializer");
{
    var ms = new MemoryStream(); var bw = new BinaryWriter(ms);
    bw.Write(0); bw.Write((byte)0x0C); bw.Write(Encoding.UTF8.GetBytes("p\0"));
    var ns = Encoding.UTF8.GetBytes("db.coll\0"); bw.Write(ns.Length); bw.Write(ns); bw.Write(new byte[12]);
    bw.Write((byte)0); var bytes = ms.ToArray(); BitConverter.GetBytes(bytes.Length).CopyTo(bytes, 0);
    try { var doc = BsonSerializer.Deserialize<BsonDocument>(bytes); Console.WriteLine($"read: {doc["p"].BsonType} {doc.ToJson(relaxed)}"); }
    catch (Exception e) { Console.WriteLine($"THROWS {e.GetType().Name}: {e.Message}"); }
}

Console.WriteLine("\n== size of one insert body of 10 documents: spaced (driver) vs compact");
{
    var docs = new BsonArray();
    for (var i = 0; i < 10; i++)
        docs.Add(new BsonDocument { { "_id", ObjectId.GenerateNewId() }, { "loanNumber", $"L-{1000 + i}" },
            { "principal", new Decimal128(1000.50m + i) }, { "status", "Ready" }, { "createdAt", new DateTime(2024, 1, 1, 0, 0, i, DateTimeKind.Utc) },
            { "tags", new BsonArray { "a", "b" } }, { "owner", new BsonDocument { { "name", "José Q" }, { "age", 42 } } } });
    var spaced = new BsonDocument("documents", docs).ToJson(relaxed);
    var compact = Stj(w => { using var jd = JsonDocument.Parse(spaced); jd.WriteTo(w); }, JavaScriptEncoder.UnsafeRelaxedJsonEscaping);
    int Gz(string t) { using var o = new MemoryStream(); using (var g = new GZipStream(o, CompressionLevel.Optimal)) g.Write(Encoding.UTF8.GetBytes(t)); return o.ToArray().Length; }
    Console.WriteLine($"spaced {spaced.Length} chars, gzip {Gz(spaced)} B; compact {compact.Length} chars, gzip {Gz(compact)} B");
    Console.WriteLine($"spaced head: {spaced[..120]}");
    Console.WriteLine($"compact head: {compact[..120]}");
}
