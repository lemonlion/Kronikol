using System.Net;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Events;
using MongoDB.Driver.Core.Servers;
using Kronikol.Extensions.MongoDB;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.MongoDB;

/// <summary>
/// A write error was drawn <c>200</c>, so nothing that counts errors saw it (plans/MONGODB_DETAILED_PAYLOADS_PLAN.md,
/// F2): here <c>kronikol query services</c>, over a data file written from what the subscriber recorded.
/// </summary>
[Collection("TestCorrelationStore")]
public sealed class MongoDbQueryTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-mongo-query").FullName;
    private readonly string _testId = Guid.NewGuid().ToString();

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private static ConnectionId Connection() => new(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 27017)));

    [Fact]
    public void A_write_error_counts_as_an_error_in_query_services()
    {
        var subscriber = new MongoDbTrackingSubscriber(new MongoDbTrackingOptions
        {
            ServiceName = "MongoDB",
            CallerName = "LoanService",
            AttributeByDocumentOwner = false,
            AutoCorrelateWrites = false,
            CurrentTestInfoFetcher = () => ("Open a loan", _testId),
        });
        var database = new DatabaseNamespace("app");
        subscriber.OnCommandStarted(new CommandStartedEvent("insert",
            new BsonDocument { { "insert", "loans" }, { "documents", new BsonArray { new BsonDocument("loanNumber", "L-1001") } } }, database, 1L, 1, Connection()));
        subscriber.OnCommandSucceeded(new CommandSucceededEvent("insert", new BsonDocument
        {
            { "n", 0 },
            { "writeErrors", new BsonArray { new BsonDocument { { "index", 0 }, { "code", 11000 }, { "errmsg", "E11000 duplicate key error" } } } },
            { "ok", 1.0 },
        }, database, 1L, 1, Connection(), TimeSpan.FromMilliseconds(2)));
        subscriber.OnCommandStarted(new CommandStartedEvent("find",
            new BsonDocument { { "find", "loans" }, { "filter", new BsonDocument("loanNumber", "L-1001") } }, database, 1L, 2, Connection()));
        subscriber.OnCommandSucceeded(new CommandSucceededEvent("find", new BsonDocument
        {
            { "cursor", new BsonDocument { { "firstBatch", new BsonArray() }, { "id", 0L }, { "ns", "app.loans" } } },
            { "ok", 1.0 },
        }, database, 1L, 2, Connection(), TimeSpan.FromMilliseconds(2)));

        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Loans",
                Scenarios = [new Scenario { Id = _testId, DisplayName = "Open a loan", Result = ExecutionResult.Failed, ErrorMessage = "MongoWriteException" }]
            }
        ];
        var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();
        var written = ReportGenerator.GenerateTestRunReportData(features, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow,
            "MongoQuery_" + Guid.NewGuid().ToString("N")[..8] + ".json", DataFormat.Json, diagrams: null, logs);
        var report = Path.Combine(_directory, "TestRunReport.json");
        File.Move(written, report, overwrite: true);

        var output = new StringWriter();
        var exit = QueryCommand.Run(["services", report], output, new StringWriter());

        Assert.Equal(0, exit);
        var row = output.ToString().Split('\n').Single(line => line.StartsWith("MongoDB ", StringComparison.Ordinal));
        var columns = row.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("2", columns[1]);
        Assert.Equal("1", columns[2]);
    }
}
