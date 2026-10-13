#:package MongoDB.Driver@3.12.0
#:package Kronikol.Extensions.MongoDB@4.18.1
#:property DefineConstants=$(DefineConstants);DRIVER3
#:property NoWarn=$(NoWarn);NU1902;NU1903;CS0618
#:property Nullable=enable
// Part B probe: the command and reply documents the driver's command events carry, per operation, against a real
// server, and what Kronikol 4.18.1 records for each (the published packages: Kronikol.Extensions.MongoDB for driver
// 3.x, Kronikol.Extensions.MongoDB.V2 for driver 2.x; the source is unchanged on origin/main 34145c44).
// run-wire-shapes.sh replaces the first five lines for driver 2.30.0. MONGO_URL overrides the server.
using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Kronikol.Extensions.MongoDB;
using Kronikol.Tracking;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;
using MongoDB.Driver.GridFS;

var url = Environment.GetEnvironmentVariable("MONGO_URL") ?? "mongodb://127.0.0.1:27217/?replicaSet=rs0";
var driverVersion = typeof(MongoClient).Assembly.GetName().Version;
int cursorIndex = 0;
int logIndex = 0;
void P(string s = "") { Console.WriteLine(s); }

var events = new ConcurrentQueue<Ev>();
var started = new ConcurrentDictionary<int, Ev>();
var fetchOn = true;
(string Name, string Id) Fetch() => fetchOn ? ("harness", "h1") : throw new InvalidOperationException("no test is running");

var detailed = new MongoDbTrackingSubscriber(new MongoDbTrackingOptions
{
    ServiceName = "MongoDB-Detailed", Verbosity = MongoDbTrackingVerbosity.Detailed, CurrentTestInfoFetcher = Fetch, TrackGetMore = true
});
var raw = new MongoDbTrackingSubscriber(new MongoDbTrackingOptions
{
    ServiceName = "MongoDB-Raw", Verbosity = MongoDbTrackingVerbosity.Raw, CurrentTestInfoFetcher = Fetch, TrackGetMore = true
});

MongoClient NewClient(string connectionString, Action<MongoClientSettings>? tweak = null)
{
    var settings = MongoClientSettings.FromConnectionString(connectionString);
    tweak?.Invoke(settings);
    settings.ClusterConfigurator = cb =>
    {
        cb.Subscribe<CommandStartedEvent>(e =>
        {
            var ev = new Ev("started", e.CommandName, e.RequestId, e.DatabaseNamespace?.DatabaseName)
            {
                Doc = e.Command, JsonAtEvent = TryJ(e.Command), TypeInfo = Describe(e.Command), Thread = Environment.CurrentManagedThreadId,
                Names = SafeNames(e.Command).ToList()
            };
            started[e.RequestId] = ev;
            events.Enqueue(ev);
        });
        cb.Subscribe<CommandSucceededEvent>(e =>
        {
            var ev = new Ev("succeeded", e.CommandName, e.RequestId, null)
            {
                Doc = e.Reply, JsonAtEvent = TryJ(e.Reply), TypeInfo = Describe(e.Reply), DurationMs = e.Duration.TotalMilliseconds,
                Thread = Environment.CurrentManagedThreadId, Names = SafeNames(e.Reply).ToList()
            };
            // What Kronikol's deferred findAndModify does: read the started event's command in the succeeded handler.
            if (started.TryGetValue(e.RequestId, out var s)) ev.StartedCommandAtReply = TryJ(s.Doc);
            events.Enqueue(ev);
        });
        cb.Subscribe<CommandFailedEvent>(e =>
        {
            var ev = new Ev("failed", e.CommandName, e.RequestId, null)
            {
                Failure = $"{e.Failure?.GetType().FullName}: {e.Failure?.Message}", DurationMs = e.Duration.TotalMilliseconds,
                Thread = Environment.CurrentManagedThreadId
            };
            if (e.Failure is MongoCommandException mce) ev.Doc = mce.Result;
            if (ev.Doc is not null) { ev.JsonAtEvent = TryJ(ev.Doc); ev.TypeInfo = Describe(ev.Doc); ev.Names = SafeNames(ev.Doc).ToList(); }
            if (started.TryGetValue(e.RequestId, out var s)) ev.StartedCommandAtReply = TryJ(s.Doc);
            events.Enqueue(ev);
        });
        detailed.Subscribe(cb);
        raw.Subscribe(cb);
    };
    return new MongoClient(settings);
}

