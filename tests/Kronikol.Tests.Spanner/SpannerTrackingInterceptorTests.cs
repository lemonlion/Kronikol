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

    [Fact]
    public async Task UnaryCall_RpcException_LogsNullContent()
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

        await Assert.ThrowsAsync<RpcException>(() => call.ResponseAsync);

        var logs = GetLogs();
        var responseLog = logs.Last();
        Assert.Equal(RequestResponseType.Response, responseLog.Type);
        Assert.Null(responseLog.Content);
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
    public void StreamingCall_LogsNullContent_WhenLogResponseContentFalse()
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

        var fakeStream = new FakeAsyncStreamReader<PartialResultSet>([]);
        var originalCall = new AsyncServerStreamingCall<PartialResultSet>(
            fakeStream,
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

        interceptor.AsyncServerStreamingCall(request, context,
            (req, ctx) => originalCall);

        var logs = GetLogs();
        var responseLog = logs.Last();
        Assert.Null(responseLog.Content);
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
        private int _index = -1;

        public FakeAsyncStreamReader(IReadOnlyList<T> items) => _items = items;

        public T Current => _items[_index];

        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            _index++;
            return Task.FromResult(_index < _items.Count);
        }
    }
}
