using System.Net;
using System.Text.Json;
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

    // ─── HttpContextAccessor (#136) ──────────────────────────

    [Fact]
    public void Subscriber_reads_HttpContextAccessor_from_options_when_not_passed_directly()
    {
        var headerTestId = Guid.NewGuid().ToString();
        var options = MakeOptions();
        options.HttpContextAccessor = HeaderAccessor.For("Header Test", headerTestId);
        var subscriber = new MongoDbTrackingSubscriber(options);

        subscriber.OnCommandStarted(MakeStartedEvent("find"));

        // The request's headers outrank the fetcher, which answers this class's own test.
        var log = Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == headerTestId);
        Assert.Equal("Header Test", log.TestName);
        Assert.Equal(AttributionSource.RequestHeader, log.AttributionSource);
        Assert.Empty(GetLogsFromThisTest());
        Assert.True(subscriber.HasHttpContextAccessor);
    }

    [Fact]
    public void Subscriber_explicit_accessor_takes_precedence_over_options()
    {
        var explicitTestId = Guid.NewGuid().ToString();
        var optionsTestId = Guid.NewGuid().ToString();
        var options = MakeOptions();
        options.HttpContextAccessor = HeaderAccessor.For("Options Test", optionsTestId);
        var subscriber = new MongoDbTrackingSubscriber(options, HeaderAccessor.For("Explicit Test", explicitTestId));

        subscriber.OnCommandStarted(MakeStartedEvent("find"));

        var log = Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == explicitTestId);
        Assert.Equal(AttributionSource.RequestHeader, log.AttributionSource);
        Assert.DoesNotContain(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == optionsTestId);
    }

    [Fact]
    public void Subscriber_has_no_accessor_when_neither_options_nor_parameter()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());

        Assert.False(subscriber.HasHttpContextAccessor);
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
    public void An_update_reply_is_its_counts_as_json()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());

        subscriber.OnCommandStarted(MakeStartedEvent("update"));
        var reply = new BsonDocument { { "n", 3 }, { "nModified", 2 }, { "ok", 1 } };
        subscriber.OnCommandSucceeded(MakeSucceededEvent("update", reply: reply));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Equal("{ \"n\" : 3, \"nModified\" : 2 }", log.Content);
        Assert.Equal(HttpStatusCode.OK, log.StatusCode?.Value);
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
        Assert.Equal("{ \"n\" : 3, \"nModified\" : 2 }", log.Content);
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
        Assert.Equal("{ \"n\" : 5 }", log.SetupVariant!.Content);
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

    // ─── Reply notes (plans/MONGODB_DETAILED_PAYLOADS_PLAN.md, R1) ──

    private static BsonDocument MakeReplyWithDocuments(params BsonDocument[] documents) =>
        new()
        {
            { "cursor", new BsonDocument
                {
                    { "firstBatch", new BsonArray(documents) },
                    { "id", 0L },
                    { "ns", "testdb.testcollection" },
                }
            },
            { "ok", 1.0 },
        };

    private static readonly BsonDocument ClusterTime = new()
    {
        { "clusterTime", new BsonTimestamp(1760000000, 1) },
        { "signature", new BsonDocument { { "hash", new BsonBinaryData(new byte[20]) }, { "keyId", 0L } } },
    };

    /// <summary>The reply note a command and its reply are logged with.</summary>
    private string? ReplyNote(string commandName, BsonDocument reply, BsonDocument? command = null, MongoDbTrackingOptions? options = null)
    {
        var subscriber = new MongoDbTrackingSubscriber(options ?? MakeOptions());
        subscriber.OnCommandStarted(MakeStartedEvent(commandName, command));
        subscriber.OnCommandSucceeded(MakeSucceededEvent(commandName, reply: reply));
        return GetLogsFromThisTest().Last(l => l.Type == RequestResponseType.Response).Content;
    }

    [Fact]
    public void A_reply_batch_is_the_documents_as_a_json_array()
    {
        var note = ReplyNote("find", MakeReplyWithDocuments(new BsonDocument { { "_id", "abc-123" }, { "Name", "Alice" } }));

        Assert.Equal("[{ \"_id\" : \"abc-123\", \"Name\" : \"Alice\" }]", note);
    }

    [Fact]
    public void A_reply_batch_of_several_documents_is_one_array()
    {
        var note = ReplyNote("find", MakeReplyWithDocuments(
            new BsonDocument { { "_id", "1" }, { "Name", "Alice" } },
            new BsonDocument { { "_id", "2" }, { "Name", "Bob" } }));

        Assert.Equal("[{ \"_id\" : \"1\", \"Name\" : \"Alice\" }, { \"_id\" : \"2\", \"Name\" : \"Bob\" }]", note);
    }

    [Fact]
    public void A_reply_batch_is_json_on_both_drivers()
    {
        // The values driver 2.x wrote in the shell's syntax: NumberDecimal, ObjectId, ISODate, CSUUID, NumberLong.
        var document = new BsonDocument
        {
            { "_id", ObjectId.Parse("65a1b2c3d4e5f60718293a4b") },
            { "amount", Decimal128.Parse("1000.50") },
            { "openedAt", new BsonDateTime(1704067200000) },
            { "testId", BsonDocument.Parse("{ \"g\" : { \"$binary\" : { \"base64\" : \"D4+tW9nLRp+hZXCGdyiVDg==\", \"subType\" : \"04\" } } }")["g"] },
            { "version", 5000000000L },
        };

        var note = ReplyNote("find", MakeReplyWithDocuments(document));

        Assert.Equal(
            "[{ \"_id\" : { \"$oid\" : \"65a1b2c3d4e5f60718293a4b\" }, \"amount\" : { \"$numberDecimal\" : \"1000.50\" }, " +
            "\"openedAt\" : { \"$date\" : \"2024-01-01T00:00:00Z\" }, \"testId\" : { \"$uuid\" : \"0f8fad5b-d9cb-469f-a165-70867728950e\" }, " +
            "\"version\" : 5000000000 }]",
            note);
        using (JsonDocument.Parse(note!)) { }
    }

    [Fact]
    public void An_empty_batch_is_an_empty_array()
    {
        Assert.Equal("[]", ReplyNote("find", MakeReplyWithDocuments()));
    }

    [Fact]
    public void A_capped_batch_ends_with_its_footnote_and_parses_once_the_footnote_is_off()
    {
        var options = MakeOptions();
        options.MaxResponseDocuments = 1;

        var note = ReplyNote("find", MakeReplyWithDocuments(
            new BsonDocument("_id", "1"), new BsonDocument("_id", "2"), new BsonDocument("_id", "3")), options: options);

        Assert.Equal("[{ \"_id\" : \"1\" }]\n... (2 more documents not shown)", note);
        Assert.NotNull(note);
        using (JsonDocument.Parse(note[..note.IndexOf('\n')])) { }
    }

    [Fact]
    public void A_reply_with_counts_and_a_cursor_keeps_both_with_the_batch_in_place_of_the_cursor()
    {
        // The client-level bulkWrite (driver 3.x, server 8.0): counts beside a cursor of per-operation results.
        var reply = new BsonDocument
        {
            { "cursor", new BsonDocument
                {
                    { "id", 0L },
                    { "firstBatch", new BsonArray { new BsonDocument { { "ok", 1.0 }, { "idx", 0 }, { "n", 1 } } } },
                    { "ns", "admin.$cmd.bulkWrite" },
                }
            },
            { "nErrors", 0 }, { "nInserted", 1 }, { "nUpserted", 0 }, { "nMatched", 0 }, { "nModified", 0 }, { "nDeleted", 0 },
            { "ok", 1.0 },
        };

        Assert.Equal(
            "{ \"cursor\" : [{ \"ok\" : 1.0, \"idx\" : 0, \"n\" : 1 }], \"nErrors\" : 0, \"nInserted\" : 1, \"nUpserted\" : 0, " +
            "\"nMatched\" : 0, \"nModified\" : 0, \"nDeleted\" : 0 }",
            ReplyNote("bulkWrite", reply));
    }

    [Fact]
    public void A_date_in_a_batch_is_a_date()
    {
        var document = new BsonDocument
        {
            { "_id", "abc" },
            { "Tags", new BsonArray { "fluffy", "breakfast" } },
            { "CreatedAt", new BsonDateTime(new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc)) },
        };

        Assert.Equal(
            "[{ \"_id\" : \"abc\", \"Tags\" : [\"fluffy\", \"breakfast\"], \"CreatedAt\" : { \"$date\" : \"2026-05-17T12:00:00Z\" } }]",
            ReplyNote("find", MakeReplyWithDocuments(document)));
    }

    [Fact]
    public void An_upsert_reply_names_its_id()
    {
        var reply = new BsonDocument
        {
            { "n", 1 },
            { "electionId", ObjectId.Parse("7fffffff0000000000000001") },
            { "opTime", new BsonDocument { { "ts", new BsonTimestamp(1760000000, 1) }, { "t", 1L } } },
            { "nModified", 0 },
            { "upserted", new BsonArray { new BsonDocument { { "index", 0 }, { "_id", ObjectId.Parse("65a1b2c3d4e5f60718293a4b") } } } },
            { "ok", 1.0 },
            { "$clusterTime", ClusterTime },
            { "operationTime", new BsonTimestamp(1760000000, 1) },
        };

        Assert.Equal(
            "{ \"n\" : 1, \"nModified\" : 0, \"upserted\" : [{ \"index\" : 0, \"_id\" : { \"$oid\" : \"65a1b2c3d4e5f60718293a4b\" } }] }",
            ReplyNote("update", reply));
    }

    private static readonly BsonDocument ClaimById = new()
    {
        { "findAndModify", "loans" },
        { "query", new BsonDocument("_id", "loan-1") },
        { "update", new BsonDocument("$set", new BsonDocument("status", "claimed")) },
    };

    [Fact]
    public void A_findAndModify_reply_holds_its_document()
    {
        var reply = new BsonDocument
        {
            { "lastErrorObject", new BsonDocument { { "n", 1 }, { "updatedExisting", true } } },
            { "value", new BsonDocument { { "_id", "loan-1" }, { "status", "claimed" } } },
            { "ok", 1.0 },
        };

        Assert.Equal(
            "{ \"lastErrorObject\" : { \"n\" : 1, \"updatedExisting\" : true }, \"value\" : { \"_id\" : \"loan-1\", \"status\" : \"claimed\" } }",
            ReplyNote("findAndModify", reply, ClaimById));
    }

    [Fact]
    public void A_findAndModify_that_took_nothing_says_value_null()
    {
        var reply = new BsonDocument
        {
            { "lastErrorObject", new BsonDocument { { "n", 0 }, { "updatedExisting", false } } },
            { "value", BsonNull.Value },
            { "ok", 1.0 },
        };

        Assert.Equal(
            "{ \"lastErrorObject\" : { \"n\" : 0, \"updatedExisting\" : false }, \"value\" : null }",
            ReplyNote("findAndModify", reply, ClaimById));
    }

    [Fact]
    public void A_distinct_reply_holds_its_values()
    {
        var reply = new BsonDocument
        {
            { "values", new BsonArray { "Draft", "Ready" } },
            { "ok", 1.0 },
            { "$clusterTime", ClusterTime },
            { "operationTime", new BsonTimestamp(1760000000, 1) },
        };

        Assert.Equal("{ \"values\" : [\"Draft\", \"Ready\"] }", ReplyNote("distinct", reply));
    }

    private static readonly BsonDocument GetMoreOnLoans = new() { { "getMore", 8271923L }, { "collection", "loans" }, { "batchSize", 2 } };

    [Fact]
    public void A_getMore_reply_holds_its_next_batch()
    {
        var options = MakeOptions();
        options.TrackGetMore = true;
        var reply = new BsonDocument
        {
            { "cursor", new BsonDocument
                {
                    { "nextBatch", new BsonArray { new BsonDocument("_id", 3), new BsonDocument("_id", 4) } },
                    { "id", 0L },
                    { "ns", "testdb.loans" },
                }
            },
            { "ok", 1.0 },
        };

        Assert.Equal("[{ \"_id\" : 3 }, { \"_id\" : 4 }]", ReplyNote("getMore", reply, GetMoreOnLoans, options));
    }

    [Fact]
    public void A_getMore_is_drawn_against_its_collection()
    {
        var options = MakeOptions();
        options.TrackGetMore = true;
        var subscriber = new MongoDbTrackingSubscriber(options);

        subscriber.OnCommandStarted(MakeStartedEvent("getMore", GetMoreOnLoans));

        var request = GetLogsFromThisTest().Single(l => l.Type == RequestResponseType.Request);
        Assert.Equal("GetMore ← loans", request.Method.Value?.ToString());
        Assert.Equal("mongodb:///testdb/loans", request.Uri.ToString());
    }

    // Written out rather than read from the product, so a name dropped from its list fails its own case.
    public static TheoryData<string> Bookkeeping =>
        ["ok", "$clusterTime", "operationTime", "electionId", "opTime", "recoveryToken", "retriedStmtIds", "readOnly", "$replData"];

    [Theory]
    [MemberData(nameof(Bookkeeping))]
    public void The_servers_bookkeeping_is_not_in_a_reply_note(string field)
    {
        var reply = new BsonDocument { { "n", 1 }, { field, new BsonDocument("recoveryShardId", "sh0") } };

        Assert.Equal("{ \"n\" : 1 }", ReplyNote("update", reply));
    }

    [Fact]
    public void A_sharded_count_keeps_its_count_per_shard()
    {
        var reply = new BsonDocument { { "shards", new BsonDocument("sh0", 11) }, { "n", 11 }, { "ok", 1.0 } };

        Assert.Equal("{ \"shards\" : { \"sh0\" : 11 }, \"n\" : 11 }", ReplyNote("count", reply));
    }

    [Fact]
    public void A_reply_with_nothing_but_bookkeeping_has_no_note()
    {
        var reply = new BsonDocument { { "ok", 1.0 }, { "$clusterTime", ClusterTime }, { "operationTime", new BsonTimestamp(1760000000, 1) } };

        Assert.Null(ReplyNote("commitTransaction", reply));
    }

    [Theory]
    [InlineData("serverStatus")]
    [InlineData("dbStats")]
    [InlineData("collStats")]
    public void Server_state_replies_are_left_out_at_Detailed_and_whole_at_Raw(string commandName)
    {
        var reply = new BsonDocument { { "host", "mongo:27017" }, { "uptime", 12.0 }, { "ok", 1.0 } };

        Assert.Null(ReplyNote(commandName, reply));
        Assert.Equal(
            "{ \"host\" : \"mongo:27017\", \"uptime\" : 12.0, \"ok\" : 1.0 }",
            ReplyNote(commandName, reply, options: MakeOptions(MongoDbTrackingVerbosity.Raw)));
    }

    [Fact]
    public void Without_LogResponseContent_the_documents_are_left_out_and_the_rest_stays()
    {
        var options = MakeOptions();
        options.LogResponseContent = false;

        Assert.Null(ReplyNote("find", MakeReplyWithDocuments(new BsonDocument("_id", 1)), options: options));
        Assert.Equal(
            "{ \"lastErrorObject\" : { \"n\" : 1, \"updatedExisting\" : true } }",
            ReplyNote("findAndModify", new BsonDocument
            {
                { "lastErrorObject", new BsonDocument { { "n", 1 }, { "updatedExisting", true } } },
                { "value", new BsonDocument("_id", "loan-1") },
                { "ok", 1.0 },
            }, ClaimById, options));
        Assert.Null(ReplyNote("distinct", new BsonDocument { { "values", new BsonArray { "Ready" } }, { "ok", 1.0 } }, options: options));
        Assert.Equal("{ \"n\" : 2, \"nModified\" : 2 }", ReplyNote("update", new BsonDocument { { "n", 2 }, { "nModified", 2 }, { "ok", 1.0 } }, options: options));
    }

    [Fact]
    public void A_large_binary_is_a_placeholder_at_Detailed_and_whole_at_Raw()
    {
        var chunk = new BsonDocument { { "_id", 1 }, { "data", new BsonBinaryData(new byte[300]) } };

        Assert.Equal(
            "[{ \"_id\" : 1, \"data\" : \"[binary: 300 bytes, subtype 00]\" }]",
            ReplyNote("find", MakeReplyWithDocuments(chunk)));
        Assert.Contains(
            "\"data\" : { \"$binary\" : { \"base64\" : \"AAAA",
            ReplyNote("find", MakeReplyWithDocuments(chunk), options: MakeOptions(MongoDbTrackingVerbosity.Raw)));
    }

    [Fact]
    public void A_raw_reply_is_the_whole_reply_as_relaxed_extended_json()
    {
        var reply = new BsonDocument { { "n", 1 }, { "ok", 1.0 }, { "operationTime", new BsonTimestamp(1760000000, 1) } };

        Assert.Equal(
            "{ \"n\" : 1, \"ok\" : 1.0, \"operationTime\" : { \"$timestamp\" : { \"t\" : 1760000000, \"i\" : 1 } } }",
            ReplyNote("update", reply, options: MakeOptions(MongoDbTrackingVerbosity.Raw)));
    }

    // ─── Failures (plans/MONGODB_DETAILED_PAYLOADS_PLAN.md, R1) ──

    private const string DuplicateKeyMessage = "E11000 duplicate key error collection: app.loans index: loanNumber_1 dup key: { loanNumber: \"L-1001\" }";

    private static BsonDocument DuplicateKeyReply() => new()
    {
        { "n", 0 },
        { "writeErrors", new BsonArray
            {
                new BsonDocument
                {
                    { "index", 0 }, { "code", 11000 }, { "errmsg", DuplicateKeyMessage },
                    { "keyPattern", new BsonDocument("loanNumber", 1) }, { "keyValue", new BsonDocument("loanNumber", "L-1001") },
                },
            }
        },
        { "ok", 1.0 },
    };

    private RequestResponseLog ResponseTo(string commandName, BsonDocument reply)
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());
        subscriber.OnCommandStarted(MakeStartedEvent(commandName));
        subscriber.OnCommandSucceeded(MakeSucceededEvent(commandName, reply: reply));
        return GetLogsFromThisTest().Single(l => l.Type == RequestResponseType.Response);
    }

    [Fact]
    public void A_write_error_reply_is_a_failure()
    {
        var response = ResponseTo("insert", DuplicateKeyReply());

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode?.Value);
        Assert.Equal(
            "{ \"n\" : 0, \"writeErrors\" : [{ \"index\" : 0, \"code\" : 11000, \"errmsg\" : \"E11000 duplicate key error collection: " +
            "app.loans index: loanNumber_1 dup key: { loanNumber: \\\"L-1001\\\" }\", \"keyPattern\" : { \"loanNumber\" : 1 }, " +
            "\"keyValue\" : { \"loanNumber\" : \"L-1001\" } }] }",
            response.Content);
    }

    [Fact]
    public void A_write_error_sets_Error_to_its_errmsg()
    {
        Assert.Equal(DuplicateKeyMessage, ResponseTo("insert", DuplicateKeyReply()).Error);
    }

    [Fact]
    public void A_write_concern_error_reply_is_a_failure()
    {
        var reply = new BsonDocument
        {
            { "n", 1 },
            { "writeConcernError", new BsonDocument { { "code", 64 }, { "codeName", "WriteConcernFailed" }, { "errmsg", "waiting for replication timed out" } } },
            { "ok", 1.0 },
        };

        var response = ResponseTo("update", reply);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode?.Value);
        Assert.Equal("waiting for replication timed out", response.Error);
        Assert.Equal(
            "{ \"n\" : 1, \"writeConcernError\" : { \"code\" : 64, \"codeName\" : \"WriteConcernFailed\", \"errmsg\" : \"waiting for replication timed out\" } }",
            response.Content);
    }

    [Fact]
    public void A_client_bulkWrite_with_errors_is_a_failure()
    {
        var reply = new BsonDocument
        {
            { "cursor", new BsonDocument
                {
                    { "id", 0L },
                    { "firstBatch", new BsonArray
                        {
                            new BsonDocument { { "ok", 1.0 }, { "idx", 0 }, { "n", 1 } },
                            new BsonDocument { { "ok", 0.0 }, { "idx", 1 }, { "code", 11000 }, { "errmsg", DuplicateKeyMessage } },
                        }
                    },
                    { "ns", "admin.$cmd.bulkWrite" },
                }
            },
            { "nErrors", 1 }, { "nInserted", 1 }, { "nUpserted", 0 }, { "nMatched", 0 }, { "nModified", 0 }, { "nDeleted", 0 },
            { "ok", 1.0 },
        };

        var response = ResponseTo("bulkWrite", reply);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode?.Value);
        Assert.Equal(DuplicateKeyMessage, response.Error);
    }

    [Fact]
    public void A_count_of_errors_is_read_whatever_its_integer_type()
    {
        // The rule reads any number the server may send a count as, a 64-bit one included.
        var response = ResponseTo("bulkWrite", new BsonDocument
        {
            { "cursor", new BsonDocument { { "id", 0L }, { "firstBatch", new BsonArray() }, { "ns", "admin.$cmd.bulkWrite" } } },
            { "nErrors", 1L }, { "nInserted", 0L },
            { "ok", 1.0 },
        });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode?.Value);
    }

    [Fact]
    public void A_write_that_succeeded_has_no_Error()
    {
        var response = ResponseTo("update", new BsonDocument { { "n", 1 }, { "nModified", 1 }, { "ok", 1.0 } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode?.Value);
        Assert.Null(response.Error);
    }

    /// <summary>The failure the driver raises on a command's failed event: its message names the command, and the server's words are in its result.</summary>
    private static MongoCommandException ServerError(string commandName, BsonDocument result) =>
        new(MakeConnectionId(), $"{commandName} command failed", new BsonDocument(commandName, "loans"), result);

    private static BsonDocument BadValueResult() => new()
    {
        { "ok", 0.0 },
        { "errmsg", "unknown operator: $bogus" },
        { "code", 2 },
        { "codeName", "BadValue" },
        { "$clusterTime", ClusterTime },
        { "operationTime", new BsonTimestamp(1760000000, 1) },
    };

    private RequestResponseLog FailureOf(Exception exception, MongoDbTrackingVerbosity verbosity = MongoDbTrackingVerbosity.Detailed)
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions(verbosity));
        subscriber.OnCommandStarted(MakeStartedEvent("find"));
        subscriber.OnCommandFailed(MakeFailedEvent("find", exception: exception));
        return GetLogsFromThisTest().Single(l => l.Type == RequestResponseType.Response);
    }

    [Fact]
    public void A_server_error_keeps_errmsg_code_and_codeName()
    {
        var response = FailureOf(ServerError("find", BadValueResult()));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode?.Value);
        Assert.Equal("{ \"errmsg\" : \"unknown operator: $bogus\", \"code\" : 2, \"codeName\" : \"BadValue\" }", response.Content);
    }

    [Fact]
    public void A_failed_command_sets_Error_to_the_servers_errmsg()
    {
        Assert.Equal("unknown operator: $bogus", FailureOf(ServerError("find", BadValueResult())).Error);
    }

    [Fact]
    public void A_raw_server_error_is_the_whole_reply()
    {
        var response = FailureOf(ServerError("find", BadValueResult()), MongoDbTrackingVerbosity.Raw);

        Assert.StartsWith("{ \"ok\" : 0.0, \"errmsg\" : \"unknown operator: $bogus\", \"code\" : 2, \"codeName\" : \"BadValue\", \"$clusterTime\" : {", response.Content);
    }

    [Fact]
    public void A_failure_with_no_reply_keeps_its_message_as_note_and_Error()
    {
        var response = FailureOf(new TimeoutException("A timeout occurred after 30000ms selecting a server."));

        Assert.Equal("A timeout occurred after 30000ms selecting a server.", response.Content);
        Assert.Equal("A timeout occurred after 30000ms selecting a server.", response.Error);
    }

    // ─── The driver's documents are gone when a handler returns (plans/MONGODB_DETAILED_PAYLOADS_PLAN.md, F6) ──

    private static RawBsonDocument Disposable(BsonDocument document) => new(document.ToBson());

    [Fact]
    public void A_deferred_claim_is_logged_at_Raw_after_its_command_is_disposed()
    {
        // A claim by filter is logged when its reply names the document it took, after the driver has disposed the
        // command. At Raw the request note was read from the command then, and the application's call threw.
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions(MongoDbTrackingVerbosity.Raw));
        subscriber.OnCommandStarted(MakeStartedEvent("insert", InsertOne("order-7")));
        subscriber.OnCommandSucceeded(MakeSucceededEvent("insert"));

        var claim = Disposable(new BsonDocument
        {
            { "findAndModify", "orders" },
            { "query", new BsonDocument("status", "Pending") },
            { "update", new BsonDocument("$set", new BsonDocument("status", "Processing")) },
        });
        var asStarted = claim.ToString();
        Exception? thrown;
        using (TestIdentityScope.Detach())
        {
            subscriber.OnCommandStarted(MakeStartedEvent("findAndModify", claim, requestId: 2));
            claim.Dispose();
            thrown = Record.Exception(() => subscriber.OnCommandSucceeded(MakeSucceededEvent("findAndModify", requestId: 2,
                reply: new BsonDocument { { "value", new BsonDocument { { "_id", "order-7" }, { "status", "Pending" } } }, { "ok", 1.0 } })));
        }

        Assert.Null(thrown);
        var requests = GetLogsFromThisTest().Where(l => l.Type == RequestResponseType.Request).ToArray();
        Assert.Equal(2, requests.Length);
        Assert.Equal(asStarted, requests[1].Content);
    }

    [Fact]
    public void A_deferred_claim_keeps_a_note_for_each_phase_verbosity()
    {
        var options = MakeOptions(MongoDbTrackingVerbosity.Detailed);
        options.SetupVerbosity = MongoDbTrackingVerbosity.Raw;
        options.ActionVerbosity = MongoDbTrackingVerbosity.Summarised;
        var subscriber = new MongoDbTrackingSubscriber(options);
        subscriber.OnCommandStarted(MakeStartedEvent("insert", InsertOne("order-8")));
        subscriber.OnCommandSucceeded(MakeSucceededEvent("insert"));

        var claim = Disposable(new BsonDocument
        {
            { "findAndModify", "orders" },
            { "query", new BsonDocument("status", "Pending") },
            { "update", new BsonDocument("$set", new BsonDocument("status", "Processing")) },
        });
        var asStarted = claim.ToString();
        using (TestIdentityScope.Detach())
        {
            subscriber.OnCommandStarted(MakeStartedEvent("findAndModify", claim, requestId: 2));
            claim.Dispose();
            subscriber.OnCommandSucceeded(MakeSucceededEvent("findAndModify", requestId: 2,
                reply: new BsonDocument { { "value", new BsonDocument { { "_id", "order-8" }, { "status", "Pending" } } }, { "ok", 1.0 } }));
        }

        var request = GetLogsFromThisTest().Where(l => l.Type == RequestResponseType.Request).ElementAt(1);
        Assert.Equal(asStarted, request.SetupVariant?.Content);
        Assert.Null(request.ActionVariant?.Content);
    }

    [Fact]
    public void A_reply_that_cannot_be_read_is_drawn_saying_why_and_does_not_reach_the_driver()
    {
        var reply = Disposable(new BsonDocument { { "n", 1 }, { "ok", 1.0 } });
        reply.Dispose();

        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());
        subscriber.OnCommandStarted(MakeStartedEvent("update"));
        var thrown = Record.Exception(() => subscriber.OnCommandSucceeded(MakeSucceededEvent("update", reply: reply)));

        Assert.Null(thrown);
        var response = GetLogsFromThisTest().Single(l => l.Type == RequestResponseType.Response);
        Assert.Equal("<reply could not be rendered: ObjectDisposedException>", response.Content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode?.Value);
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

    // ─── The owner window (plans/OWNER_WINDOW_AND_COUNT_CONFIRMATION_PLAN.md) ──

    /// <summary>A tracked call that names no document, made by the flow: the outbox processor's dispatch.</summary>
    private static void Dispatch(string what) =>
        RequestResponseLogger.LogPair(HttpMethod.Post, new Uri("http://dispatcher/" + what), "Dispatcher", "Breakfast Provider");

    private static BsonDocument InsertOne(string id) =>
        new() { { "insert", "orders" }, { "documents", new BsonArray { new BsonDocument("_id", id) } } };

    private static BsonDocument UpdateById(string id) =>
        new() { { "update", "orders" }, { "updates", new BsonArray { new BsonDocument { { "q", new BsonDocument("_id", id) }, { "u", new BsonDocument("$set", new BsonDocument("status", "Processing")) } } } } };

    private static BsonDocument Poll() =>
        new() { { "find", "orders" }, { "filter", new BsonDocument("status", "Pending") } };

    private static AttributionSource?[] Sources(IEnumerable<RequestResponseLog> logs) =>
        logs.Where(l => l.Type == RequestResponseType.Request).Select(l => l.AttributionSource).ToArray();

    [Fact]
    public void The_flow_that_wrote_a_scenarios_document_keeps_the_scenario_until_it_touches_another()
    {
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());
        subscriber.OnCommandStarted(MakeStartedEvent("insert", InsertOne("order-1")));
        subscriber.OnCommandSucceeded(MakeSucceededEvent("insert"));

        using (TestIdentityScope.Detach())
        {
            subscriber.OnCommandStarted(MakeStartedEvent("update", UpdateById("order-1"), requestId: 2)); // the claim
            subscriber.OnCommandSucceeded(MakeSucceededEvent("update", requestId: 2));
            Dispatch("first");
            subscriber.OnCommandStarted(MakeStartedEvent("update", UpdateById("order-1"), requestId: 3)); // the status update
            subscriber.OnCommandSucceeded(MakeSucceededEvent("update", requestId: 3));
            Dispatch("after");
            subscriber.OnCommandStarted(MakeStartedEvent("find", Poll(), requestId: 4));                  // the next poll
            subscriber.OnCommandSucceeded(MakeSucceededEvent("find", requestId: 4));
            Dispatch("orphan");
        }

        var logs = GetLogsFromThisTest();
        Assert.Equal([AttributionSource.TestContext, AttributionSource.DocumentOwner, AttributionSource.DocumentFlow, AttributionSource.DocumentOwner], Sources(logs));
        Assert.Equal("http://dispatcher/first", logs.Where(l => l.Type == RequestResponseType.Request).ElementAt(2).Uri.ToString());
        Assert.DoesNotContain(RequestResponseLogger.RequestAndResponseLogs, l => l.Uri.ToString() is "http://dispatcher/after" or "http://dispatcher/orphan");
    }

    [Fact]
    public void A_claim_by_filter_is_the_scenarios_when_the_reply_names_its_document()
    {
        // findAndModify({status: Pending} -> {status: Processing}) is the idiomatic Mongo claim: it names no
        // document until the server answers with the one it took. The claim, and the window, wait for the
        // reply; a claim that took nothing is nobody's.
        var subscriber = new MongoDbTrackingSubscriber(MakeOptions());
        subscriber.OnCommandStarted(MakeStartedEvent("insert", InsertOne("order-1")));
        subscriber.OnCommandSucceeded(MakeSucceededEvent("insert"));

        using (TestIdentityScope.Detach())
        {
            var claim = new BsonDocument { { "findAndModify", "orders" }, { "query", new BsonDocument("status", "Pending") }, { "update", new BsonDocument("$set", new BsonDocument("status", "Processing")) } };
            subscriber.OnCommandStarted(MakeStartedEvent("findAndModify", claim, requestId: 2));
            subscriber.OnCommandSucceeded(MakeSucceededEvent("findAndModify", requestId: 2,
                reply: new BsonDocument { { "ok", 1 }, { "value", new BsonDocument { { "_id", "order-1" }, { "status", "Pending" } } } }));
            Dispatch("first");
            subscriber.OnCommandStarted(MakeStartedEvent("update", UpdateById("order-1"), requestId: 3));
            subscriber.OnCommandSucceeded(MakeSucceededEvent("update", requestId: 3));

            subscriber.OnCommandStarted(MakeStartedEvent("findAndModify", claim, requestId: 4));
            subscriber.OnCommandSucceeded(MakeSucceededEvent("findAndModify", requestId: 4, reply: new BsonDocument { { "ok", 1 }, { "value", BsonNull.Value } }));
        }

        var logs = GetLogsFromThisTest();
        Assert.Equal([AttributionSource.TestContext, AttributionSource.DocumentOwner, AttributionSource.DocumentFlow, AttributionSource.DocumentOwner], Sources(logs));
        Assert.Equal("http://dispatcher/first", logs.Where(l => l.Type == RequestResponseType.Request).ElementAt(2).Uri.ToString());
        Assert.True(logs[2].Timestamp <= logs[3].Timestamp, "the claim's request keeps the time it started");
    }

    [Fact]
    public void An_identity_less_write_never_becomes_the_documents_owner()
    {
        var id = "bg-" + Guid.NewGuid().ToString("N");
        var options = MakeOptions();
        RequestResponseLogger.CaptureBackground = true;
        try
        {
            var subscriber = new MongoDbTrackingSubscriber(options);
            using (TestIdentityScope.Detach())
            {
                subscriber.OnCommandStarted(MakeStartedEvent("update", UpdateById(id), requestId: 2));
                subscriber.OnCommandSucceeded(MakeSucceededEvent("update", requestId: 2));
                subscriber.OnCommandStarted(MakeStartedEvent("update", UpdateById(id), requestId: 3));
                subscriber.OnCommandSucceeded(MakeSucceededEvent("update", requestId: 3));
            }
        }
        finally
        {
            RequestResponseLogger.CaptureBackground = false;
        }

        Assert.Null(TestCorrelationStore.Lookup(CorrelationKeys.Mongo(options.ServiceName, id)));
    }
}