var client = NewClient(url);
var db = client.GetDatabase("kronikol168");
var loans = db.GetCollection<BsonDocument>("loans");

P($"# Wire shapes: MongoDB.Driver {driverVersion}, Kronikol 4.18.1 ({typeof(MongoDbTrackingSubscriber).Assembly.GetName().Name})");
P($"URL: {url}");
var build = db.RunCommand<BsonDocument>(new BsonDocument("buildInfo", 1));
var hello = db.RunCommand<BsonDocument>(new BsonDocument("hello", 1));
P($"Server {build["version"]}, setName={(hello.Contains("setName") ? hello["setName"] : "(none)")}, maxWireVersion={hello["maxWireVersion"]}");
P("Every document below is written as relaxed extended JSON, indented"
#if DRIVER2
  + ", with JsonWriterSettings.GuidRepresentation = Unspecified so a UUID binary shows as stored (part A: V2 mode re-encodes subtype 4 as 3)"
#endif
  + ". Base64 longer than 120 characters is cut.");
P();

// ------------------------------------------------------------------------------------------------------------
// Setup (recorded compactly)
Op("setup: drop the database, unique index on loanNumber", () =>
{
    client.DropDatabase("kronikol168");
    loans.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(
        Builders<BsonDocument>.IndexKeys.Ascending("loanNumber"), new CreateIndexOptions { Unique = true }));
}, compact: true);

var loanId = Guid.Parse("3f2b8c1e-7d4a-4e5b-9c6d-1a2b3c4d5e6f");
BsonDocument Loan(string number, string amount, string status, Guid id) => new()
{
    { "loanNumber", number },
    { "loanId", new BsonBinaryData(id, GuidRepresentation.Standard) },
    { "customer", new BsonDocument { { "name", "Ada Lovelace" }, { "email", "ada@example.com" },
        { "address", new BsonDocument { { "line1", "12 St James's Square" }, { "city", "London" }, { "postcode", "SW1Y 4JH" } } } } },
    { "principal", new BsonDecimal128(Decimal128.Parse(amount)) },
    { "rate", new BsonDecimal128(Decimal128.Parse("4.25")) },
    { "termMonths", 300 },
    { "status", status },
    { "createdAt", new BsonDateTime(new DateTime(2026, 10, 13, 9, 30, 0, DateTimeKind.Utc)) },
    { "tags", new BsonArray { "mortgage", "first-time-buyer" } },
    { "payments", new BsonArray {
        new BsonDocument { { "due", new BsonDateTime(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)) }, { "amount", new BsonDecimal128(Decimal128.Parse("1354.27")) }, { "paid", false } } } }
};

// 1. InsertOne without an _id: does the driver add one to the command, and to the caller's document?
var first = Loan("L-1001", "250000.00", "Pending", loanId);
Op("InsertOne (no _id in the caller's document)", () => loans.InsertOne(first),
    after: () => P($"   caller's document has _id after InsertOne: {first.Contains("_id")} ({(first.Contains("_id") ? first["_id"].ToString() : "-")})"));

// 2. InsertMany of 3 with Decimal128
Op("InsertMany of 3 documents", () => loans.InsertMany(new[]
{
    Loan("L-1002", "120000.50", "Pending", Guid.Parse("00000000-0000-0000-0000-000000000002")),
    Loan("L-1003", "98000.00", "Approved", Guid.Parse("00000000-0000-0000-0000-000000000003")),
    Loan("L-1004", "450000.75", "Pending", Guid.Parse("00000000-0000-0000-0000-000000000004")),
}));

// 3-6. Updates
Op("UpdateOne with $set", () => loans.UpdateOne(
    Builders<BsonDocument>.Filter.Eq("loanNumber", "L-1001"),
    Builders<BsonDocument>.Update.Set("status", "Approved").Set("approvedAt", new DateTime(2026, 10, 13, 10, 0, 0, DateTimeKind.Utc))
        .Inc("version", 1)));
Op("UpdateMany", () => loans.UpdateMany(
    Builders<BsonDocument>.Filter.Eq("status", "Pending"),
    Builders<BsonDocument>.Update.Set("reviewer", "risk-team").Set("fee", new BsonDecimal128(Decimal128.Parse("99.99")))));
