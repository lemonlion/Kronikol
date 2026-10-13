#:package MongoDB.Driver@3.12.0
#:property DefineConstants=$(DefineConstants);DRIVER3
#:property NoWarn=$(NoWarn);NU1902;NU1903;CS0618
#:property Nullable=enable
// c-sharded probe: through mongos, the top-level fields (name and BSON type) of every command the driver sends and
// every reply it receives, the value of a command's recoveryToken when it has one, and every reply whole as relaxed
// extended JSON. All of it is read inside the event handler: the driver disposes event documents when it returns.
// Operations, on the kronikol168sh database start-sharded.sh creates (sharded: hashed on _id; plain: unsharded):
//   (a) transactions: insert + update on the sharded collection + commit; insert into the unsharded collection and
//       update on the sharded one + commit; insert + abort; a read-only transaction (find + commit);
//   (b) one insert into the sharded collection and one into the unsharded one, outside a transaction;
//   (c) a find on the sharded collection whose filter has no shard key (sent to every shard that owns a chunk), with
//       BatchSize 2 so getMore follows, and a find by _id (targeted);
//   (d) an insert of a duplicate _id into the sharded collection (writeErrors through mongos).
// It ends with the fields seen per command name, in the layout of ../b-wire-shapes/wire-shapes.cs, and where each
// watched field appeared. run-sharded-extras.sh rewrites the first two lines for driver 2.x; MONGO_URL overrides the URL.
using System.Collections.Concurrent;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;

var url = Environment.GetEnvironmentVariable("MONGO_URL") ?? "mongodb://127.0.0.1:27230/?directConnection=false";
var events = new ConcurrentQueue<Ev>();
var mark = 0;
void P(string s = "") => Console.WriteLine(s);

var settings = MongoClientSettings.FromConnectionString(url);
settings.ClusterConfigurator = cb =>
{
    cb.Subscribe<CommandStartedEvent>(e =>
        events.Enqueue(Ev.Read("started", e.CommandName, e.RequestId, e.DatabaseNamespace?.DatabaseName, e.Command, json: false)));
    cb.Subscribe<CommandSucceededEvent>(e =>
        events.Enqueue(Ev.Read("succeeded", e.CommandName, e.RequestId, null, e.Reply, json: true, ms: e.Duration.TotalMilliseconds)));
    cb.Subscribe<CommandFailedEvent>(e =>
        events.Enqueue(Ev.Read("failed", e.CommandName, e.RequestId, null, (e.Failure as MongoCommandException)?.Result, json: true,
            ms: e.Duration.TotalMilliseconds, failure: $"{e.Failure?.GetType().FullName}: {e.Failure?.Message}")));
};
var client = new MongoClient(settings);
var db = client.GetDatabase("kronikol168sh");
var sharded = db.GetCollection<BsonDocument>("sharded");
var plain = db.GetCollection<BsonDocument>("plain");
var byId = (string id) => Builders<BsonDocument>.Filter.Eq("_id", id);

P($"# Sharded extras: MongoDB.Driver {typeof(MongoClient).Assembly.GetName().Version}");
P($"URL: {url}");
var build = db.RunCommand<BsonDocument>(new BsonDocument("buildInfo", 1));
var hello = db.RunCommand<BsonDocument>(new BsonDocument("hello", 1));
P($"Server {build["version"]}, hello.msg={(hello.Contains("msg") ? hello["msg"] : "(none)")}, setName={(hello.Contains("setName") ? hello["setName"] : "(none)")}, maxWireVersion={hello["maxWireVersion"]}");
P("Each event: its top-level fields as name (BSON type); a command's recoveryToken when it has one; each reply whole as"
  + " relaxed extended JSON, indented. Everything is read inside the event handler.");
P();

