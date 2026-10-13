using System.Net;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using Kronikol.Extensions.MongoDB;
using Kronikol.Tracking;

namespace Kronikol.Tests.MongoDB;

/// <summary>
/// The note and failure rules against a real server and the real driver (plans/MONGODB_DETAILED_PAYLOADS_PLAN.md, R1):
/// what the driver's events hold, and that it disposes them when a handler returns, only a real driver shows. The V2
/// project links this file, so each fact runs on driver 2.x and 3.x.
/// </summary>
[Collection("TestCorrelationStore")]
public class MongoDbRealServerTests(MongoServerFixture mongo) : IClassFixture<MongoServerFixture>
{
    private readonly string _testId = Guid.NewGuid().ToString();
    private readonly string _database = "kronikol_r1_" + Guid.NewGuid().ToString("N")[..10];

    private IMongoDatabase Database(MongoDbTrackingVerbosity verbosity, bool trackGetMore = false)
    {
        var subscriber = new MongoDbTrackingSubscriber(new MongoDbTrackingOptions
        {
            ServiceName = "MongoDB",
            CallerName = "LoanService",
            Verbosity = verbosity,
            TrackGetMore = trackGetMore,
            CurrentTestInfoFetcher = () => ("Real server", _testId),
        });
        var settings = MongoClientSettings.FromConnectionString(mongo.Require());
        settings.ClusterConfigurator = cluster => subscriber.Subscribe(cluster);
        return new MongoClient(settings).GetDatabase(_database);
    }

