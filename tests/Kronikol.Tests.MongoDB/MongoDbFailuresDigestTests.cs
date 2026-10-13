using System.Net;
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

    private MongoDbTrackingSubscriber Subscriber() => new(new MongoDbTrackingOptions
    {
        ServiceName = "MongoDB",
        CallerName = "LoanService",
        AttributeByDocumentOwner = false,
        AutoCorrelateWrites = false,
        CurrentTestInfoFetcher = () => ("Open a loan", _testId),
    });

    private static ConnectionId MakeConnectionId() =>
        new(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 27017)));

    private string Digest()
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
        return FailuresDigestGenerator.Generate(features, [step, .. calls], "TestRunReport", "4.19.0").Markdown;
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
}
