using System.Net;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Events;
using MongoDB.Driver.Core.Servers;
using Kronikol.Extensions.MongoDB;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.MongoDB;

/// <summary>Facts that set a process-wide static every other test's capture reads, so they run alone.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessWideStatics
{
    public const string Name = "MongoDB process-wide statics";
}

/// <summary>
/// What users' capture-time hooks see of a MongoDB note, and what happens when one throws
/// (plans/MONGODB_DETAILED_PAYLOADS_PLAN.md, F6 and F20).
/// </summary>
[Collection(ProcessWideStatics.Name)]
public class MongoDbProcessWideTests
{
    private const string DiagnosticKey = "mongodb-subscriber-failed";
    private readonly string _testId = Guid.NewGuid().ToString();

    private MongoDbTrackingOptions MakeOptions() => new()
    {
        ServiceName = "MongoDB",
        CallerName = "MyService",
        CurrentTestInfoFetcher = () => ("My Test", _testId),
    };

    private static ConnectionId MakeConnectionId() =>
        new(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 27017)));

    private RequestResponseLog[] Logs() =>
        RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private static BsonDocument FindReply(params BsonDocument[] documents) => new()
    {
        { "cursor", new BsonDocument { { "firstBatch", new BsonArray(documents) }, { "id", 0L }, { "ns", "app.users" } } },
        { "ok", 1.0 },
    };

    [Fact]
    public void No_handler_exception_reaches_the_driver()
    {
        // The driver runs a subscriber inside the application's call: a hook that throws must not fail that call.
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());
        var command = new BsonDocument { { "find", "users" }, { "filter", new BsonDocument("status", "active") } };
        var previous = RequestResponseLogger.Redaction;
        ProcessDiagnostics.Remove(DiagnosticKey);
        RequestResponseLogger.Redaction = new CaptureRedaction { Custom = _ => throw new InvalidOperationException("a redaction hook failed") };
        try
        {
            var started = Record.Exception(() => subscriber.OnCommandStarted(new CommandStartedEvent("find", command, new DatabaseNamespace("app"), 1L, 1, MakeConnectionId())));
            // Two commands start with the hook off, so their replies have a request to answer when it throws again.
            RequestResponseLogger.Redaction = previous;
            subscriber.OnCommandStarted(new CommandStartedEvent("find", command, new DatabaseNamespace("app"), 1L, 2, MakeConnectionId()));
            subscriber.OnCommandStarted(new CommandStartedEvent("find", command, new DatabaseNamespace("app"), 1L, 3, MakeConnectionId()));
            RequestResponseLogger.Redaction = new CaptureRedaction { Custom = _ => throw new InvalidOperationException("a redaction hook failed") };
            var succeeded = Record.Exception(() => subscriber.OnCommandSucceeded(
                new CommandSucceededEvent("find", FindReply(), new DatabaseNamespace("app"), 1L, 2, MakeConnectionId(), TimeSpan.FromMilliseconds(1))));
            var failed = Record.Exception(() => subscriber.OnCommandFailed(
                new CommandFailedEvent("find", new DatabaseNamespace("app"), new Exception("find command failed"), 1L, 3, MakeConnectionId(), TimeSpan.FromMilliseconds(1))));

            Assert.Null(started);
            Assert.Null(succeeded);
            Assert.Null(failed);
            var diagnostic = Assert.Single(ProcessDiagnostics.Entries, entry => entry.Message.StartsWith("MongoDbTrackingSubscriber (MongoDB) could not record a command", StringComparison.Ordinal));
            Assert.Equal(DiagnosticKind.CaptureDegraded, diagnostic.Kind);
            Assert.Contains("InvalidOperationException: a redaction hook failed", diagnostic.Message);
        }
        finally
        {
            RequestResponseLogger.Redaction = previous;
            ProcessDiagnostics.Remove(DiagnosticKey);
        }
    }

    [Fact]
    public void A_command_whose_request_could_not_be_logged_leaves_no_reply_behind()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());
        var command = new BsonDocument { { "find", "users" }, { "filter", new BsonDocument("status", "active") } };
        var previous = RequestResponseLogger.Redaction;
        RequestResponseLogger.Redaction = new CaptureRedaction { Custom = _ => throw new InvalidOperationException("a redaction hook failed") };
        try
        {
            subscriber.OnCommandStarted(new CommandStartedEvent("find", command, new DatabaseNamespace("app"), 1L, 1, MakeConnectionId()));
        }
        finally
        {
            RequestResponseLogger.Redaction = previous;
            ProcessDiagnostics.Remove(DiagnosticKey);
        }

        subscriber.OnCommandSucceeded(new CommandSucceededEvent("find", FindReply(), new DatabaseNamespace("app"), 1L, 1, MakeConnectionId(), TimeSpan.FromMilliseconds(1)));

        Assert.Empty(Logs());
    }

    [Fact]
    public void A_content_pattern_written_for_todays_text_still_redacts()
    {
        // A capture-time pattern written against the driver's spacing, `"email" : "…"`, as users' patterns were.
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());
        var previous = RequestResponseLogger.Redaction;
        RequestResponseLogger.Redaction = new CaptureRedaction().RedactContent("\"email\" : \"[^\"]*\"", "\"email\" : \"[redacted]\"");
        try
        {
            subscriber.OnCommandStarted(new CommandStartedEvent("find",
                new BsonDocument { { "find", "users" }, { "filter", new BsonDocument("status", "active") } },
                new DatabaseNamespace("app"), 1L, 1, MakeConnectionId()));
            subscriber.OnCommandSucceeded(new CommandSucceededEvent("find",
                FindReply(new BsonDocument { { "_id", 1 }, { "email", "ada@example.com" }, { "status", "active" } }),
                new DatabaseNamespace("app"), 1L, 1, MakeConnectionId(), TimeSpan.FromMilliseconds(1)));
        }
        finally
        {
            RequestResponseLogger.Redaction = previous;
        }

        var response = Logs().Single(l => l.Type == RequestResponseType.Response);
        Assert.DoesNotContain("ada@example.com", response.Content);
        Assert.Contains("\"email\" : \"[redacted]\"", response.Content);
    }

    [Fact]
    public void JsonWriterSettings_Defaults_do_not_change_a_note()
    {
        // A host can set MongoDB.Bson's process-wide JsonWriterSettings.Defaults; a request note never reads it (F18).
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());
        var command = new BsonDocument
        {
            { "insert", "loans" },
            { "documents", new BsonArray { new BsonDocument { { "_id", 1 }, { "rate", 0.1 }, { "principal", new BsonDecimal128(Decimal128.Parse("5000.00")) } } } },
            { "$db", "app" },
        };
        var defaults = JsonWriterSettings.Defaults;
        try
        {
            JsonWriterSettings.Defaults = new JsonWriterSettings { OutputMode = JsonOutputMode.CanonicalExtendedJson, Indent = true };
            subscriber.OnCommandStarted(new CommandStartedEvent("insert", command, new DatabaseNamespace("app"), 1L, 1, MakeConnectionId()));
        }
        finally
        {
            JsonWriterSettings.Defaults = defaults;
        }

        var request = Assert.Single(Logs(), l => l.Type == RequestResponseType.Request);
        Assert.Equal("{ \"documents\" : [{ \"_id\" : 1, \"rate\" : 0.1, \"principal\" : { \"$numberDecimal\" : \"5000.00\" } }] }", request.Content);
    }
}
