#:package MongoDB.Driver@3.12.0
#:package Kronikol.Extensions.MongoDB@4.18.1
#:property DefineConstants=$(DefineConstants);DRIVER3
#:property NoWarn=$(NoWarn);NU1902;NU1903;CS0618
#:property Nullable=enable
// Kronikol's deferred findAndModify (MongoDbTrackingSubscriber keeps e.Command from CommandStartedEvent and reads it in
// OnCommandSucceeded) against a real server, one Kronikol subscriber per client so the outcome is attributable.
// The scenario: a scenario inserts {_id: "claim-N", status: "queued"} (AutoCorrelateWrites records the owner); then
// code with no scenario (the fetcher throws, as a background worker's would) claims it by filter with FindOneAndUpdate.
// run-deferred-claim.sh replaces the first five lines for driver 2.30.0.
using Kronikol.Extensions.MongoDB;
using Kronikol.Tracking;
using MongoDB.Bson;
using MongoDB.Driver;

var url = Environment.GetEnvironmentVariable("MONGO_URL") ?? "mongodb://127.0.0.1:27217/?replicaSet=rs0";
Console.WriteLine($"MongoDB.Driver {typeof(MongoClient).Assembly.GetName().Version}, {typeof(MongoDbTrackingSubscriber).Assembly.GetName().Name} 4.18.1, {url}");
var fetchOn = true;
(string Name, string Id) Fetch() => fetchOn ? ("harness", "h1") : throw new InvalidOperationException("no test is running");

new MongoClient(url).GetDatabase("kronikol168").DropCollection("claims");
var n = 0;
foreach (var (label, verbosity, byOwner) in new[]
{
    ("Raw, AttributeByDocumentOwner = true (the default)", MongoDbTrackingVerbosity.Raw, true),
    ("Detailed, AttributeByDocumentOwner = true (the default)", MongoDbTrackingVerbosity.Detailed, true),
    ("Raw, AttributeByDocumentOwner = false", MongoDbTrackingVerbosity.Raw, false),
})
{
    n++;
    Console.WriteLine();
    Console.WriteLine($"## {label}");
    var subscriber = new MongoDbTrackingSubscriber(new MongoDbTrackingOptions
    {
        ServiceName = $"MongoDB-{n}", Verbosity = verbosity, CurrentTestInfoFetcher = Fetch, AttributeByDocumentOwner = byOwner
    });
    var settings = MongoClientSettings.FromConnectionString(url);
    settings.ClusterConfigurator = cb => subscriber.Subscribe(cb);
    var client = new MongoClient(settings);
    var coll = client.GetDatabase("kronikol168").GetCollection<BsonDocument>("claims");
    var before = RequestResponseLogger.RequestAndResponseLogs.Length;

    fetchOn = true;
    coll.InsertOne(new BsonDocument { { "_id", $"claim-{n}" }, { "status", $"queued-{n}" } });
    fetchOn = false;
    try
    {
        var claimed = coll.FindOneAndUpdate(Builders<BsonDocument>.Filter.Eq("status", $"queued-{n}"),
            Builders<BsonDocument>.Update.Set("status", "claimed"), new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After });
        Console.WriteLine($"FindOneAndUpdate returned {claimed?["_id"]}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FindOneAndUpdate THREW: {ex}");
    }
    finally { fetchOn = true; }

    try
    {
        var stored = coll.Find(Builders<BsonDocument>.Filter.Eq("_id", $"claim-{n}")).First();
        Console.WriteLine($"the document on the server afterwards: status = {stored["status"]}");
        var count = coll.CountDocuments(FilterDefinition<BsonDocument>.Empty);
        Console.WriteLine($"next command on the same client: CountDocuments = {count}");
    }
    catch (Exception ex) { Console.WriteLine($"next command on the same client THREW {ex.GetType().FullName}: {ex.Message}"); }

    foreach (var log in RequestResponseLogger.RequestAndResponseLogs.Skip(before))
        Console.WriteLine($"   logged {log.Type} \"{log.Method.Value}\" status={log.StatusCode?.Value} attribution={log.AttributionSource} content={(log.Content is null ? "null" : $"{log.Content.Length} chars")}");
}
