using System.Net;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Events;
using MongoDB.Driver.Core.Servers;
using Kronikol.Extensions.MongoDB;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.MongoDB;

/// <summary>
/// <c>Failures.md</c> prints why a call in the failing step failed when the call's <c>Error</c> says so; no MongoDB
/// capture set it, so a failed command was listed with its status alone (plans/MONGODB_DETAILED_PAYLOADS_PLAN.md, F24).
/// </summary>
[Collection("TestCorrelationStore")]
public class MongoDbFailuresDigestTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private MongoDbTrackingSubscriber Subscriber(MongoDbTrackingVerbosity verbosity = MongoDbTrackingVerbosity.Detailed) => new(new MongoDbTrackingOptions
    {
        ServiceName = "MongoDB",
        CallerName = "LoanService",
        AttributeByDocumentOwner = false,
        AutoCorrelateWrites = false,
        Verbosity = verbosity,
        CurrentTestInfoFetcher = () => ("Open a loan", _testId),
    });

    private static ConnectionId MakeConnectionId() =>
        new(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 27017)));

    private string Digest() => DigestOf().Markdown;

    private FailuresDigest DigestOf()
    {
        var step = new RequestResponseLog(_testId, _testId, "", "", new Uri("http://override.com"), [], "", "",
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        {
            IsOverrideStart = true,
            MarkerKind = DiagramMarkerKind.Step,
            PlantUml = "hnote across <<stepDelimiter>> #black:<color:white>the loan is opened",
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-1),
        };
        var calls = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Loans",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = _testId, DisplayName = "Open a loan", Result = ExecutionResult.Failed, ErrorMessage = "the loan was not opened",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "When", Text = "the loan is opened", Status = ExecutionResult.Failed,
                                FailureMessage = "the loan was not opened",
                            },
                        ],
                    },
                ],
            },
        };
        return FailuresDigestGenerator.Generate(features, [step, .. calls], "TestRunReport", "4.19.0");
    }

    [Fact]
    public void Failures_md_caps_a_raw_label_as_it_caps_a_path()
    {
        // At Raw a MongoDB label carries the whole filter; listed by label since 4.20.0, a find over 300 ids put every
        // one of them in the Call column and in Failures.jsonl's summary.
        var subscriber = Subscriber(MongoDbTrackingVerbosity.Raw);
        var ids = new BsonArray(Enumerable.Range(1, 300).Select(i => $"loan-{i:D6}"));
        subscriber.OnCommandStarted(new CommandStartedEvent("find",
            new BsonDocument { { "find", "loans" }, { "filter", new BsonDocument("_id", new BsonDocument("$in", ids)) } },
            new DatabaseNamespace("app"), 1L, 1, MakeConnectionId()));

        var digest = DigestOf();

        var row = Assert.Single(digest.Markdown.Split('\n'), line => line.Contains("Find app.loans", StringComparison.Ordinal));
        Assert.DoesNotContain("loan-000300", row);
        Assert.True(row.Length < 400, $"the row is {row.Length} characters");
        Assert.DoesNotContain("loan-000300", digest.Jsonl);
    }

    [Fact]
    public void Failures_jsonl_says_why_a_call_failed_as_Failures_md_does()
    {
        var subscriber = Subscriber();
        subscriber.OnCommandStarted(new CommandStartedEvent("insert",
            new BsonDocument { { "insert", "loans" }, { "documents", new BsonArray { new BsonDocument("loanNumber", "L-1001") } } },
            new DatabaseNamespace("app"), 1L, 1, MakeConnectionId()));
        subscriber.OnCommandSucceeded(new CommandSucceededEvent("insert", new BsonDocument
        {
            { "n", 0 },
            { "writeErrors", new BsonArray { new BsonDocument { { "index", 0 }, { "code", 11000 }, { "errmsg", "E11000 duplicate key error collection: app.loans" } } } },
            { "ok", 1.0 },
        }, new DatabaseNamespace("app"), 1L, 1, MakeConnectionId(), TimeSpan.FromMilliseconds(2)));

        var record = JsonDocument.Parse(DigestOf().Jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries)[1]).RootElement;

        var call = Assert.Single(record.GetProperty("calls").EnumerateArray());
        Assert.Equal("InternalServerError", call.GetProperty("status").GetString());
        Assert.Equal("E11000 duplicate key error collection: app.loans", call.GetProperty("error").GetString());
    }

    [Fact]
    public void Failures_md_says_why_a_mongo_write_failed()
    {
        var subscriber = Subscriber();
        subscriber.OnCommandStarted(new CommandStartedEvent("insert",
            new BsonDocument { { "insert", "loans" }, { "documents", new BsonArray { new BsonDocument("loanNumber", "L-1001") } } },
            new DatabaseNamespace("app"), 1L, 1, MakeConnectionId()));
        subscriber.OnCommandSucceeded(new CommandSucceededEvent("insert", new BsonDocument
        {
            { "n", 0 },
            { "writeErrors", new BsonArray { new BsonDocument { { "index", 0 }, { "code", 11000 }, { "errmsg", "E11000 duplicate key error collection: app.loans" } } } },
            { "ok", 1.0 },
        }, new DatabaseNamespace("app"), 1L, 1, MakeConnectionId(), TimeSpan.FromMilliseconds(2)));

        var digest = Digest();

        Assert.Contains("E11000 duplicate key error collection: app.loans", digest);
    }

    [Fact]
    public void Failures_md_says_why_a_mongo_command_failed()
    {
        var subscriber = Subscriber();
        var command = new BsonDocument { { "find", "loans" }, { "filter", new BsonDocument("principal", new BsonDocument("$bogus", 1)) } };
        subscriber.OnCommandStarted(new CommandStartedEvent("find", command, new DatabaseNamespace("app"), 1L, 1, MakeConnectionId()));
        var result = new BsonDocument { { "ok", 0.0 }, { "errmsg", "unknown operator: $bogus" }, { "code", 2 }, { "codeName", "BadValue" } };
        subscriber.OnCommandFailed(new CommandFailedEvent("find", new DatabaseNamespace("app"),
            new MongoCommandException(MakeConnectionId(), "find command failed", command, result), 1L, 1, MakeConnectionId(), TimeSpan.FromMilliseconds(2)));

        var digest = Digest();

        Assert.Contains("unknown operator: $bogus", digest);
    }

    [Fact]
    public void Failures_md_lists_a_MongoDB_call_by_its_label()
    {
        // A MongoDB note is what the command sent, documents included (#168 R2), so the digest lists the call by its
        // label and path, as it lists an HTTP call, and never quotes the note (F23).
        var subscriber = Subscriber();
        var documents = new BsonArray
        {
            new BsonDocument("loanNumber", "L-1001"), new BsonDocument("loanNumber", "L-1002"), new BsonDocument("loanNumber", "L-1003"),
        };
        subscriber.OnCommandStarted(new CommandStartedEvent("insert",
            new BsonDocument { { "insert", "loans" }, { "documents", documents } }, new DatabaseNamespace("app"), 1L, 1, MakeConnectionId()));
        subscriber.OnCommandSucceeded(new CommandSucceededEvent("insert", new BsonDocument { { "n", 3 }, { "ok", 1.0 } },
            new DatabaseNamespace("app"), 1L, 1, MakeConnectionId(), TimeSpan.FromMilliseconds(2)));
        subscriber.OnCommandStarted(new CommandStartedEvent("find",
            new BsonDocument { { "find", "loans" }, { "filter", new BsonDocument("loanNumber", "L-1001") } }, new DatabaseNamespace("app"), 1L, 2, MakeConnectionId()));

        var digest = Digest();

        Assert.Contains("Insert (×3) → loans /app/loans", digest);
        Assert.Contains("Find ← loans /app/loans", digest);
        Assert.DoesNotContain("L-1001", digest);
    }
}