Op("ReplaceOne", () => loans.ReplaceOne(
    Builders<BsonDocument>.Filter.Eq("loanNumber", "L-1003"),
    Loan("L-1003", "97500.00", "Approved", Guid.Parse("00000000-0000-0000-0000-000000000003"))));
Op("UpdateOne upsert that inserts", () => loans.UpdateOne(
    Builders<BsonDocument>.Filter.Eq("loanNumber", "L-2000"),
    Builders<BsonDocument>.Update.Set("status", "Draft").SetOnInsert("principal", new BsonDecimal128(Decimal128.Parse("5000.00"))),
    new UpdateOptions { IsUpsert = true }));
Op("UpdateOne with an aggregation pipeline (u is an array)", () => loans.UpdateOne(
    Builders<BsonDocument>.Filter.Eq("loanNumber", "L-1004"),
    Builders<BsonDocument>.Update.Pipeline(new BsonDocument[]
    {
        new("$set", new BsonDocument("total", new BsonDocument("$add", new BsonArray { "$principal", "$fee" })))
    })));

// 7-8. Deletes (on documents inserted for the purpose)
loans.InsertMany(new[] { Loan("L-9001", "1.00", "Cancelled", Guid.NewGuid()), Loan("L-9002", "2.00", "Cancelled", Guid.NewGuid()), Loan("L-9003", "3.00", "Cancelled", Guid.NewGuid()) });
DrainSilently();
Op("DeleteOne", () => loans.DeleteOne(Builders<BsonDocument>.Filter.Eq("loanNumber", "L-9001")));
Op("DeleteMany", () => loans.DeleteMany(Builders<BsonDocument>.Filter.Eq("status", "Cancelled")));

// 9-12. findAndModify
Op("FindOneAndUpdate (ReturnDocument.After)", () => loans.FindOneAndUpdate(
    Builders<BsonDocument>.Filter.Eq("loanNumber", "L-1002"),
    Builders<BsonDocument>.Update.Set("status", "Funded").Set("fundedAmount", new BsonDecimal128(Decimal128.Parse("120000.50"))),
    new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After }));
Op("FindOneAndReplace (ReturnDocument.After)", () => loans.FindOneAndReplace(
    Builders<BsonDocument>.Filter.Eq("loanNumber", "L-2000"),
    new BsonDocument { { "loanNumber", "L-2000" }, { "status", "Withdrawn" }, { "principal", new BsonDecimal128(Decimal128.Parse("5000.00")) } },
    new FindOneAndReplaceOptions<BsonDocument> { ReturnDocument = ReturnDocument.After }));
Op("FindOneAndDelete", () => loans.FindOneAndDelete(Builders<BsonDocument>.Filter.Eq("loanNumber", "L-2000")));
Op("FindOneAndUpdate matching nothing", () => loans.FindOneAndUpdate(
    Builders<BsonDocument>.Filter.Eq("loanNumber", "L-NOPE"),
    Builders<BsonDocument>.Update.Set("status", "Funded")));
Op("FindOneAndUpdate upsert that inserts (ReturnDocument.Before)", () => loans.FindOneAndUpdate(
    Builders<BsonDocument>.Filter.Eq("loanNumber", "L-3000"),
    Builders<BsonDocument>.Update.Set("status", "Draft"),
    new FindOneAndUpdateOptions<BsonDocument> { IsUpsert = true }));

// 13-14. Reads
Op("Find with a filter", () => loans.Find(Builders<BsonDocument>.Filter.Eq("status", "Approved")).ToList());
for (var i = 0; i < 6; i++) loans.InsertOne(new BsonDocument { { "loanNumber", $"L-50{i:00}" }, { "status", "Batch" }, { "principal", new BsonDecimal128(Decimal128.Parse($"{1000 + i}.00")) } });
DrainSilently();
Op("Find with Limit(5) and BatchSize 2 (find, then getMore)", () =>
    loans.Find(Builders<BsonDocument>.Filter.Eq("status", "Batch"), new FindOptions { BatchSize = 2 })
        .Sort(Builders<BsonDocument>.Sort.Ascending("loanNumber")).Limit(5).ToList());
