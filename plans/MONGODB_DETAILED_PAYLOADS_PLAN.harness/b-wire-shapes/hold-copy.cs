#:package MongoDB.Driver@3.12.0
#:property NoWarn=$(NoWarn);NU1902;NU1903;CS0618
#:property Nullable=enable
// Which copy of an event's document, taken inside the handler, can still be read after the handler returns?
// The driver hands out RawBsonDocuments over the message buffer and disposes them when the handler returns
// (wire-shapes results: 116 of 116 held documents threw ObjectDisposedException). run-hold-copy.sh rewrites line 1.
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;

var url = Environment.GetEnvironmentVariable("MONGO_URL") ?? "mongodb://127.0.0.1:27217/?replicaSet=rs0";
Console.WriteLine($"MongoDB.Driver {typeof(MongoClient).Assembly.GetName().Version}");
var held = new List<(string What, BsonDocument Doc)>();
var settings = MongoClientSettings.FromConnectionString(url);
settings.ClusterConfigurator = cb =>
{
    cb.Subscribe<CommandStartedEvent>(e => { if (e.CommandName is "insert" or "findAndModify") Hold($"{e.CommandName} command", e.Command); });
    cb.Subscribe<CommandSucceededEvent>(e => { if (e.CommandName is "insert" or "findAndModify") Hold($"{e.CommandName} reply", e.Reply); });
};
void Hold(string what, BsonDocument doc)
{
    held.Add(($"{what}: the event's own document ({doc.GetType().Name})", doc));
    held.Add(($"{what}: DeepClone()", (BsonDocument)doc.DeepClone()));
    held.Add(($"{what}: Clone()", (BsonDocument)doc.Clone()));
    held.Add(($"{what}: new BsonDocument(doc) (top level copied)", new BsonDocument(doc)));
    held.Add(($"{what}: BsonSerializer.Deserialize<BsonDocument>(doc.ToBson())", BsonSerializer.Deserialize<BsonDocument>(doc.ToBson())));
}

var client = new MongoClient(settings);
var coll = client.GetDatabase("kronikol168").GetCollection<BsonDocument>("holdcopy");
coll.DeleteMany(FilterDefinition<BsonDocument>.Empty);
coll.InsertOne(new BsonDocument { { "loanNumber", "H-1" }, { "principal", new BsonDecimal128(Decimal128.Parse("10.50")) }, { "tags", new BsonArray { "a" } } });
coll.FindOneAndUpdate(Builders<BsonDocument>.Filter.Eq("loanNumber", "H-1"), Builders<BsonDocument>.Update.Set("status", "x"),
    new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After });

foreach (var (what, doc) in held)
{
    string outcome;
    try { var json = doc.ToJson(); outcome = $"readable ({json.Length} chars)"; }
    catch (Exception ex) { outcome = $"THREW {ex.GetType().Name}"; }
    Console.WriteLine($"{what,-95} -> {outcome}");
}
