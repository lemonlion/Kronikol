using Google.Cloud.Spanner.V1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Kronikol.Extensions.Spanner;
using Kronikol.Tracking;
using TypeCode = Google.Cloud.Spanner.V1.TypeCode;

namespace Kronikol.Tests.Spanner;

public class SpannerTrackingInterceptorTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] GetLogs() =>
        RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == _testId)
            .ToArray();

    private SpannerTrackingOptions MakeOptions(
        SpannerTrackingVerbosity verbosity = SpannerTrackingVerbosity.Detailed) => new()
    {
        ServiceName = "Spanner",
        CallerName = "TestCaller",
        Verbosity = verbosity,
        CurrentTestInfoFetcher = () => ("Interceptor Test", _testId),
    };

    private SpannerTrackingInterceptor CreateInterceptor(SpannerTrackingOptions? options = null) =>
        new(options ?? MakeOptions());

    // ─── Helpers for faking gRPC calls ──────────────────────

    private static Method<TRequest, TResponse> MakeMethod<TRequest, TResponse>(
        string serviceName, string methodName, MethodType type = MethodType.Unary)
        where TRequest : class where TResponse : class
    {
        return new Method<TRequest, TResponse>(
            type, serviceName, methodName,
            Marshallers.Create<TRequest>(
                msg => ((IMessage)msg).ToByteArray(),
                bytes => default!),
            Marshallers.Create<TResponse>(
                msg => ((IMessage)msg).ToByteArray(),
                bytes => default!));
    }

    private static AsyncUnaryCall<TResponse> FakeUnaryCall<TResponse>(TResponse response)
    {
        return new AsyncUnaryCall<TResponse>(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
    }

    // ─── AsyncUnaryCall Tests ────────────────────────────────

    // ─── LogSqlText, and content per phase, as the ADO.NET command has them ───

    private RequestResponseLog ExecuteSql(SpannerTrackingOptions options, string sql)
    {
        var interceptor = CreateInterceptor(options);
        var request = new ExecuteSqlRequest { Sql = sql, Session = "projects/p/instances/i/databases/d/sessions/s" };
        var method = MakeMethod<ExecuteSqlRequest, ResultSet>("google.spanner.v1.Spanner", "ExecuteSql");
        interceptor.AsyncUnaryCall(request, new ClientInterceptorContext<ExecuteSqlRequest, ResultSet>(method, null, new CallOptions()),
            (_, _) => FakeUnaryCall(new ResultSet()));
        return Assert.Single(GetLogs(), l => l.Type == RequestResponseType.Request);
    }

    [Fact]
    public void With_LogSqlText_off_a_statement_is_logged_without_its_SQL_at_Detailed_as_a_command_is()
    {
        // TrackingSpannerCommand leaves the SQL text out at Detailed when LogSqlText is off; the interceptor ignored it.
        var options = MakeOptions();
        options.LogSqlText = false;

        Assert.Null(ExecuteSql(options, "SELECT * FROM Users").Content);
    }

    [Fact]
    public void With_LogSqlText_off_a_statement_still_carries_its_SQL_at_Raw_as_a_command_does()
    {
        var options = MakeOptions(SpannerTrackingVerbosity.Raw);
        options.LogSqlText = false;

        Assert.Equal("SELECT * FROM Users", ExecuteSql(options, "SELECT * FROM Users").Content);
    }

    [Theory]
    [InlineData(SpannerTrackingVerbosity.Detailed, null)]
    [InlineData(SpannerTrackingVerbosity.Raw, "SELECT * FROM Users")]
    public void With_LogSqlText_off_a_streamed_statement_is_logged_as_a_unary_one_is(SpannerTrackingVerbosity verbosity, string? expected)
    {
        // The streaming call logs its request at a call site of its own.
        var options = MakeOptions(verbosity);
        options.LogSqlText = false;
        var interceptor = CreateInterceptor(options);
        var request = new ExecuteSqlRequest { Sql = "SELECT * FROM Users", Session = "projects/p/instances/i/databases/d/sessions/s" };
        var method = MakeMethod<ExecuteSqlRequest, PartialResultSet>("google.spanner.v1.Spanner", "ExecuteStreamingSql", MethodType.ServerStreaming);
        var originalCall = new AsyncServerStreamingCall<PartialResultSet>(
            new FakeAsyncStreamReader<PartialResultSet>([]), Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess, () => new Metadata(), () => { });

        interceptor.AsyncServerStreamingCall(request,
            new ClientInterceptorContext<ExecuteSqlRequest, PartialResultSet>(method, null, new CallOptions()),
            (_, _) => originalCall);

        Assert.Equal(expected, Assert.Single(GetLogs(), l => l.Type == RequestResponseType.Request).Content);
    }

    [Fact]
    public void A_phase_whose_verbosity_shows_content_gets_it_when_the_base_verbosity_is_Summarised()
    {
        // Variants are attached while the phase is not yet known; the interceptor computed the content once, at the
        // base verbosity, and so gave the action phase none.
        TestPhaseContext.Reset();
        var options = MakeOptions(SpannerTrackingVerbosity.Summarised);
        options.ActionVerbosity = SpannerTrackingVerbosity.Detailed;

        var log = ExecuteSql(options, "SELECT * FROM Orders");

        Assert.Null(log.Content);
        Assert.Equal("SELECT * FROM Orders", log.ActionVariant?.Content);
    }


    [Fact]
    public void AsyncUnaryCall_ExecuteSql_Logs_request_and_response()
    {
        var interceptor = CreateInterceptor();
        var request = new ExecuteSqlRequest
        {
            Sql = "SELECT * FROM Users WHERE Id = @id",
            Session = "projects/p/instances/i/databases/d/sessions/s"
        };
        var response = new ResultSet();
        var method = MakeMethod<ExecuteSqlRequest, ResultSet>(
            "google.spanner.v1.Spanner", "ExecuteSql");
        var context = new ClientInterceptorContext<ExecuteSqlRequest, ResultSet>(
            method, null, new CallOptions());

        interceptor.AsyncUnaryCall(request, context, (req, ctx) => FakeUnaryCall(response));

        // Wait for the response task
        var logs = GetLogs();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
    }

    [Fact]
    public void AsyncUnaryCall_ExecuteSql_Extracts_sql_text()
    {
        var interceptor = CreateInterceptor();
        var request = new ExecuteSqlRequest
        {
            Sql = "SELECT Name FROM Users",
            Session = "projects/p/instances/i/databases/d/sessions/s"
        };
        var method = MakeMethod<ExecuteSqlRequest, ResultSet>(
            "google.spanner.v1.Spanner", "ExecuteSql");
        var context = new ClientInterceptorContext<ExecuteSqlRequest, ResultSet>(
            method, null, new CallOptions());

        interceptor.AsyncUnaryCall(request, context,
            (req, ctx) => FakeUnaryCall(new ResultSet()));

        var log = GetLogs().First();
        Assert.Contains("SELECT Name FROM Users", log.Content);
    }

    [Fact]
    public void AsyncUnaryCall_Read_Extracts_table_name()
    {
        var interceptor = CreateInterceptor();
        var request = new ReadRequest
        {
            Table = "Orders",
            Session = "projects/p/instances/i/databases/d/sessions/s"
        };
        var method = MakeMethod<ReadRequest, ResultSet>(
            "google.spanner.v1.Spanner", "Read");
        var context = new ClientInterceptorContext<ReadRequest, ResultSet>(
            method, null, new CallOptions());

        interceptor.AsyncUnaryCall(request, context,
            (req, ctx) => FakeUnaryCall(new ResultSet()));

        var log = GetLogs().First();
        Assert.Contains("Orders", log.Method.Value?.ToString());
    }

    [Fact]
    public void AsyncUnaryCall_Commit_Extracts_mutation_table_names()
    {
        var interceptor = CreateInterceptor();
        var request = new CommitRequest
        {
            Session = "projects/p/instances/i/databases/d/sessions/s"
        };
        request.Mutations.Add(new Mutation
        {
            Insert = new Mutation.Types.Write { Table = "Feedback" }
        });
        var method = MakeMethod<CommitRequest, CommitResponse>(
            "google.spanner.v1.Spanner", "Commit");
        var context = new ClientInterceptorContext<CommitRequest, CommitResponse>(
            method, null, new CallOptions());

        interceptor.AsyncUnaryCall(request, context,
            (req, ctx) => FakeUnaryCall(new CommitResponse()));

        var log = GetLogs().First();
        Assert.Contains("Feedback", log.Content ?? log.Method.Value?.ToString());
    }

    [Fact]
    public void AsyncUnaryCall_Commit_Multiple_mutations_extracts_all_tables()
    {
        var interceptor = CreateInterceptor();
        var request = new CommitRequest
        {
            Session = "projects/p/instances/i/databases/d/sessions/s"
        };
        request.Mutations.Add(new Mutation
        {
            Insert = new Mutation.Types.Write { Table = "Orders" }
        });
        request.Mutations.Add(new Mutation
        {
            InsertOrUpdate = new Mutation.Types.Write { Table = "Preferences" }
        });
        var method = MakeMethod<CommitRequest, CommitResponse>(
            "google.spanner.v1.Spanner", "Commit");
        var context = new ClientInterceptorContext<CommitRequest, CommitResponse>(
            method, null, new CallOptions());

        interceptor.AsyncUnaryCall(request, context,
            (req, ctx) => FakeUnaryCall(new CommitResponse()));

        var log = GetLogs().First();
        var content = log.Content ?? log.Method.Value?.ToString();
        Assert.Contains("Orders", content);
        Assert.Contains("Preferences", content);
    }

    [Fact]
    public void AsyncUnaryCall_ExcludedOperation_No_logs()
    {
        var options = MakeOptions();
        options.ExcludedOperations = [SpannerOperation.CreateSession];
        var interceptor = CreateInterceptor(options);
        var request = new CreateSessionRequest
        {
            Database = "projects/p/instances/i/databases/d"
        };
        var method = MakeMethod<CreateSessionRequest, Session>(
            "google.spanner.v1.Spanner", "CreateSession");
        var context = new ClientInterceptorContext<CreateSessionRequest, Session>(
            method, null, new CallOptions());

        interceptor.AsyncUnaryCall(request, context,
            (req, ctx) => FakeUnaryCall(new Session()));

        Assert.Empty(GetLogs());
    }

    [Fact]
    public void AsyncUnaryCall_NoTestInfo_Still_forwards_call()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        var interceptor = CreateInterceptor(options);
        var request = new ExecuteSqlRequest { Sql = "SELECT 1" };
        var method = MakeMethod<ExecuteSqlRequest, ResultSet>(
            "google.spanner.v1.Spanner", "ExecuteSql");
        var context = new ClientInterceptorContext<ExecuteSqlRequest, ResultSet>(
            method, null, new CallOptions());
        var called = false;

        interceptor.AsyncUnaryCall(request, context, (req, ctx) =>
        {
            called = true;
            return FakeUnaryCall(new ResultSet());
        });

        Assert.True(called);
        Assert.Empty(GetLogs());
    }

    [Fact]
    public void AsyncUnaryCall_Uses_Database_dependency_category()
    {
        var interceptor = CreateInterceptor();
        var request = new ExecuteSqlRequest { Sql = "SELECT 1" };
        var method = MakeMethod<ExecuteSqlRequest, ResultSet>(
            "google.spanner.v1.Spanner", "ExecuteSql");
        var context = new ClientInterceptorContext<ExecuteSqlRequest, ResultSet>(
            method, null, new CallOptions());

        interceptor.AsyncUnaryCall(request, context,
            (req, ctx) => FakeUnaryCall(new ResultSet()));

        var log = GetLogs().First();
        Assert.Equal("Spanner", log.DependencyCategory);
    }

    [Fact]
    public void AsyncUnaryCall_Summarised_Omits_content()
    {
        var interceptor = CreateInterceptor(MakeOptions(SpannerTrackingVerbosity.Summarised));
        var request = new ExecuteSqlRequest { Sql = "SELECT * FROM Users" };
        var method = MakeMethod<ExecuteSqlRequest, ResultSet>(
            "google.spanner.v1.Spanner", "ExecuteSql");
        var context = new ClientInterceptorContext<ExecuteSqlRequest, ResultSet>(
            method, null, new CallOptions());

        interceptor.AsyncUnaryCall(request, context,
            (req, ctx) => FakeUnaryCall(new ResultSet()));

        var log = GetLogs().First();
        Assert.Null(log.Content);
        Assert.Equal("SELECT", log.Method.Value?.ToString());
    }

    [Fact]
    public void AsyncUnaryCall_Pairs_request_and_response_traceIds()
    {
        var interceptor = CreateInterceptor();
        var request = new ExecuteSqlRequest
        {
            Sql = "SELECT 1",
            Session = "projects/p/instances/i/databases/d/sessions/s"
        };
        var method = MakeMethod<ExecuteSqlRequest, ResultSet>(
            "google.spanner.v1.Spanner", "ExecuteSql");
        var context = new ClientInterceptorContext<ExecuteSqlRequest, ResultSet>(
            method, null, new CallOptions());

        interceptor.AsyncUnaryCall(request, context,
            (req, ctx) => FakeUnaryCall(new ResultSet()));

        var logs = GetLogs();
        Assert.Equal(2, logs.Length);
        Assert.Equal(logs[0].TraceId, logs[1].TraceId);
        Assert.Equal(logs[0].RequestResponseId, logs[1].RequestResponseId);
    }

    // ─── ITrackingComponent ─────────────────────────────────

    [Fact]
    public void Implements_ITrackingComponent()
    {
        var interceptor = CreateInterceptor();
        Assert.IsAssignableFrom<ITrackingComponent>(interceptor);
    }

    [Fact]
    public void WasInvoked_false_before_calls()
    {
        var interceptor = CreateInterceptor();
        Assert.False(interceptor.WasInvoked);
    }

    [Fact]
    public void WasInvoked_true_after_call()
    {
        var interceptor = CreateInterceptor();
        var request = new ExecuteSqlRequest { Sql = "SELECT 1" };
        var method = MakeMethod<ExecuteSqlRequest, ResultSet>(
            "google.spanner.v1.Spanner", "ExecuteSql");
        var context = new ClientInterceptorContext<ExecuteSqlRequest, ResultSet>(
            method, null, new CallOptions());

        interceptor.AsyncUnaryCall(request, context,
            (req, ctx) => FakeUnaryCall(new ResultSet()));

        Assert.True(interceptor.WasInvoked);
    }

    [Fact]
    public void InvocationCount_increments()
    {
        var interceptor = CreateInterceptor();
        var method = MakeMethod<ExecuteSqlRequest, ResultSet>(
            "google.spanner.v1.Spanner", "ExecuteSql");
        var context = new ClientInterceptorContext<ExecuteSqlRequest, ResultSet>(
            method, null, new CallOptions());

        interceptor.AsyncUnaryCall(new ExecuteSqlRequest { Sql = "s1" }, context,
            (req, ctx) => FakeUnaryCall(new ResultSet()));
        interceptor.AsyncUnaryCall(new ExecuteSqlRequest { Sql = "s2" }, context,
            (req, ctx) => FakeUnaryCall(new ResultSet()));

        Assert.Equal(2, interceptor.InvocationCount);
    }

    [Fact]
    public void ComponentName_includes_service_name()
    {
        var interceptor = CreateInterceptor();
        Assert.Contains("Spanner", interceptor.ComponentName);
    }

    // ─── URI ────────────────────────────────────────────────

    [Fact]
    public void AsyncUnaryCall_URI_uses_spanner_scheme()
    {
        var interceptor = CreateInterceptor();
        var request = new ExecuteSqlRequest
        {
            Sql = "SELECT * FROM Users",
            Session = "projects/p/instances/i/databases/d/sessions/s"
        };
        var method = MakeMethod<ExecuteSqlRequest, ResultSet>(
            "google.spanner.v1.Spanner", "ExecuteSql");
        var context = new ClientInterceptorContext<ExecuteSqlRequest, ResultSet>(
            method, null, new CallOptions());

        interceptor.AsyncUnaryCall(request, context,
            (req, ctx) => FakeUnaryCall(new ResultSet()));

        var log = GetLogs().First();
        Assert.StartsWith("spanner://", log.Uri.ToString());
    }

    // ─── Mutation type extraction ──────────────────────────

    [Fact]
    public void AsyncUnaryCall_Commit_InsertOrUpdate_classified_correctly()
    {
        var interceptor = CreateInterceptor();
        var request = new CommitRequest
        {
            Session = "projects/p/instances/i/databases/d/sessions/s"
        };
        request.Mutations.Add(new Mutation
        {
            InsertOrUpdate = new Mutation.Types.Write { Table = "Preferences" }
        });
        var method = MakeMethod<CommitRequest, CommitResponse>(
            "google.spanner.v1.Spanner", "Commit");
        var context = new ClientInterceptorContext<CommitRequest, CommitResponse>(
            method, null, new CallOptions());

        interceptor.AsyncUnaryCall(request, context,
            (req, ctx) => FakeUnaryCall(new CommitResponse()));

        var log = GetLogs().First();
        Assert.Contains("Preferences", log.Content ?? log.Method.Value?.ToString());
    }

    [Fact]
    public void AsyncUnaryCall_Commit_Delete_mutation_classified()
    {
        var interceptor = CreateInterceptor();
        var request = new CommitRequest
        {
            Session = "projects/p/instances/i/databases/d/sessions/s"
        };
        request.Mutations.Add(new Mutation
        {
            Delete = new Mutation.Types.Delete { Table = "Feedback" }
        });
        var method = MakeMethod<CommitRequest, CommitResponse>(
            "google.spanner.v1.Spanner", "Commit");
        var context = new ClientInterceptorContext<CommitRequest, CommitResponse>(
            method, null, new CallOptions());

        interceptor.AsyncUnaryCall(request, context,
            (req, ctx) => FakeUnaryCall(new CommitResponse()));

        var log = GetLogs().First();
        Assert.Contains("Feedback", log.Content ?? log.Method.Value?.ToString());
    }

    // ─── BatchDml extraction ───────────────────────────────

    [Fact]
    public void AsyncUnaryCall_ExecuteBatchDml_extracts_sql_statements()
    {
        var interceptor = CreateInterceptor();
        var request = new ExecuteBatchDmlRequest
        {
            Session = "projects/p/instances/i/databases/d/sessions/s"
        };
        request.Statements.Add(new ExecuteBatchDmlRequest.Types.Statement { Sql = "INSERT INTO Orders VALUES (@id)" });
        request.Statements.Add(new ExecuteBatchDmlRequest.Types.Statement { Sql = "UPDATE Stock SET Qty = Qty - 1" });
        var method = MakeMethod<ExecuteBatchDmlRequest, ExecuteBatchDmlResponse>(
            "google.spanner.v1.Spanner", "ExecuteBatchDml");
        var context = new ClientInterceptorContext<ExecuteBatchDmlRequest, ExecuteBatchDmlResponse>(
            method, null, new CallOptions());

        interceptor.AsyncUnaryCall(request, context,
            (req, ctx) => FakeUnaryCall(new ExecuteBatchDmlResponse()));

        var log = GetLogs().First();
        Assert.Contains("INSERT INTO Orders", log.Content!);
        Assert.Contains("UPDATE Stock", log.Content!);
    }

    // ─── Response content capture ──────────────────────────

    [Fact]
    public void UnaryCall_LogsResponseContent_WhenLogResponseContentTrue()
    {
        var options = MakeOptions();
        options.LogResponseContent = true;
        var interceptor = CreateInterceptor(options);
        var request = new ExecuteSqlRequest
        {
            Sql = "SELECT Name FROM Users",
            Session = "projects/p/instances/i/databases/d/sessions/s"
        };
        var response = MakeResultSetResponse(["Name"], [["Alice"], ["Bob"]]);
        var method = MakeMethod<ExecuteSqlRequest, ResultSet>(
            "google.spanner.v1.Spanner", "ExecuteSql");
        var context = new ClientInterceptorContext<ExecuteSqlRequest, ResultSet>(
            method, null, new CallOptions());

        interceptor.AsyncUnaryCall(request, context, (req, ctx) => FakeUnaryCall(response));

        var logs = GetLogs();
        var responseLog = logs.Last();
        Assert.Equal(RequestResponseType.Response, responseLog.Type);
        Assert.NotNull(responseLog.Content);
        // Unset ResponseDetail at Detailed verbosity captures the actual rows.
        Assert.Contains("Alice", responseLog.Content!);
        Assert.Contains("Bob", responseLog.Content!);
    }

    [Fact]
    public void UnaryCall_LogsNullContent_WhenLogResponseContentFalse()
    {
        var options = MakeOptions();
        options.LogResponseContent = false;
        var interceptor = CreateInterceptor(options);
        var request = new ExecuteSqlRequest
        {
            Sql = "SELECT Name FROM Users",
            Session = "projects/p/instances/i/databases/d/sessions/s"
        };
        var response = MakeResultSetResponse(["Name"], [["Alice"]]);
        var method = MakeMethod<ExecuteSqlRequest, ResultSet>(
            "google.spanner.v1.Spanner", "ExecuteSql");
        var context = new ClientInterceptorContext<ExecuteSqlRequest, ResultSet>(
            method, null, new CallOptions());

        interceptor.AsyncUnaryCall(request, context, (req, ctx) => FakeUnaryCall(response));

        var logs = GetLogs();
        var responseLog = logs.Last();
        Assert.Null(responseLog.Content);
    }

    [Fact]
    public void UnaryCall_CommitResponse_LogsCommitTimestamp()
    {
        var options = MakeOptions();
        var interceptor = CreateInterceptor(options);
        var request = new CommitRequest
        {
            Session = "projects/p/instances/i/databases/d/sessions/s"
        };
        request.Mutations.Add(new Mutation
        {
            Insert = new Mutation.Types.Write { Table = "Users" }
        });
        var response = new CommitResponse
        {
            CommitTimestamp = Timestamp.FromDateTimeOffset(
                new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero))
        };
        var method = MakeMethod<CommitRequest, CommitResponse>(
            "google.spanner.v1.Spanner", "Commit");
        var context = new ClientInterceptorContext<CommitRequest, CommitResponse>(
            method, null, new CallOptions());

        interceptor.AsyncUnaryCall(request, context, (req, ctx) => FakeUnaryCall(response));

        var logs = GetLogs();
        var responseLog = logs.Last();
        Assert.Contains("2026-05-15", responseLog.Content!);
    }

    [Fact]
    public void UnaryCall_ResultSet_LogsRowCountAndColumns()
    {
        var options = MakeOptions();
        options.ResponseDetail = SpannerResponseDetail.RowCountAndColumns;
        var interceptor = CreateInterceptor(options);
        var request = new ExecuteSqlRequest
        {
            Sql = "SELECT Name, Age FROM Users",
            Session = "projects/p/instances/i/databases/d/sessions/s"
        };
        var response = MakeResultSetResponse(["Name", "Age"], [["Alice", "30"]]);
        var method = MakeMethod<ExecuteSqlRequest, ResultSet>(
            "google.spanner.v1.Spanner", "ExecuteSql");
        var context = new ClientInterceptorContext<ExecuteSqlRequest, ResultSet>(
            method, null, new CallOptions());

        interceptor.AsyncUnaryCall(request, context, (req, ctx) => FakeUnaryCall(response));

        var logs = GetLogs();
        var responseLog = logs.Last();
        Assert.Contains("1 row", responseLog.Content!);
        Assert.Contains("Name", responseLog.Content!);
        Assert.Contains("Age", responseLog.Content!);
    }

    // A phase's own verbosity decides its note's detail, though the call was made before its phase was known: a
    // Detailed phase of a Summarised interceptor lists the rows, and a Summarised phase of a Detailed one counts them.
    private RequestResponseLog ExecuteSqlAnswered(SpannerTrackingOptions options, ResultSet response)
    {
        var interceptor = CreateInterceptor(options);
        var request = new ExecuteSqlRequest { Sql = "SELECT Name FROM Users", Session = "projects/p/instances/i/databases/d/sessions/s" };
        var method = MakeMethod<ExecuteSqlRequest, ResultSet>("google.spanner.v1.Spanner", "ExecuteSql");
        interceptor.AsyncUnaryCall(request, new ClientInterceptorContext<ExecuteSqlRequest, ResultSet>(method, null, new CallOptions()),
            (_, _) => FakeUnaryCall(response));
        return Assert.Single(GetLogs(), l => l.Type == RequestResponseType.Response);
    }

    [Fact]
    public void A_detailed_phase_of_a_summarised_interceptor_lists_the_rows()
    {
        TestPhaseContext.Reset();
        var options = MakeOptions(SpannerTrackingVerbosity.Summarised);
        options.ActionVerbosity = SpannerTrackingVerbosity.Detailed;

        var response = ExecuteSqlAnswered(options, MakeResultSetResponse(["Name"], [["Alice"], ["Bob"]]));

        Assert.Equal("2 rows [Name]", response.Content);
        Assert.Equal("2 rows [Name]", response.SetupVariant!.Content);
        Assert.Contains("Alice", response.ActionVariant!.Content);
    }

    [Fact]
    public void A_summarised_phase_of_a_detailed_interceptor_counts_the_rows()
    {
        TestPhaseContext.Reset();
        var options = MakeOptions();
        options.SetupVerbosity = SpannerTrackingVerbosity.Summarised;

        var response = ExecuteSqlAnswered(options, MakeResultSetResponse(["Name"], [["Alice"], ["Bob"]]));

        Assert.Contains("Alice", response.Content);
        Assert.Equal("2 rows [Name]", response.SetupVariant!.Content);
        Assert.Contains("Alice", response.ActionVariant!.Content);
    }

    // Reads a stream of two names to its end, calling whileReading after each chunk.
    private async Task<RequestResponseLog> ReadNamesStream(SpannerTrackingOptions options, Action? whileReading = null)
    {
        var interceptor = CreateInterceptor(options);
        var request = new ExecuteSqlRequest { Sql = "SELECT Name FROM Users", Session = "projects/p/instances/i/databases/d/sessions/s" };
        var method = MakeMethod<ExecuteSqlRequest, PartialResultSet>("google.spanner.v1.Spanner", "ExecuteStreamingSql", MethodType.ServerStreaming);
        var chunk = new PartialResultSet
        {
            Metadata = new ResultSetMetadata
            {
                RowType = new StructType
                {
                    Fields = { new StructType.Types.Field { Name = "Name", Type = new Google.Cloud.Spanner.V1.Type { Code = TypeCode.String } } }
                }
            },
            Values = { Value.ForString("Alice"), Value.ForString("Bob") }
        };
        var originalCall = new AsyncServerStreamingCall<PartialResultSet>(
            new FakeAsyncStreamReader<PartialResultSet>([chunk]), Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess, () => new Metadata(), () => { });

        var wrappedCall = interceptor.AsyncServerStreamingCall(request,
            new ClientInterceptorContext<ExecuteSqlRequest, PartialResultSet>(method, null, new CallOptions()), (_, _) => originalCall);
        while (await wrappedCall.ResponseStream.MoveNext(CancellationToken.None))
            whileReading?.Invoke();

        return Assert.Single(GetLogs(), l => l.Type == RequestResponseType.Response);
    }

    [Fact]
    public async Task A_detailed_phase_of_a_summarised_interceptor_lists_the_rows_a_stream_read()
    {
        TestPhaseContext.Reset();
        var options = MakeOptions(SpannerTrackingVerbosity.Summarised);
        options.ActionVerbosity = SpannerTrackingVerbosity.Detailed;

        var response = await ReadNamesStream(options);

        Assert.Equal("2 rows [Name]", response.Content);
        Assert.Equal("2 rows [Name]", response.SetupVariant!.Content);
        Assert.Contains("Alice", response.ActionVariant!.Content);
    }

    // A stream keeps every row it reads, whatever phase it starts in, so one started in a Summarised phase and read to
    // its end in a Detailed one lists the rows the Detailed note asks for.
    [Fact]
    public async Task A_stream_started_in_a_summarised_phase_and_ended_in_a_detailed_one_lists_the_rows()
    {
        var options = MakeOptions();
        options.SetupVerbosity = SpannerTrackingVerbosity.Summarised;
        try
        {
            TestPhaseContext.Current = TestPhase.Setup;

            var response = await ReadNamesStream(options, whileReading: () => TestPhaseContext.Current = TestPhase.Action);

            Assert.Contains("Alice", response.Content);
        }
        finally
        {
            TestPhaseContext.Reset();
        }
    }

    [Fact]
    public async Task UnaryCall_RpcException_LogsAnErrorResponseWithTheCallsMessage()
    {
        var options = MakeOptions();
        var interceptor = CreateInterceptor(options);
        var request = new ExecuteSqlRequest
        {
            Sql = "SELECT * FROM Users",
            Session = "projects/p/instances/i/databases/d/sessions/s"
        };
        var method = MakeMethod<ExecuteSqlRequest, ResultSet>(
            "google.spanner.v1.Spanner", "ExecuteSql");
        var context = new ClientInterceptorContext<ExecuteSqlRequest, ResultSet>(
            method, null, new CallOptions());

        var rpcEx = new RpcException(new Status(StatusCode.NotFound, "Not found"));
        var call = interceptor.AsyncUnaryCall(request, context,
            (req, ctx) => new AsyncUnaryCall<ResultSet>(
                Task.FromException<ResultSet>(rpcEx),
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.NotFound, "Not found"),
                () => new Metadata(),
                () => { }));

        var thrown = await Assert.ThrowsAsync<RpcException>(() => call.ResponseAsync);

        Assert.Same(rpcEx, thrown);
        var logs = GetLogs();
        var responseLog = logs.Last();
        Assert.Equal(RequestResponseType.Response, responseLog.Type);
        Assert.Equal("Error", responseLog.StatusCode?.Value?.ToString());
        Assert.Equal(rpcEx.Message, responseLog.Content);
    }

    [Fact]
    public void ResponseDetail_RowCountOnly_AffectsOutput()
    {
        var options = MakeOptions();
        options.ResponseDetail = SpannerResponseDetail.RowCountOnly;
        var interceptor = CreateInterceptor(options);
        var request = new ExecuteSqlRequest
        {
            Sql = "SELECT Name FROM Users",
            Session = "projects/p/instances/i/databases/d/sessions/s"
        };
        var response = MakeResultSetResponse(["Name"], [["Alice"]]);
        var method = MakeMethod<ExecuteSqlRequest, ResultSet>(
            "google.spanner.v1.Spanner", "ExecuteSql");
        var context = new ClientInterceptorContext<ExecuteSqlRequest, ResultSet>(
            method, null, new CallOptions());

        interceptor.AsyncUnaryCall(request, context, (req, ctx) => FakeUnaryCall(response));

        var logs = GetLogs();
        var responseLog = logs.Last();
        Assert.Equal("1 row", responseLog.Content);
    }

    [Fact]
    public void ResponseDetail_FullRows_AffectsOutput()
    {
        var options = MakeOptions();
        options.ResponseDetail = SpannerResponseDetail.FullRows;
        var interceptor = CreateInterceptor(options);
        var request = new ExecuteSqlRequest
        {
            Sql = "SELECT Name FROM Users",
            Session = "projects/p/instances/i/databases/d/sessions/s"
        };
        var response = MakeResultSetResponse(["Name"], [["Alice"]]);
        var method = MakeMethod<ExecuteSqlRequest, ResultSet>(
            "google.spanner.v1.Spanner", "ExecuteSql");
        var context = new ClientInterceptorContext<ExecuteSqlRequest, ResultSet>(
            method, null, new CallOptions());

        interceptor.AsyncUnaryCall(request, context, (req, ctx) => FakeUnaryCall(response));

        var logs = GetLogs();
        var responseLog = logs.Last();
        Assert.Contains("Alice", responseLog.Content!);
    }

    [Fact]
    public void MaxResponseRows_Respected()
    {
        var options = MakeOptions();
        options.ResponseDetail = SpannerResponseDetail.FullRows;
        options.MaxResponseRows = 1;
        var interceptor = CreateInterceptor(options);
        var request = new ExecuteSqlRequest
        {
            Sql = "SELECT Name FROM Users",
            Session = "projects/p/instances/i/databases/d/sessions/s"
        };
        var response = MakeResultSetResponse(["Name"], [["Alice"], ["Bob"], ["Carol"]]);
        var method = MakeMethod<ExecuteSqlRequest, ResultSet>(
            "google.spanner.v1.Spanner", "ExecuteSql");
        var context = new ClientInterceptorContext<ExecuteSqlRequest, ResultSet>(
            method, null, new CallOptions());

        interceptor.AsyncUnaryCall(request, context, (req, ctx) => FakeUnaryCall(response));

        var logs = GetLogs();
        var responseLog = logs.Last();
        Assert.Contains("Alice", responseLog.Content!);
        Assert.DoesNotContain("Bob", responseLog.Content!);
        Assert.Contains("... (2 more rows not shown)", responseLog.Content!);
    }

    // ─── Streaming response tests ──────────────────────────

    [Fact]
    public void StreamingCall_WrapsStream_WhenLogResponseContentTrue()
    {
        var options = MakeOptions();
        options.LogResponseContent = true;
        var interceptor = CreateInterceptor(options);
        var request = new ExecuteSqlRequest
        {
            Sql = "SELECT Name FROM Users",
            Session = "projects/p/instances/i/databases/d/sessions/s"
        };
        var method = MakeMethod<ExecuteSqlRequest, PartialResultSet>(
            "google.spanner.v1.Spanner", "ExecuteStreamingSql",
            MethodType.ServerStreaming);
        var context = new ClientInterceptorContext<ExecuteSqlRequest, PartialResultSet>(
            method, null, new CallOptions());

        var fakeStream = new FakeAsyncStreamReader<PartialResultSet>([]);
        var originalCall = new AsyncServerStreamingCall<PartialResultSet>(
            fakeStream,
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

        var wrappedCall = interceptor.AsyncServerStreamingCall(request, context,
            (req, ctx) => originalCall);

        // The stream should be wrapped (not the same object)
        Assert.NotSame(fakeStream, wrappedCall.ResponseStream);
    }

    [Fact]
    public async Task StreamingCall_LogsNullContent_WhenLogResponseContentFalse()
    {
        var options = MakeOptions();
        options.LogResponseContent = false;
        var interceptor = CreateInterceptor(options);
        var request = new ExecuteSqlRequest
        {
            Sql = "SELECT Name FROM Users",
            Session = "projects/p/instances/i/databases/d/sessions/s"
        };
        var method = MakeMethod<ExecuteSqlRequest, PartialResultSet>(
            "google.spanner.v1.Spanner", "ExecuteStreamingSql",
            MethodType.ServerStreaming);
        var context = new ClientInterceptorContext<ExecuteSqlRequest, PartialResultSet>(
            method, null, new CallOptions());

        var fakeStream = new FakeAsyncStreamReader<PartialResultSet>([NameChunk("Alice", "Bob")]);
        var originalCall = new AsyncServerStreamingCall<PartialResultSet>(
            fakeStream,
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

        var call = interceptor.AsyncServerStreamingCall(request, context,
            (req, ctx) => originalCall);

        // The response waits for the stream to end, as it does with content on, so a stream that fails can say so.
        Assert.DoesNotContain(GetLogs(), l => l.Type == RequestResponseType.Response);
        while (await call.ResponseStream.MoveNext(CancellationToken.None)) { }

        var responseLog = Assert.Single(GetLogs(), l => l.Type == RequestResponseType.Response);
        Assert.Null(responseLog.Content);
        Assert.Null(responseLog.StatusCode);
    }

    // 4.4.1 drew a unary call that failed with status Error and its message; a streamed read that failed was still drawn
    // as an answer, and with LogResponseContent off it was answered before its stream was read at all.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_streamed_read_that_fails_is_drawn_with_status_Error_and_its_message(bool logResponseContent)
    {
        var options = MakeOptions();
        options.LogResponseContent = logResponseContent;
        var interceptor = CreateInterceptor(options);
        var request = new ExecuteSqlRequest
        {
            Sql = "SELECT Nme FROM Users",
            Session = "projects/p/instances/i/databases/d/sessions/s"
        };
        var method = MakeMethod<ExecuteSqlRequest, PartialResultSet>(
            "google.spanner.v1.Spanner", "ExecuteStreamingSql",
            MethodType.ServerStreaming);
        var context = new ClientInterceptorContext<ExecuteSqlRequest, PartialResultSet>(
            method, null, new CallOptions());
        var rpcEx = new RpcException(new Status(StatusCode.InvalidArgument, "Unrecognized name: Nme"));
        var originalCall = new AsyncServerStreamingCall<PartialResultSet>(
            new FakeAsyncStreamReader<PartialResultSet>([], failWith: rpcEx),
            Task.FromResult(new Metadata()),
            () => new Status(StatusCode.InvalidArgument, "Unrecognized name: Nme"),
            () => new Metadata(),
            () => { });

        var call = interceptor.AsyncServerStreamingCall(request, context,
            (req, ctx) => originalCall);
        var thrown = await Assert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext(CancellationToken.None));
        call.Dispose();

        Assert.Same(rpcEx, thrown);
        var responseLog = Assert.Single(GetLogs(), l => l.Type == RequestResponseType.Response);
        Assert.Equal("Error", responseLog.StatusCode?.Value?.ToString());
        Assert.Equal(rpcEx.Message, responseLog.Content);
    }

    [Fact]
    public async Task A_streamed_read_that_fails_after_some_rows_is_drawn_with_status_Error()
    {
        var interceptor = CreateInterceptor(MakeOptions());
        var request = new ExecuteSqlRequest
        {
            Sql = "SELECT Name FROM Users",
            Session = "projects/p/instances/i/databases/d/sessions/s"
        };
        var method = MakeMethod<ExecuteSqlRequest, PartialResultSet>(
            "google.spanner.v1.Spanner", "ExecuteStreamingSql",
            MethodType.ServerStreaming);
        var context = new ClientInterceptorContext<ExecuteSqlRequest, PartialResultSet>(
            method, null, new CallOptions());
        var rpcEx = new RpcException(new Status(StatusCode.Unavailable, "Connection reset"));
        var originalCall = new AsyncServerStreamingCall<PartialResultSet>(
            new FakeAsyncStreamReader<PartialResultSet>([NameChunk("Alice")], failWith: rpcEx),
            Task.FromResult(new Metadata()),
            () => new Status(StatusCode.Unavailable, "Connection reset"),
            () => new Metadata(),
            () => { });

        var call = interceptor.AsyncServerStreamingCall(request, context,
            (req, ctx) => originalCall);
        Assert.True(await call.ResponseStream.MoveNext(CancellationToken.None));
        await Assert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext(CancellationToken.None));

        var responseLog = Assert.Single(GetLogs(), l => l.Type == RequestResponseType.Response);
        Assert.Equal("Error", responseLog.StatusCode?.Value?.ToString());
        Assert.Equal(rpcEx.Message, responseLog.Content);
    }

    private static PartialResultSet NameChunk(params string[] names)
    {
        var chunk = new PartialResultSet
        {
            Metadata = new ResultSetMetadata
            {
                RowType = new StructType
                {
                    Fields =
                    {
                        new StructType.Types.Field
                        {
                            Name = "Name",
                            Type = new Google.Cloud.Spanner.V1.Type { Code = TypeCode.String }
                        }
                    }
                }
            }
        };
        foreach (var name in names) chunk.Values.Add(Value.ForString(name));
        return chunk;
    }

    [Fact]
    public async Task StreamingCall_LogsContentAfterStreamCompletion()
    {
        var options = MakeOptions();
        options.LogResponseContent = true;
        var interceptor = CreateInterceptor(options);
        var request = new ExecuteSqlRequest
        {
            Sql = "SELECT Name FROM Users",
            Session = "projects/p/instances/i/databases/d/sessions/s"
        };
        var method = MakeMethod<ExecuteSqlRequest, PartialResultSet>(
            "google.spanner.v1.Spanner", "ExecuteStreamingSql",
            MethodType.ServerStreaming);
        var context = new ClientInterceptorContext<ExecuteSqlRequest, PartialResultSet>(
            method, null, new CallOptions());

        var chunk = new PartialResultSet
        {
            Metadata = new ResultSetMetadata
            {
                RowType = new StructType
                {
                    Fields =
                    {
                        new StructType.Types.Field
                        {
                            Name = "Name",
                            Type = new Google.Cloud.Spanner.V1.Type { Code = TypeCode.String }
                        }
                    }
                }
            },
            Values = { Value.ForString("Alice"), Value.ForString("Bob") }
        };

        var fakeStream = new FakeAsyncStreamReader<PartialResultSet>([chunk]);
        var originalCall = new AsyncServerStreamingCall<PartialResultSet>(
            fakeStream,
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

        var wrappedCall = interceptor.AsyncServerStreamingCall(request, context,
            (req, ctx) => originalCall);

        // Consume the stream
        while (await wrappedCall.ResponseStream.MoveNext(CancellationToken.None)) { }

        var logs = GetLogs();
        var responseLog = logs.Last();
        Assert.Equal(RequestResponseType.Response, responseLog.Type);
        Assert.NotNull(responseLog.Content);
        Assert.Contains("2 rows", responseLog.Content!);
    }

    [Fact]
    public async Task StreamingCall_DisposedBeforeExhaustion_LogsPartialContent()
    {
        var options = MakeOptions();
        options.LogResponseContent = true;
        var interceptor = CreateInterceptor(options);
        var request = new ExecuteSqlRequest
        {
            Sql = "SELECT Name FROM Users",
            Session = "projects/p/instances/i/databases/d/sessions/s"
        };
        var method = MakeMethod<ExecuteSqlRequest, PartialResultSet>(
            "google.spanner.v1.Spanner", "ExecuteStreamingSql",
            MethodType.ServerStreaming);
        var context = new ClientInterceptorContext<ExecuteSqlRequest, PartialResultSet>(
            method, null, new CallOptions());

        var chunk1 = new PartialResultSet
        {
            Metadata = new ResultSetMetadata
            {
                RowType = new StructType
                {
                    Fields =
                    {
                        new StructType.Types.Field
                        {
                            Name = "Name",
                            Type = new Google.Cloud.Spanner.V1.Type { Code = TypeCode.String }
                        }
                    }
                }
            },
            Values = { Value.ForString("Alice") }
        };
        var chunk2 = new PartialResultSet
        {
            Values = { Value.ForString("Bob") }
        };

        var fakeStream = new FakeAsyncStreamReader<PartialResultSet>([chunk1, chunk2]);
        var disposed = false;
        var originalCall = new AsyncServerStreamingCall<PartialResultSet>(
            fakeStream,
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { disposed = true; });

        var wrappedCall = interceptor.AsyncServerStreamingCall(request, context,
            (req, ctx) => originalCall);

        // Read only 1 chunk, then dispose
        await wrappedCall.ResponseStream.MoveNext(CancellationToken.None);
        wrappedCall.Dispose();

        Assert.True(disposed);
        var logs = GetLogs();
        var responseLog = logs.Last();
        Assert.Equal(RequestResponseType.Response, responseLog.Type);
    }

    // ─── Helpers for response tests ─────────────────────────

    private static ResultSet MakeResultSetResponse(string[] columns, string[][] rows)
    {
        var rs = new ResultSet
        {
            Metadata = new ResultSetMetadata
            {
                RowType = new StructType()
            }
        };
        foreach (var col in columns)
        {
            rs.Metadata.RowType.Fields.Add(new StructType.Types.Field
            {
                Name = col,
                Type = new Google.Cloud.Spanner.V1.Type { Code = TypeCode.String }
            });
        }
        foreach (var row in rows)
        {
            var lv = new ListValue();
            foreach (var val in row)
                lv.Values.Add(Value.ForString(val));
            rs.Rows.Add(lv);
        }
        return rs;
    }

    /// <summary>
    /// A fake IAsyncStreamReader for testing streaming calls.
    /// </summary>
    private sealed class FakeAsyncStreamReader<T> : IAsyncStreamReader<T>
    {
        private readonly IReadOnlyList<T> _items;
        private readonly Exception? _failWith;
        private int _index = -1;

        /// <param name="failWith">Thrown by the read after the last item, in place of the end of the stream.</param>
        public FakeAsyncStreamReader(IReadOnlyList<T> items, Exception? failWith = null)
        {
            _items = items;
            _failWith = failWith;
        }

        public T Current => _items[_index];

        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            _index++;
            if (_index >= _items.Count && _failWith is not null)
                return Task.FromException<bool>(_failWith);
            return Task.FromResult(_index < _items.Count);
        }
    }
}