Op("Find with BatchSize 2 and no limit, abandoned after the first document (killCursors)", () =>
{
    using var cursor = loans.Find(Builders<BsonDocument>.Filter.Eq("status", "Batch"), new FindOptions { BatchSize = 2 }).ToCursor();
    cursor.MoveNext();
});
Op("CountDocuments", () => loans.CountDocuments(Builders<BsonDocument>.Filter.Eq("status", "Batch")));
Op("EstimatedDocumentCount", () => loans.EstimatedDocumentCount());
Op("Distinct", () => loans.Distinct<string>("status", Builders<BsonDocument>.Filter.Exists("principal")).ToList());
Op("Aggregate $match/$group", () => loans.Aggregate<BsonDocument>(new BsonDocument[]
{
    new("$match", new BsonDocument("principal", new BsonDocument("$exists", true))),
    new("$group", new BsonDocument { { "_id", "$status" }, { "total", new BsonDocument("$sum", "$principal") }, { "n", new BsonDocument("$sum", 1) } }),
    new("$sort", new BsonDocument("_id", 1))
}).ToList());

// 15. Collection-level BulkWrite
Op("Collection BulkWrite: insert, update, delete, insert (ordered)", () => loans.BulkWrite(new WriteModel<BsonDocument>[]
{
    new InsertOneModel<BsonDocument>(new BsonDocument { { "loanNumber", "L-6001" }, { "status", "Pending" }, { "principal", new BsonDecimal128(Decimal128.Parse("700.00")) } }),
    new UpdateOneModel<BsonDocument>(Builders<BsonDocument>.Filter.Eq("loanNumber", "L-6001"), Builders<BsonDocument>.Update.Set("status", "Approved")),
    new DeleteOneModel<BsonDocument>(Builders<BsonDocument>.Filter.Eq("loanNumber", "L-5000")),
    new InsertOneModel<BsonDocument>(new BsonDocument { { "loanNumber", "L-6002" }, { "status", "Pending" } }),
}));

// 16-17. Write errors
Op("InsertOne violating the unique index (duplicate key)", () =>
{
    try { loans.InsertOne(new BsonDocument { { "loanNumber", "L-1001" }, { "status", "Duplicate" } }); }
    catch (Exception ex) { P($"   API threw {ex.GetType().FullName}: {ex.Message}"); }
});
Op("InsertMany IsOrdered=false, second of three a duplicate", () =>
{
    try
    {
        loans.InsertMany(new[]
        {
            new BsonDocument { { "loanNumber", "L-7001" }, { "status", "Pending" } },
            new BsonDocument { { "loanNumber", "L-1001" }, { "status", "Duplicate" } },
            new BsonDocument { { "loanNumber", "L-7003" }, { "status", "Pending" } },
        }, new InsertManyOptions { IsOrdered = false });
    }
    catch (Exception ex) { P($"   API threw {ex.GetType().FullName}: {ex.Message}"); }
});
Op("UpdateOne making a duplicate key", () =>
{
    try { loans.UpdateOne(Builders<BsonDocument>.Filter.Eq("loanNumber", "L-7001"), Builders<BsonDocument>.Update.Set("loanNumber", "L-1001")); }
    catch (Exception ex) { P($"   API threw {ex.GetType().FullName}: {ex.Message}"); }
});
Op("Find with an unknown operator (server ok: 0)", () =>
{
    try { loans.Find(new BsonDocument("principal", new BsonDocument("$bogus", 1))).ToList(); }
    catch (Exception ex) { P($"   API threw {ex.GetType().FullName}: {ex.Message}"); }
});

// 18. Transaction
Op("Transaction: InsertOne + UpdateOne + CommitTransaction", () =>
{
    using var session = client.StartSession();
    session.StartTransaction();
    loans.InsertOne(session, new BsonDocument { { "loanNumber", "L-8001" }, { "status", "Pending" }, { "principal", new BsonDecimal128(Decimal128.Parse("8001.00")) } });
    loans.UpdateOne(session, Builders<BsonDocument>.Filter.Eq("loanNumber", "L-8001"), Builders<BsonDocument>.Update.Set("status", "Approved"));
    session.CommitTransaction();
});
Op("Transaction: InsertOne + AbortTransaction", () =>
{
    using var session = client.StartSession();
    session.StartTransaction();
    loans.InsertOne(session, new BsonDocument { { "loanNumber", "L-8002" }, { "status", "Pending" } });
    session.AbortTransaction();
});