    private RequestResponseLog[] Logs() =>
        RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private static BsonDocument Loan(string number, string status) => new()
    {
        { "loanNumber", number },
        { "status", status },
        { "principal", Decimal128.Parse("1000.50") },
        { "openedAt", new BsonDateTime(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)) },
        { "deletedAt", BsonNull.Value },
    };

    [Fact]
    public async Task A_raw_claim_by_filter_does_not_fail_the_applications_call()
    {
        // A claim by filter that resolves no scenario waits for its reply to name the document it took; at Raw its
        // request note was read from the command after the driver had disposed it, and the application's own
        // FindOneAndUpdate threw, after the server had applied the update.
        var loans = Database(MongoDbTrackingVerbosity.Raw).GetCollection<BsonDocument>("loans");
        await loans.InsertOneAsync(Loan("L-1001", "Ready"), cancellationToken: TestContext.Current.CancellationToken);

        BsonDocument? claimed;
        using (TestIdentityScope.Detach())
        {
            claimed = await loans.FindOneAndUpdateAsync(
                Builders<BsonDocument>.Filter.Eq("status", "Ready"),
                Builders<BsonDocument>.Update.Set("status", "claimed"),
                new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After },
                TestContext.Current.CancellationToken);
        }

        await loans.Database.Client.DropDatabaseAsync(_database, TestContext.Current.CancellationToken);

        Assert.Equal("claimed", claimed?["status"].AsString);
        var claim = Assert.Single(Logs(), l => l.Type == RequestResponseType.Request && l.Method.Value?.ToString()?.StartsWith("FindAndModify") == true);
        Assert.Contains("\"findAndModify\" : \"loans\"", claim.Content);
        Assert.Contains("\"status\" : \"Ready\"", claim.Content);
    }

    [Fact]
    public async Task A_duplicate_key_insert_is_drawn_failed()
    {
        var loans = Database(MongoDbTrackingVerbosity.Detailed).GetCollection<BsonDocument>("loans");
        await loans.Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("loanNumber"), new CreateIndexOptions { Unique = true }),
            cancellationToken: TestContext.Current.CancellationToken);
        await loans.InsertOneAsync(Loan("L-1001", "Ready"), cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<MongoWriteException>(() =>
            loans.InsertOneAsync(Loan("L-1001", "Draft"), cancellationToken: TestContext.Current.CancellationToken));

        await loans.Database.Client.DropDatabaseAsync(_database, TestContext.Current.CancellationToken);

        var inserts = Logs().Where(l => l.Type == RequestResponseType.Response && l.Method.Value?.ToString() == "Insert → loans").ToArray();
        Assert.Equal(2, inserts.Length);
        Assert.Equal(HttpStatusCode.OK, inserts[0].StatusCode?.Value);
        Assert.Equal(HttpStatusCode.InternalServerError, inserts[1].StatusCode?.Value);
        Assert.StartsWith("E11000 duplicate key error", inserts[1].Error);
        Assert.StartsWith("{ \"n\" : 0, \"writeErrors\" : [{ \"index\" : 0, \"code\" : 11000, ", inserts[1].Content);
    }

    [Fact]
    public async Task Every_detailed_reply_note_parses_as_json()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = Database(MongoDbTrackingVerbosity.Detailed, trackGetMore: true);
        var loans = database.GetCollection<BsonDocument>("loans");
        var filter = Builders<BsonDocument>.Filter;
        var update = Builders<BsonDocument>.Update;

        await loans.InsertOneAsync(Loan("L-1", "Ready"), cancellationToken: ct);
        await loans.InsertManyAsync([Loan("L-2", "Ready"), Loan("L-3", "Draft"), Loan("L-4", "Draft")], cancellationToken: ct);
        await (await loans.FindAsync(filter.Eq("status", "Ready"), cancellationToken: ct)).ToListAsync(ct);
        await loans.UpdateOneAsync(filter.Eq("loanNumber", "L-1"), update.Set("status", "Open"), cancellationToken: ct);
        await loans.UpdateManyAsync(filter.Eq("status", "Draft"), update.Set("reviewed", true), cancellationToken: ct);
        await loans.ReplaceOneAsync(filter.Eq("loanNumber", "L-2"), Loan("L-2", "Replaced"), cancellationToken: ct);
        await loans.UpdateOneAsync(filter.Eq("loanNumber", "L-9"), update.Set("status", "Upserted"), new UpdateOptions { IsUpsert = true }, ct);
        await loans.FindOneAndUpdateAsync(filter.Eq("loanNumber", "L-3"), update.Set("status", "Claimed"), cancellationToken: ct);
        await loans.FindOneAndReplaceAsync(filter.Eq("loanNumber", "L-4"), Loan("L-4", "Replaced"), cancellationToken: ct);
        await loans.FindOneAndUpdateAsync(filter.Eq("loanNumber", "L-404"), update.Set("status", "Nothing"), cancellationToken: ct);
        await loans.FindOneAndDeleteAsync(filter.Eq("loanNumber", "L-9"), cancellationToken: ct);
        await (await loans.FindAsync(filter.Empty, new FindOptions<BsonDocument> { BatchSize = 1, Limit = 3 }, ct)).ToListAsync(ct);
        await (await loans.FindAsync(filter.Eq("status", "Gone"), cancellationToken: ct)).ToListAsync(ct);
        await loans.CountDocumentsAsync(filter.Empty, cancellationToken: ct);
        await loans.EstimatedDocumentCountAsync(cancellationToken: ct);
        await (await loans.DistinctAsync<string>("status", filter.Empty, cancellationToken: ct)).ToListAsync(ct);
        await loans.Aggregate().Match(filter.Ne("status", "Gone")).Group(new BsonDocument { { "_id", "$status" }, { "n", new BsonDocument("$sum", 1) } }).ToListAsync(ct);
        await loans.BulkWriteAsync(
        [
            new InsertOneModel<BsonDocument>(Loan("L-5", "Ready")),
            new UpdateOneModel<BsonDocument>(filter.Eq("loanNumber", "L-5"), update.Set("status", "Open")),
            new DeleteOneModel<BsonDocument>(filter.Eq("loanNumber", "L-5")),
        ], cancellationToken: ct);
        using (var session = await database.Client.StartSessionAsync(cancellationToken: ct))
        {
            session.StartTransaction();
            await loans.InsertOneAsync(session, Loan("L-6", "Ready"), cancellationToken: ct);
            await loans.UpdateOneAsync(session, filter.Eq("loanNumber", "L-6"), update.Set("status", "Open"), cancellationToken: ct);
            await session.CommitTransactionAsync(ct);
        }
        await loans.DeleteManyAsync(filter.Empty, cancellationToken: ct);
        await database.Client.DropDatabaseAsync(_database, ct);

        var responses = Logs().Where(l => l.Type == RequestResponseType.Response).ToArray();
        var notes = responses.Where(l => l.Content is not null).ToArray();
        Assert.All(notes, response =>
        {
            var content = response.Content!;
            var footnote = content.IndexOf("\n... (", StringComparison.Ordinal);
            using var _ = JsonDocument.Parse(footnote < 0 ? content : content[..footnote]);
        });

        // Not vacuous: every kind of reply the rules tell apart was drawn with a note.
        string? Note(string label) => responses.FirstOrDefault(l => l.Method.Value?.ToString()?.StartsWith(label) == true)?.Content;
        Assert.Contains("\"upserted\" : [{ \"index\" : 0, \"_id\" : { \"$oid\" : ", responses.Select(l => l.Content).OfType<string>().First(c => c.Contains("upserted")));
        Assert.StartsWith("{ \"lastErrorObject\" : ", Note("FindAndModify ↔ loans"));
        Assert.StartsWith("{ \"values\" : [", Note("Distinct ← loans"));
        Assert.StartsWith("[", Note("GetMore ← loans"));
        Assert.Contains(responses, l => l.Content == "[]");
        Assert.Contains("\"principal\" : { \"$numberDecimal\" : \"1000.50\" }", Note("Find ← loans"));
        Assert.Contains("\"openedAt\" : { \"$date\" : \"2024-01-01T00:00:00Z\" }", Note("Find ← loans"));
        Assert.Contains(responses, l => l.Method.Value?.ToString() == "CommitTransaction" && l.Content is null);
        Assert.True(notes.Length >= 20, $"{notes.Length} reply notes");
    }
}