Op("setup: empty both collections; the shard key from config.collections, through mongos", () =>
{
    sharded.DeleteMany(FilterDefinition<BsonDocument>.Empty);
    plain.DeleteMany(FilterDefinition<BsonDocument>.Empty);
    var collections = client.GetDatabase("config").GetCollection<BsonDocument>("collections");
    var key = collections.Find(new BsonDocument("_id", "kronikol168sh.sharded")).FirstOrDefault()?["key"];
    var plainTracked = collections.CountDocuments(new BsonDocument("_id", "kronikol168sh.plain"));
    var shards = client.GetDatabase("admin").RunCommand<BsonDocument>(new BsonDocument("listShards", 1))["shards"].AsBsonArray
        .Select(s => s["_id"].AsString);
    P($"   kronikol168sh.sharded key {key?.ToJson() ?? "(not sharded)"}; kronikol168sh.plain in config.collections: {plainTracked}; shards: {string.Join(", ", shards)}");
}, compact: true);

// (a) Transactions
Op("(a1) Transaction on the sharded collection: InsertOne + UpdateOne + CommitTransaction", () =>
{
    using var session = client.StartSession();
    session.StartTransaction();
    sharded.InsertOne(session, new BsonDocument { { "_id", "txn-1" }, { "status", "Pending" }, { "amount", new BsonDecimal128(Decimal128.Parse("100.00")) } });
    sharded.UpdateOne(session, byId("txn-1"), Builders<BsonDocument>.Update.Set("status", "Approved"));
    session.CommitTransaction();
});
Op("(a2) Transaction across both collections: InsertOne (unsharded) + UpdateOne (sharded) + CommitTransaction", () =>
{
    using var session = client.StartSession();
    session.StartTransaction();
    plain.InsertOne(session, new BsonDocument { { "_id", "txn-2" }, { "status", "Pending" } });
    sharded.UpdateOne(session, byId("txn-1"), Builders<BsonDocument>.Update.Set("status", "Funded"));
    session.CommitTransaction();
});
Op("(a3) Transaction: InsertOne (sharded) + AbortTransaction", () =>
{
    using var session = client.StartSession();
    session.StartTransaction();
    sharded.InsertOne(session, new BsonDocument { { "_id", "txn-3" }, { "status", "Pending" } });
    session.AbortTransaction();
});
Op("(a4) Read-only transaction: Find by _id (sharded) + CommitTransaction", () =>
{
    using var session = client.StartSession();
    session.StartTransaction();
    sharded.Find(session, byId("txn-1")).ToList();
    session.CommitTransaction();
});

// (b) Inserts outside a transaction
Op("(b1) InsertOne into the sharded collection (no transaction)", () =>
    sharded.InsertOne(new BsonDocument { { "_id", "ins-sharded-1" }, { "status", "Pending" } }));
Op("(b2) InsertOne into the unsharded collection (no transaction)", () =>
    plain.InsertOne(new BsonDocument { { "_id", "ins-plain-1" }, { "status", "Pending" } }));

// (c) Finds on the sharded collection
Op("setup: six documents in the sharded collection", () =>
    sharded.InsertMany(Enumerable.Range(0, 6).Select(i => new BsonDocument { { "_id", $"find-{i}" }, { "kind", "batch" }, { "n", i } })),
    compact: true);
Op("(c1) Find on the sharded collection, no shard key in the filter (every shard owning a chunk), BatchSize 2", () =>
    P($"   returned {sharded.Find(Builders<BsonDocument>.Filter.Eq("kind", "batch"), new FindOptions { BatchSize = 2 }).ToList().Count} documents"));
Op("(c2) Find on the sharded collection by _id (targeted)", () =>
    P($"   returned {sharded.Find(byId("find-3")).ToList().Count} documents"));

// (d) A write error on the sharded collection
Op("(d) InsertOne of a duplicate _id into the sharded collection", () =>
    sharded.InsertOne(new BsonDocument { { "_id", "ins-sharded-1" }, { "status", "Duplicate" } }));