// 19. Client-level bulkWrite (driver 3.x API, server 8.0+)
#if DRIVER3
Op("IMongoClient.BulkWrite (client bulkWrite): insert, update, delete across two collections", () =>
{
    try
    {
        var result = client.BulkWrite(new BulkWriteModel[]
        {
            new BulkWriteInsertOneModel<BsonDocument>(CollectionNamespace.FromFullName("kronikol168.loans"),
                new BsonDocument { { "loanNumber", "L-9101" }, { "status", "Pending" }, { "principal", new BsonDecimal128(Decimal128.Parse("9101.00")) } }),
            new BulkWriteUpdateOneModel<BsonDocument>(CollectionNamespace.FromFullName("kronikol168.loans"),
                Builders<BsonDocument>.Filter.Eq("loanNumber", "L-9101"), Builders<BsonDocument>.Update.Set("status", "Approved")),
            new BulkWriteInsertOneModel<BsonDocument>(CollectionNamespace.FromFullName("kronikol168.audit"),
                new BsonDocument { { "loanNumber", "L-9101" }, { "event", "approved" } }),
            new BulkWriteDeleteOneModel<BsonDocument>(CollectionNamespace.FromFullName("kronikol168.loans"),
                Builders<BsonDocument>.Filter.Eq("loanNumber", "L-5001")),
        }, new ClientBulkWriteOptions { VerboseResult = true });
        P($"   result: inserted={result.InsertedCount} matched={result.MatchedCount} modified={result.ModifiedCount} deleted={result.DeletedCount}");
    }
    catch (Exception ex) { P($"   API threw {ex.GetType().FullName}: {ex.Message}"); }
});
#else
P("## IMongoClient.BulkWrite: not in MongoDB.Driver 2.x (no client-level bulk write API)");
P();
#endif

// 20. POCOs through the driver's default serializers
Op("InsertOne of a POCO with default serializers (decimal, Guid, DateTime, List, nested class)", () =>
{
    try
    {
        db.GetCollection<LoanPoco>("pocos").InsertOne(new LoanPoco
        {
            LoanNumber = "P-1", Principal = 1000.50m, LoanId = loanId, CreatedAt = new DateTime(2026, 10, 13, 9, 30, 0, DateTimeKind.Utc),
            Tags = new List<string> { "mortgage" }, Address = new AddressPoco { City = "London" }
        });
    }
    catch (Exception ex) { P($"   API threw {ex.GetType().FullName}: {ex.Message}"); }
});
Op("InsertOne of a POCO with [BsonRepresentation(Decimal128)] and [BsonGuidRepresentation(Standard)]", () =>
{
    try
    {
        db.GetCollection<LoanPocoAttributed>("pocos").InsertOne(new LoanPocoAttributed
        {
            LoanNumber = "P-2", Principal = 1000.50m, LoanId = loanId, CreatedAt = new DateTime(2026, 10, 13, 9, 30, 0, DateTimeKind.Utc)
        });
    }
    catch (Exception ex) { P($"   API threw {ex.GetType().FullName}: {ex.Message}"); }
});

Op("InsertOne of a POCO with default serializers and no Guid (decimal, DateTime)", () =>
{
    try
    {
        db.GetCollection<PlainPoco>("pocos").InsertOne(new PlainPoco
        {
            LoanNumber = "P-3", Principal = 1000.50m, CreatedAt = new DateTime(2026, 10, 13, 9, 30, 0, DateTimeKind.Utc)
        });
    }
    catch (Exception ex) { P($"   API threw {ex.GetType().FullName}: {ex.Message}"); }
});

// 21. GridFS: the chunk inserts are what a Detailed insert note would carry
Op("GridFS UploadFromBytes of 600 KB (chunk inserts)", () =>
{
    var bytes = new byte[600 * 1024];
    new Random(168).NextBytes(bytes);
    var bucket = new GridFSBucket(db);
    var id = bucket.UploadFromBytes("statement.pdf", bytes);
    P($"   uploaded id {id}");
}, compactDocs: true);

