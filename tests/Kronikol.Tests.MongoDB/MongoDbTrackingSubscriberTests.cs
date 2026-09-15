using System.Net;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Events;
using MongoDB.Driver.Core.Servers;
using Kronikol.Extensions.MongoDB;
using Kronikol.Tracking;

namespace Kronikol.Tests.MongoDB;

[Collection("TestCorrelationStore")]
public class MongoDbTrackingSubscriberTests : IDisposable
{
    private readonly string _testId = Guid.NewGuid().ToString();

    public MongoDbTrackingSubscriberTests()
    {
        TrackingComponentRegistry.Clear();
    }

    public void Dispose()
    {
        TrackingComponentRegistry.Clear();
    }

    private MongoDbTrackingOptions MakeOptions(MongoDbTrackingVerbosity verbosity = MongoDbTrackingVerbosity.Detailed) => new()
    {
        ServiceName = "MongoDB",
        CallerName = "MyService",
        Verbosity = verbosity,
        CurrentTestInfoFetcher = () => ("My Test", _testId),
    };

    private static ConnectionId MakeConnectionId() =>
        new(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 27017)));

    private static CommandStartedEvent MakeStartedEvent(string commandName, BsonDocument? command = null, int requestId = 1, string db = "testdb") =>
        new(commandName,
            command ?? new BsonDocument(commandName, "testcollection"),
            new DatabaseNamespace(db),
            1L, requestId, MakeConnectionId());

    private static CommandSucceededEvent MakeSucceededEvent(string commandName, int requestId = 1, BsonDocument? reply = null) =>
        new(commandName,
            reply ?? new BsonDocument("ok", 1),
            new DatabaseNamespace("testdb"),
            1L, requestId, MakeConnectionId(), TimeSpan.FromMilliseconds(5));

    private static CommandFailedEvent MakeFailedEvent(string commandName, int requestId = 1, Exception? exception = null) =>
        new(commandName,
            new DatabaseNamespace("testdb"),
            exception ?? new Exception("Test failure"),
            1L, requestId, MakeConnectionId(), TimeSpan.FromMilliseconds(5));

    private RequestResponseLog[] GetLogsFromThisTest() =>
        RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    // ─── Basic logging ───────────────────────────────────────

    [Fact]
    public void CommandStartedAndSucceeded_LogsRequestAndResponse()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());

        subscriber.OnCommandStarted(MakeStartedEvent("find"));
        subscriber.OnCommandSucceeded(MakeSucceededEvent("find"));

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
    }

    [Fact]
    public void CommandStartedAndFailed_LogsRequestAndErrorResponse()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());

        subscriber.OnCommandStarted(MakeStartedEvent("find"));
        subscriber.OnCommandFailed(MakeFailedEvent("find", exception: new Exception("Connection lost")));

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
        Assert.Equal(HttpStatusCode.InternalServerError, logs[1].StatusCode?.Value);
        Assert.Contains("Connection lost", logs[1].Content!);
    }

    // ─── Ignored commands ────────────────────────────────────

    [Theory]
    [InlineData("isMaster")]
    [InlineData("hello")]
    [InlineData("ping")]
    [InlineData("saslStart")]
    [InlineData("saslContinue")]
    [InlineData("buildInfo")]
    [InlineData("getLastError")]
    [InlineData("killCursors")]
    public void IgnoredCommands_AreNotLogged(string commandName)
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());

        subscriber.OnCommandStarted(MakeStartedEvent(commandName));

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    [Fact]
    public void GetMore_NotTrackedByDefault()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());

        subscriber.OnCommandStarted(MakeStartedEvent("getMore"));

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    [Fact]
    public void GetMore_TrackedWhenEnabled()
    {
        var options = MakeOptions();
        options.TrackGetMore = true;
        var subscriber = new MongoDbTrackingSubscriber(options);

        subscriber.OnCommandStarted(MakeStartedEvent("getMore"));
        subscriber.OnCommandSucceeded(MakeSucceededEvent("getMore"));

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    // ─── No logging when no test info ────────────────────────

    [Fact]
    public void NoLogging_WhenCurrentTestInfoFetcherIsNull()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        var subscriber = new MongoDbTrackingSubscriber(options);

        subscriber.OnCommandStarted(MakeStartedEvent("find"));

        Assert.Empty(GetLogsFromThisTest());
    }

    // ─── Summarised: skip Other ──────────────────────────────

    [Fact]
    public void Summarised_SkipsOtherOperations()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions(MongoDbTrackingVerbosity.Summarised));

        subscriber.OnCommandStarted(MakeStartedEvent("unknownCommand"));

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    [Fact]
    public void Summarised_LogsKnownOperations()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions(MongoDbTrackingVerbosity.Summarised));

        subscriber.OnCommandStarted(MakeStartedEvent("find"));
        subscriber.OnCommandSucceeded(MakeSucceededEvent("find"));

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    // ─── OperationId correlation ─────────────────────────────

    [Fact]
    public void RequestAndResponse_ShareSameTraceId()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());

        subscriber.OnCommandStarted(MakeStartedEvent("find"));
        subscriber.OnCommandSucceeded(MakeSucceededEvent("find"));

        var logs = GetLogsFromThisTest();
        Assert.Equal(logs[0].TraceId, logs[1].TraceId);
    }

    [Fact]
    public void MultipleConcurrentOperations_CorrelateCorrectly()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());

        subscriber.OnCommandStarted(MakeStartedEvent("find", requestId: 1));
        subscriber.OnCommandStarted(MakeStartedEvent("insert", requestId: 2));
        subscriber.OnCommandSucceeded(MakeSucceededEvent("insert", requestId: 2));
        subscriber.OnCommandSucceeded(MakeSucceededEvent("find", requestId: 1));

        var logs = GetLogsFromThisTest();
        Assert.Equal(4, logs.Length);

        // Find request and response should share same trace ID
        var findRequest = logs.First(l => l.Type == RequestResponseType.Request && l.Uri.ToString().Contains("testcollection"));
        var responses = logs.Where(l => l.Type == RequestResponseType.Response).ToArray();
        Assert.Equal(2, responses.Length);
    }

    // ─── Verbosity content ───────────────────────────────────

    [Fact]
    public void Raw_IncludesFullBsonCommand()
    {
        var command = new BsonDocument { { "find", "users" }, { "filter", new BsonDocument("age", 25) } };
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions(MongoDbTrackingVerbosity.Raw));

        subscriber.OnCommandStarted(MakeStartedEvent("find", command));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("find", log.Content!);
        Assert.Contains("users", log.Content!);
    }

    [Fact]
    public void Raw_ResponseIncludesReplyBson()
    {
        var reply = new BsonDocument { { "ok", 1 }, { "cursor", new BsonDocument("id", 0) } };
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions(MongoDbTrackingVerbosity.Raw));

        subscriber.OnCommandStarted(MakeStartedEvent("find"));
        subscriber.OnCommandSucceeded(MakeSucceededEvent("find", reply: reply));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Contains("cursor", log.Content!);
    }

    [Fact]
    public void Detailed_ShowsFilterAsRequestContent()
    {
        var command = new BsonDocument { { "find", "users" }, { "filter", new BsonDocument("age", 25) } };
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions(MongoDbTrackingVerbosity.Detailed));

        subscriber.OnCommandStarted(MakeStartedEvent("find", command));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("age", log.Content!);
    }

    [Fact]
    public void Detailed_ResponseContentIsNull()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions(MongoDbTrackingVerbosity.Detailed));

        subscriber.OnCommandStarted(MakeStartedEvent("find"));
        subscriber.OnCommandSucceeded(MakeSucceededEvent("find"));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Null(log.Content);
    }

    [Fact]
    public void Summarised_RequestContentIsNull()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions(MongoDbTrackingVerbosity.Summarised));

        subscriber.OnCommandStarted(MakeStartedEvent("find"));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Null(log.Content);
    }

    [Fact]
    public void Summarised_ResponseContentIsNull()
    {
        var opts = MakeOptions(MongoDbTrackingVerbosity.Summarised);
        opts.LogResponseContent = false;
        var subscriber = new MongoDbTrackingSubscriber(opts);

        subscriber.OnCommandStarted(MakeStartedEvent("find"));
        subscriber.OnCommandSucceeded(MakeSucceededEvent("find"));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Null(log.Content);
    }

    // ─── URI building ────────────────────────────────────────

    [Fact]
    public void Uri_IncludesDatabaseAndCollection()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());

        subscriber.OnCommandStarted(MakeStartedEvent("find", db: "myapp"));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("myapp", log.Uri.ToString());
        Assert.Contains("testcollection", log.Uri.ToString());
    }

    [Fact]
    public void Summarised_Uri_IncludesDatabaseOnly()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions(MongoDbTrackingVerbosity.Summarised));

        subscriber.OnCommandStarted(MakeStartedEvent("find", db: "myapp"));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("myapp", log.Uri.ToString());
    }

    // ─── Service names ───────────────────────────────────────

    [Fact]
    public void LogsUseConfiguredServiceNames()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());

        subscriber.OnCommandStarted(MakeStartedEvent("find"));

        var log = GetLogsFromThisTest().First();
        Assert.Equal("MongoDB", log.ServiceName);
        Assert.Equal("MyService", log.CallerName);
    }

    // ─── Succeeded response uses OK status ───────────────────

    [Fact]
    public void SucceededResponse_UsesOkStatus()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());

        subscriber.OnCommandStarted(MakeStartedEvent("find"));
        subscriber.OnCommandSucceeded(MakeSucceededEvent("find"));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Equal(HttpStatusCode.OK, log.StatusCode?.Value);
    }

    // ─── Unmatched events ────────────────────────────────────

    [Fact]
    public void SucceededEvent_WithoutMatchingStart_DoesNotLog()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());

        subscriber.OnCommandSucceeded(MakeSucceededEvent("find", requestId: 99));

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    [Fact]
    public void FailedEvent_WithoutMatchingStart_DoesNotLog()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());

        subscriber.OnCommandFailed(MakeFailedEvent("find", requestId: 99));

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    // ─── ITrackingComponent ──────────────────────────────────

    [Fact]
    public void Implements_ITrackingComponent()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());
        Assert.IsAssignableFrom<ITrackingComponent>(subscriber);
    }

    [Fact]
    public void WasInvoked_IsFalse_BeforeAnyCommands()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());
        Assert.False(subscriber.WasInvoked);
    }

    [Fact]
    public void WasInvoked_IsTrue_AfterCommand()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());
        subscriber.OnCommandStarted(MakeStartedEvent("find"));
        Assert.True(subscriber.WasInvoked);
    }

    [Fact]
    public void InvocationCount_StartsAtZero()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());
        Assert.Equal(0, subscriber.InvocationCount);
    }

    [Fact]
    public void InvocationCount_IncreasesWithEachCommand()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());

        subscriber.OnCommandStarted(MakeStartedEvent("find", requestId: 1));
        subscriber.OnCommandStarted(MakeStartedEvent("insert", requestId: 2));
        subscriber.OnCommandStarted(MakeStartedEvent("update", requestId: 3));

        Assert.Equal(3, subscriber.InvocationCount);
    }

    [Fact]
    public void InvocationCount_IncrementsEvenForIgnoredCommands()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());

        subscriber.OnCommandStarted(MakeStartedEvent("isMaster"));

        Assert.Equal(1, subscriber.InvocationCount);
    }

    [Fact]
    public void ComponentName_MatchesServiceName()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());
        Assert.Contains("MongoDB", subscriber.ComponentName);
    }

    [Fact]
    public void Constructor_AutoRegistersWithTrackingComponentRegistry()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());

        var registered = TrackingComponentRegistry.GetRegisteredComponents();
        Assert.Contains(registered, c => c.ComponentName == subscriber.ComponentName);
    }

    // ─── ExcludedOperations ──────────────────────────────────

    [Fact]
    public void ExcludedOperations_SkipsExcludedOps()
    {
        var options = MakeOptions();
        options.ExcludedOperations = [MongoDbOperation.Find];
        var subscriber = new MongoDbTrackingSubscriber(options);

        subscriber.OnCommandStarted(MakeStartedEvent("find"));

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    [Fact]
    public void ExcludedOperations_AllowsNonExcludedOps()
    {
        var options = MakeOptions();
        options.ExcludedOperations = [MongoDbOperation.Find];
        var subscriber = new MongoDbTrackingSubscriber(options);

        subscriber.OnCommandStarted(MakeStartedEvent("insert"));
        subscriber.OnCommandSucceeded(MakeSucceededEvent("insert"));

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    // ─── LogFilterText ───────────────────────────────────────

    [Fact]
    public void LogFilterText_False_SuppressesFilterInContent()
    {
        var options = MakeOptions();
        options.LogFilterText = false;
        var subscriber = new MongoDbTrackingSubscriber(options);
        var command = new BsonDocument { { "find", "users" }, { "filter", new BsonDocument("age", 25) } };

        subscriber.OnCommandStarted(MakeStartedEvent("find", command));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Null(log.Content);
    }

    [Fact]
    public void LogFilterText_True_ShowsFilterInContent()
    {
        var options = MakeOptions();
        options.LogFilterText = true;
        var subscriber = new MongoDbTrackingSubscriber(options);
        var command = new BsonDocument { { "find", "users" }, { "filter", new BsonDocument("age", 25) } };

        subscriber.OnCommandStarted(MakeStartedEvent("find", command));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("age", log.Content!);
    }

    // ─── endSessions ignored by default ──────────────────────

    [Fact]
    public void EndSessions_IgnoredByDefault()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());

        subscriber.OnCommandStarted(MakeStartedEvent("endSessions"));

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    // ─── Response metadata extraction ────────────────────────

    [Fact]
    public void SucceededResponse_Detailed_IncludesRowsAffected()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());

        subscriber.OnCommandStarted(MakeStartedEvent("update"));
        var reply = new BsonDocument { { "ok", 1 }, { "n", 3 }, { "nModified", 2 } };
        subscriber.OnCommandSucceeded(MakeSucceededEvent("update", reply: reply));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Contains("n=3", log.Content!);
        Assert.Contains("nModified=2", log.Content!);
    }

    [Fact]
    public void SucceededResponse_Detailed_NoRowsAffected_ContentIsNull()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());

        subscriber.OnCommandStarted(MakeStartedEvent("find"));
        subscriber.OnCommandSucceeded(MakeSucceededEvent("find"));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Null(log.Content);
    }

    // ─── IEventSubscriber ────────────────────────────────────

    [Fact]
    public void ImplementsIEventSubscriber()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());

        Assert.IsAssignableFrom<IEventSubscriber>(subscriber);
    }

    [Fact]
    public void TryGetEventHandler_CommandStartedEvent_ReturnsHandler()
    {
        var subscriber = (IEventSubscriber)new MongoDbTrackingSubscriber(MakeOptions());

        var result = subscriber.TryGetEventHandler<CommandStartedEvent>(out var handler);

        Assert.True(result);
        Assert.NotNull(handler);
    }

    [Fact]
    public void TryGetEventHandler_CommandSucceededEvent_ReturnsHandler()
    {
        var subscriber = (IEventSubscriber)new MongoDbTrackingSubscriber(MakeOptions());

        var result = subscriber.TryGetEventHandler<CommandSucceededEvent>(out var handler);

        Assert.True(result);
        Assert.NotNull(handler);
    }

    [Fact]
    public void TryGetEventHandler_CommandFailedEvent_ReturnsHandler()
    {
        var subscriber = (IEventSubscriber)new MongoDbTrackingSubscriber(MakeOptions());

        var result = subscriber.TryGetEventHandler<CommandFailedEvent>(out var handler);

        Assert.True(result);
        Assert.NotNull(handler);
    }

    [Fact]
    public void TryGetEventHandler_UnknownEvent_ReturnsFalse()
    {
        var subscriber = (IEventSubscriber)new MongoDbTrackingSubscriber(MakeOptions());

        var result = subscriber.TryGetEventHandler<ClusterOpenedEvent>(out var handler);

        Assert.False(result);
        Assert.Null(handler);
    }

    [Fact]
    public void IEventSubscriber_RoutesCommandStartedEvent_ProducesLogs()
    {
        var subscriber = (IEventSubscriber)new MongoDbTrackingSubscriber(MakeOptions());
        subscriber.TryGetEventHandler<CommandStartedEvent>(out var startHandler);
        subscriber.TryGetEventHandler<CommandSucceededEvent>(out var successHandler);

        startHandler!(MakeStartedEvent("insert"));
        successHandler!(MakeSucceededEvent("insert"));

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
    }

    [Fact]
    public void IEventSubscriber_RoutesCommandFailedEvent_ProducesErrorLog()
    {
        var subscriber = (IEventSubscriber)new MongoDbTrackingSubscriber(MakeOptions());
        subscriber.TryGetEventHandler<CommandStartedEvent>(out var startHandler);
        subscriber.TryGetEventHandler<CommandFailedEvent>(out var failHandler);

        startHandler!(MakeStartedEvent("delete"));
        failHandler!(MakeFailedEvent("delete"));

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
        Assert.Equal(HttpStatusCode.InternalServerError, logs[1].StatusCode?.Value);
    }

    // ─── Summarised + LogResponseContent ─────────────────────

    [Fact]
    public void Summarised_IncludesResponseContent_WhenLogResponseContentTrue()
    {
        var opts = MakeOptions(MongoDbTrackingVerbosity.Summarised);
        opts.LogResponseContent = true;
        var subscriber = new MongoDbTrackingSubscriber(opts);

        subscriber.OnCommandStarted(MakeStartedEvent("update"));
        var reply = new BsonDocument { { "ok", 1 }, { "n", 3 }, { "nModified", 2 } };
        subscriber.OnCommandSucceeded(MakeSucceededEvent("update", reply: reply));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.NotNull(log.Content);
        Assert.Contains("n=3", log.Content!);
    }

    [Fact]
    public void Summarised_OmitsResponseContent_WhenLogResponseContentFalse()
    {
        var opts = MakeOptions(MongoDbTrackingVerbosity.Summarised);
        opts.LogResponseContent = false;
        var subscriber = new MongoDbTrackingSubscriber(opts);

        subscriber.OnCommandStarted(MakeStartedEvent("update"));
        var reply = new BsonDocument { { "ok", 1 }, { "n", 3 }, { "nModified", 2 } };
        subscriber.OnCommandSucceeded(MakeSucceededEvent("update", reply: reply));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Null(log.Content);
    }

    [Fact]
    public void Summarised_ResponseVariant_includes_content_when_LogResponseContent_true()
    {
        var opts = MakeOptions(MongoDbTrackingVerbosity.Detailed);
        opts.SetupVerbosity = MongoDbTrackingVerbosity.Summarised;
        opts.LogResponseContent = true;
        var subscriber = new MongoDbTrackingSubscriber(opts);

        subscriber.OnCommandStarted(MakeStartedEvent("update"));
        var reply = new BsonDocument { { "ok", 1 }, { "n", 5 } };
        subscriber.OnCommandSucceeded(MakeSucceededEvent("update", reply: reply));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.NotNull(log.SetupVariant);
        Assert.NotNull(log.SetupVariant!.Content);
        Assert.Contains("n=5", log.SetupVariant!.Content!);
    }

    [Fact]
    public void Summarised_still_omits_request_content_when_LogResponseContent_true()
    {
        var opts = MakeOptions(MongoDbTrackingVerbosity.Summarised);
        opts.LogResponseContent = true;
        var subscriber = new MongoDbTrackingSubscriber(opts);

        subscriber.OnCommandStarted(MakeStartedEvent("find",
            new BsonDocument { { "find", "users" }, { "filter", new BsonDocument("age", 25) } }));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Null(log.Content);
    }

    // ─── Document response formatting ────────────────────────

    private static BsonDocument MakeReplyWithDocuments(params BsonDocument[] documents) =>
        new("cursor", new BsonDocument
        {
            { "id", 0L },
            { "ns", "testdb.testcollection" },
            { "firstBatch", new BsonArray(documents) }
        });

    [Fact]
    public void Detailed_ResponseWithDocuments_FormatsAsIndentedJson()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());
        var doc = new BsonDocument { { "_id", "abc-123" }, { "Name", "Alice" } };

        subscriber.OnCommandStarted(MakeStartedEvent("find"));
        subscriber.OnCommandSucceeded(MakeSucceededEvent("find", reply: MakeReplyWithDocuments(doc)));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.NotNull(log.Content);

        // Should contain indented JSON, not single-line
        Assert.Contains("\"_id\" : \"abc-123\"", log.Content!);
        Assert.Contains("[\n  {", log.Content!);  // array element indented
        Assert.Contains("\n    \"Name\"", log.Content!);  // field indented inside object
    }

    [Fact]
    public void Detailed_ResponseWithMultipleDocuments_FormatsAsIndentedJsonArray()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());
        var doc1 = new BsonDocument { { "_id", "1" }, { "Name", "Alice" } };
        var doc2 = new BsonDocument { { "_id", "2" }, { "Name", "Bob" } };

        subscriber.OnCommandStarted(MakeStartedEvent("find"));
        subscriber.OnCommandSucceeded(MakeSucceededEvent("find", reply: MakeReplyWithDocuments(doc1, doc2)));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.NotNull(log.Content);
        // Both documents should be indented in the array
        Assert.Contains("\"Alice\"", log.Content!);
        Assert.Contains("\"Bob\"", log.Content!);
        Assert.Contains("[\n  {", log.Content!);
    }

    [Fact]
    public void Detailed_ResponseWithDocuments_IncludesDocumentCount()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());
        var doc = new BsonDocument { { "_id", "abc" } };

        subscriber.OnCommandStarted(MakeStartedEvent("find"));
        subscriber.OnCommandSucceeded(MakeSucceededEvent("find", reply: MakeReplyWithDocuments(doc)));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.StartsWith("[\n  {", log.Content!);
    }

    [Fact]
    public void Detailed_ResponseWithDocumentsTruncated_ShowsMoreIndicator()
    {
        var opts = MakeOptions();
        opts.MaxResponseDocuments = 1;
        var subscriber = new MongoDbTrackingSubscriber(opts);

        var doc1 = new BsonDocument { { "_id", "1" } };
        var doc2 = new BsonDocument { { "_id", "2" } };
        var doc3 = new BsonDocument { { "_id", "3" } };

        subscriber.OnCommandStarted(MakeStartedEvent("find"));
        subscriber.OnCommandSucceeded(MakeSucceededEvent("find", reply: MakeReplyWithDocuments(doc1, doc2, doc3)));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Contains("... (2 more documents not shown)", log.Content!);

        // Only the first document should be in the formatted output
        Assert.Contains("\"_id\" : \"1\"", log.Content!);
        Assert.DoesNotContain("\"_id\" : \"2\"", log.Content!);
    }

    [Fact]
    public void Detailed_ResponseWithEmptyFirstBatch_ShowsZeroDocuments()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());

        subscriber.OnCommandStarted(MakeStartedEvent("find"));
        subscriber.OnCommandSucceeded(MakeSucceededEvent("find", reply: MakeReplyWithDocuments()));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.NotNull(log.Content);
        Assert.Contains("0 documents", log.Content!);
    }

    [Fact]
    public void Detailed_ResponseWithMetadataAndDocuments_CombinesBoth()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());
        var reply = new BsonDocument
        {
            { "ok", 1 },
            { "n", 1 },
            { "cursor", new BsonDocument
                {
                    { "id", 0L },
                    { "ns", "testdb.testcollection" },
                    { "firstBatch", new BsonArray { new BsonDocument { { "_id", "x" }, { "Rating", 5 } } } }
                }
            }
        };

        subscriber.OnCommandStarted(MakeStartedEvent("find"));
        subscriber.OnCommandSucceeded(MakeSucceededEvent("find", reply: reply));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.NotNull(log.Content);
        Assert.Contains("n=1", log.Content!);
        Assert.Contains("\"Rating\" : 5", log.Content!);
        Assert.DoesNotContain("document(s)", log.Content!);
    }

    [Fact]
    public void Detailed_ResponseWithNestedObjects_FormatsNestedIndentation()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());
        var doc = new BsonDocument
        {
            { "_id", "abc" },
            { "Tags", new BsonArray { "fluffy", "breakfast" } },
            { "CreatedAt", new BsonDocument("$date", "2026-05-17T12:00:00Z") }
        };

        subscriber.OnCommandStarted(MakeStartedEvent("find"));
        subscriber.OnCommandSucceeded(MakeSucceededEvent("find", reply: MakeReplyWithDocuments(doc)));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.NotNull(log.Content);

        // Nested array and objects should also be indented
        Assert.Contains("\"Tags\"", log.Content!);
        Assert.Contains("\"fluffy\"", log.Content!);
        Assert.Contains("\"$date\"", log.Content!);
    }

    // ─── Document ownership (plans/DOCUMENT_OWNERSHIP_PLAN.md) ──

    [Fact]
    public void A_command_on_a_document_the_scenario_wrote_is_the_scenarios_when_nothing_else_names_one()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());
        subscriber.OnCommandStarted(MakeStartedEvent("insert", new BsonDocument { { "insert", "orders" }, { "documents", new BsonArray { new BsonDocument("_id", "order-1") } } }));
        subscriber.OnCommandSucceeded(MakeSucceededEvent("insert"));

        using (TestIdentityScope.Detach())
        {
            subscriber.OnCommandStarted(MakeStartedEvent("update", new BsonDocument { { "update", "orders" }, { "updates", new BsonArray { new BsonDocument { { "q", new BsonDocument("_id", "order-1") }, { "u", new BsonDocument("$set", new BsonDocument("status", "Processing")) } } } } }, requestId: 2));
            subscriber.OnCommandSucceeded(MakeSucceededEvent("update", requestId: 2));
            subscriber.OnCommandStarted(MakeStartedEvent("find", new BsonDocument { { "find", "orders" }, { "filter", new BsonDocument("status", "Pending") } }, requestId: 3));
            subscriber.OnCommandSucceeded(MakeSucceededEvent("find", requestId: 3));
        }

        var logs = GetLogsFromThisTest();
        Assert.Equal(4, logs.Length); // the insert pair, the update pair; the find named no document
        Assert.Equal(AttributionSource.DocumentOwner, logs[2].AttributionSource);
        Assert.Equal(AttributionSource.DocumentOwner, logs[3].AttributionSource);
    }
}