// Summaries
P("## Field summary: top-level fields per command name (first-seen order), and per reply");
var byName = new Dictionary<string, (List<string> cmd, List<string> reply, int n)>();
foreach (var ev in events)
{
    if (!byName.TryGetValue(ev.Name, out var t)) t = (new List<string>(), new List<string>(), 0);
    var target = ev.Kind == "started" ? t.cmd : t.reply;
    foreach (var f in ev.Fields) if (!target.Contains(f.Name)) target.Add(f.Name);
    byName[ev.Name] = (t.cmd, t.reply, t.n + (ev.Kind == "started" ? 1 : 0));
}
foreach (var (name, t) in byName.OrderBy(k => k.Key, StringComparer.Ordinal))
{
    P($"- {name} (x{t.n})");
    P($"    command: {string.Join(", ", t.cmd)}");
    P($"    reply:   {string.Join(", ", t.reply)}");
}
P();
P("## Watched fields: where each appeared (command or reply, and the command name)");
foreach (var w in new[] { "ok", "$clusterTime", "operationTime", "electionId", "opTime", "$configServerState", "$topologyTime",
             "lastCommittedOpTime", "$gleStats", "$configTime", "recoveryToken", "readOnly", "$replData", "writeConcernError", "errorLabels" })
{
    var where = events.Where(e => e.Fields.Any(f => f.Name == w))
        .Select(e => $"{(e.Kind == "started" ? "command" : "reply")} {e.Name}").Distinct().ToList();
    P($"- {w}: {(where.Count == 0 ? "never" : string.Join(", ", where))}");
}
var unreadable = events.Where(e => e.Error is not null).ToList();
P($"- events whose document could not be read inside the handler: {unreadable.Count}");
foreach (var e in unreadable.Take(5)) P($"   {e.Kind} {e.Name} #{e.RequestId}: {e.Error}");
P();

void Op(string title, Action action, bool compact = false)
{
    mark = events.Count;
    P($"## {title}");
    try { action(); }
    catch (Exception ex) { P($"   API threw {ex.GetType().FullName}: {ex.Message.Replace("\n", " ")}"); }
    foreach (var ev in events.Skip(mark).ToList())
    {
        if (compact)
        {
            P($"   {ev.Kind} {ev.Name} #{ev.RequestId}{(ev.Failure is null ? "" : " " + ev.Failure)}");
            continue;
        }
        var head = ev.Kind switch
        {
            "started" => $">> CommandStartedEvent name={ev.Name} db={ev.Db} requestId={ev.RequestId}",
            "succeeded" => $"<< CommandSucceededEvent name={ev.Name} requestId={ev.RequestId} ({ev.Ms:F1} ms)",
            _ => $"<< CommandFailedEvent name={ev.Name} requestId={ev.RequestId} ({ev.Ms:F1} ms) failure={ev.Failure}"
        };
        P(head);
        P($"   {(ev.Kind == "failed" ? "result fields (MongoCommandException.Result)" : "fields")}: {string.Join(", ", ev.Fields.Select(f => $"{f.Name} ({f.Type})"))}");
        if (ev.RecoveryToken is not null) P($"   recoveryToken: {ev.RecoveryToken}");
        if (ev.Error is not null) P($"   <THREW {ev.Error}>");
        if (ev.Json is not null) P("   " + ev.Json.Replace("\n", "\n   "));
    }
    P();
}

sealed class Ev
{
    public string Kind = "", Name = "", Db = "";
    public int RequestId;
    public double Ms;
    public string? Failure, Json, RecoveryToken, Error;
    public List<(string Name, string Type)> Fields = new();

    public static Ev Read(string kind, string name, int requestId, string? db, BsonDocument? doc, bool json, double ms = 0, string? failure = null)
    {
        var ev = new Ev { Kind = kind, Name = name, RequestId = requestId, Db = db ?? "", Ms = ms, Failure = failure };
        if (doc is null) return ev;
        try
        {
            foreach (var el in doc)
                ev.Fields.Add((el.Name, el.Value is BsonArray a ? $"Array[{a.Count}]" : el.Value.BsonType.ToString()));
            if (kind == "started" && doc.TryGetValue("recoveryToken", out var token)) ev.RecoveryToken = token.ToJson(Settings(indent: false));
            if (json) ev.Json = doc.ToJson(Settings(indent: true));
        }
        catch (Exception ex) { ev.Error = $"{ex.GetType().FullName}: {ex.Message}"; }
        return ev;
    }

    static JsonWriterSettings Settings(bool indent)
    {
        var s = new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson, Indent = indent, IndentChars = "  ", NewLineChars = "\n" };
#if DRIVER2
        s.GuidRepresentation = GuidRepresentation.Unspecified;
#endif
        return s;
    }
}