// 22. The held references, after every operation above: is a command or reply the event handed out still readable?
P("## Reading e.Command / e.Reply references after their events returned");
{
    int same = 0, differ = 0, threw = 0, total = 0;
    var examples = new List<string>();
    foreach (var ev in events.Where(e => e.Doc is not null && e.JsonAtEvent is not null))
    {
        total++;
        var later = TryJ(ev.Doc!);
        if (later.StartsWith("<THREW")) { threw++; if (examples.Count < 6) examples.Add($"{ev.Kind} {ev.Name} #{ev.RequestId}: {later}"); }
        else if (later == ev.JsonAtEvent) same++;
        else { differ++; if (examples.Count < 6) examples.Add($"{ev.Kind} {ev.Name} #{ev.RequestId}: JSON differs from the one read during the event"); }
    }
    P($"   {total} documents held: {same} read back identical, {differ} differ, {threw} threw");
    foreach (var x in examples) P($"   e.g. {x}");
    var atReply = events.Where(e => e.StartedCommandAtReply is not null).ToList();
    P($"   started e.Command re-read inside the succeeded/failed handler: {atReply.Count(e => !e.StartedCommandAtReply!.StartsWith("<THREW"))} readable, {atReply.Count(e => e.StartedCommandAtReply!.StartsWith("<THREW"))} threw");
    foreach (var e in atReply.Where(e => e.StartedCommandAtReply!.StartsWith("<THREW")).Take(3)) P($"   e.g. {e.Name} #{e.RequestId}: {e.StartedCommandAtReply}");
    P();
}

// 23. Kronikol's deferred findAndModify (a claim by filter with no scenario resolved), at Raw: it holds e.Command
// from the started event and writes command.ToString() when the reply names the document.
Op("Kronikol deferred findAndModify: claim by filter with no scenario (fetcher throws), Raw and Detailed", () =>
{
    loans.InsertOne(new BsonDocument { { "_id", "claim-1" }, { "status", "queued" }, { "principal", new BsonDecimal128(Decimal128.Parse("42.00")) } });
    fetchOn = false;
    try
    {
        var claimed = loans.FindOneAndUpdate(Builders<BsonDocument>.Filter.Eq("status", "queued"),
            Builders<BsonDocument>.Update.Set("status", "claimed"), new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After });
        P($"   FindOneAndUpdate returned {(claimed is null ? "null" : claimed["_id"].ToString())}");
    }
    catch (Exception ex)
    {
        P($"   API threw {ex.GetType().FullName}: {ex.Message.Replace("\n", " ")}");
        foreach (var line in (ex.StackTrace ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Contains("Kronikol") || l.Contains("Events") || l.Contains("EventHelper")).Take(10))
            P($"     {line}");
    }
    finally { fetchOn = true; }
});

// 24. directConnection=true: which fields change when the client talks to the one server directly
var direct = NewClient(url.Replace("replicaSet=rs0", "directConnection=true"));
var directLoans = direct.GetDatabase("kronikol168").GetCollection<BsonDocument>("loans");
Op("directConnection=true: Find", () => directLoans.Find(Builders<BsonDocument>.Filter.Eq("loanNumber", "L-1001")).ToList());
Op("directConnection=true: InsertOne", () => directLoans.InsertOne(new BsonDocument { { "loanNumber", "L-D001" }, { "status", "Direct" } }));

// 24b. Client-level settings that add fields to commands: write concern, read concern, read preference, Stable API.
var tuned = NewClient(url + "&w=majority&readConcernLevel=majority&readPreference=primaryPreferred&appName=loans-api",
    settings => settings.ServerApi = new ServerApi(ServerApiVersion.V1));
var tunedLoans = tuned.GetDatabase("kronikol168").GetCollection<BsonDocument>("loans");
Op("w=majority, readConcernLevel=majority, readPreference=primaryPreferred, ServerApi V1: InsertOne", () =>
    tunedLoans.InsertOne(new BsonDocument { { "loanNumber", "L-T001" }, { "status", "Tuned" } }));
Op("w=majority, readConcernLevel=majority, readPreference=primaryPreferred, ServerApi V1: Find with MaxTime and Comment", () =>
    tunedLoans.Find(Builders<BsonDocument>.Filter.Eq("loanNumber", "L-T001"), new FindOptions { MaxTime = TimeSpan.FromSeconds(5), Comment = "loan lookup" }).ToList());
Op("w=majority, ... ServerApi V1: FindOneAndUpdate", () =>
    tunedLoans.FindOneAndUpdate(Builders<BsonDocument>.Filter.Eq("loanNumber", "L-T001"), Builders<BsonDocument>.Update.Set("status", "Tuned2")));
Op("w=majority, ... ServerApi V1: transaction with snapshot read concern and majority write concern", () =>
{
    using var session = tuned.StartSession();
    session.StartTransaction(new TransactionOptions(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority));
    tunedLoans.UpdateOne(session, Builders<BsonDocument>.Filter.Eq("loanNumber", "L-T001"), Builders<BsonDocument>.Update.Set("status", "InTxn"));
    session.CommitTransaction();
});

// 25. Field summary
P("## Field summary: top-level fields per command name (first-seen order), and per reply");
var byName = new Dictionary<string, (List<string> cmd, List<string> reply, int n)>();
foreach (var ev in events)
{
    if (!byName.TryGetValue(ev.Name, out var t)) t = (new List<string>(), new List<string>(), 0);
    var target = ev.Kind == "started" ? t.cmd : t.reply;
    foreach (var n in ev.Names) if (!target.Contains(n)) target.Add(n);
    byName[ev.Name] = (t.cmd, t.reply, t.n + (ev.Kind == "started" ? 1 : 0));
}
foreach (var (name, t) in byName.OrderBy(k => k.Key))
{
    P($"- {name} (x{t.n})");
    P($"    command: {string.Join(", ", t.cmd)}");
    P($"    reply:   {string.Join(", ", t.reply)}");
}
P();
#if DRIVER3
DrainSilently();
client.Dispose();
direct.Dispose();
P("## client.Dispose() and direct.Dispose()");
foreach (var ev in events.Skip(cursorIndex))
    P($"   {ev.Kind} {ev.Name} #{ev.RequestId} fields: {string.Join(", ", ev.Names)}");
if (events.Count == cursorIndex) P("   (no command events)");
P();
#endif

// ============================================================================================================

void DrainSilently()
{
    cursorIndex = events.Count;
    logIndex = RequestResponseLogger.RequestAndResponseLogs.Length;
}

void Op(string title, Action action, Action? after = null, bool compact = false, bool compactDocs = false)
{
    DrainSilently();
    P($"## {title}");
    try { action(); }
    catch (Exception ex) { P($"   UNEXPECTED {ex.GetType().FullName}: {ex.Message}"); }
    after?.Invoke();
    var mine = events.Skip(cursorIndex).ToList();
    if (compact)
    {
        foreach (var ev in mine)
            P($"   {ev.Kind} {ev.Name} #{ev.RequestId}{(ev.Failure is null ? "" : " " + ev.Failure)}");
        P();
        DrainSilently();
        return;
    }
    foreach (var ev in mine)
    {
        if (ev.Kind == "started")
        {
            P($">> CommandStartedEvent name={ev.Name} db={ev.Db} requestId={ev.RequestId} thread={ev.Thread}");
            P($"   fields: {string.Join(", ", ev.Names)}");
            P($"   types: {ev.TypeInfo}");
        }
        else if (ev.Kind == "succeeded")
        {
            P($"<< CommandSucceededEvent name={ev.Name} requestId={ev.RequestId} ({ev.DurationMs:F1} ms)");
            P($"   fields: {string.Join(", ", ev.Names)}");
            P($"   types: {ev.TypeInfo}");
        }
        else
        {
            P($"<< CommandFailedEvent name={ev.Name} requestId={ev.RequestId} ({ev.DurationMs:F1} ms) failure={ev.Failure}");
            if (ev.Doc is not null) P($"   result fields (MongoCommandException.Result): {string.Join(", ", ev.Names)}");
        }
        if (ev.JsonAtEvent is not null)
        {
            var text = compactDocs ? Shorten(ev.JsonAtEvent, 1500) : ev.JsonAtEvent;
            P(Indent(text));
        }
    }
    // What Kronikol 4.18.1 logged for this operation.
    var logs = RequestResponseLogger.RequestAndResponseLogs.Skip(logIndex).ToList();
    foreach (var log in logs)
    {
        var verbosity = log.ServiceName.EndsWith("Raw") ? "Raw" : "Detailed";
        var status = log.StatusCode is null ? "" : $" status={log.StatusCode.Value}";
        P($"-- Kronikol {verbosity} {log.Type}: label=\"{log.Method.Value}\" uri={log.Uri}{status} attribution={log.AttributionSource}");
        P(log.Content is null ? "   content: null" : Indent($"content ({log.Content.Length} chars):\n" + (compactDocs ? Shorten(Truncate(log.Content), 1500) : Truncate(log.Content))));
    }
    if (logs.Count == 0) P("-- Kronikol logged nothing for this operation");
    P();
    DrainSilently();
}

static string J(BsonValue v)
{
    var s = new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson, Indent = true, IndentChars = "  ", NewLineChars = "\n" };
#if DRIVER2
    s.GuidRepresentation = GuidRepresentation.Unspecified;
#endif
    return Truncate(v.ToJson(s));
}

static string TryJ(BsonValue? v)
{
    if (v is null) return "null";
    try { return J(v); }
    catch (Exception ex) { return $"<THREW {ex.GetType().FullName}: {ex.Message}>"; }
}

static string Truncate(string json) =>
    Regex.Replace(json, "(\"base64\" : \"|new BinData\\(\\d+, \"|BinData\\(\\d+, \")([A-Za-z0-9+/=]{120,})", m =>
        m.Groups[1].Value + m.Groups[2].Value[..40] + $"...[{m.Groups[2].Value.Length} base64 chars]");

static string Shorten(string text, int max) => text.Length <= max ? text : text[..max] + $"\n... [{text.Length} chars in all]";

static string Indent(string text) => "   " + text.Replace("\n", "\n   ");

static IEnumerable<string> SafeNames(BsonValue? v)
{
    if (v is not BsonDocument d) return Array.Empty<string>();
    try { return d.Names.ToList(); }
    catch (Exception ex) { return new[] { $"<THREW {ex.GetType().Name}>" }; }
}

static string Describe(BsonValue? v)
{
    if (v is null) return "null";
    try
    {
        var sb = new StringBuilder(v.GetType().Name);
        if (v is BsonDocument d)
            foreach (var el in d)
                if (el.Value is BsonArray a)
                    sb.Append($"; {el.Name}: {a.GetType().Name}[{a.Count}]" + (a.Count > 0 ? $" of {a[0].GetType().Name}" : ""));
                else if (el.Value is BsonDocument sub && el.Name is "cursor" or "value" or "lastErrorObject")
                    sb.Append($"; {el.Name}: {sub.GetType().Name}" + string.Concat(sub.Where(x => x.Value is BsonArray).Select(x => $" ({x.Name}: {x.Value.GetType().Name}[{x.Value.AsBsonArray.Count}]{(x.Value.AsBsonArray.Count > 0 ? " of " + x.Value.AsBsonArray[0].GetType().Name : "")})")));
        return sb.ToString();
    }
    catch (Exception ex) { return $"<THREW {ex.GetType().Name}: {ex.Message}>"; }
}

class Ev(string kind, string name, int requestId, string? db)
{
    public string Kind { get; } = kind;
    public string Name { get; } = name;
    public int RequestId { get; } = requestId;
    public string? Db { get; } = db;
    public BsonDocument? Doc { get; set; }
    public string? JsonAtEvent { get; set; }
    public string? TypeInfo { get; set; }
    public string? Failure { get; set; }
    public double DurationMs { get; set; }
    public int Thread { get; set; }
    public string? StartedCommandAtReply { get; set; }
    public List<string> Names { get; set; } = new();
}

class LoanPoco
{
    public ObjectId Id { get; set; }
    public string LoanNumber { get; set; } = "";
    public decimal Principal { get; set; }
    public Guid LoanId { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<string> Tags { get; set; } = new();
    public AddressPoco? Address { get; set; }
}

class AddressPoco { public string City { get; set; } = ""; }

class PlainPoco
{
    public ObjectId Id { get; set; }
    public string LoanNumber { get; set; } = "";
    public decimal Principal { get; set; }
    public DateTime CreatedAt { get; set; }
}

class LoanPocoAttributed
{
    public ObjectId Id { get; set; }
    public string LoanNumber { get; set; } = "";
    [BsonRepresentation(BsonType.Decimal128)] public decimal Principal { get; set; }
    [BsonGuidRepresentation(GuidRepresentation.Standard)] public Guid LoanId { get; set; }
    public DateTime CreatedAt { get; set; }
}
