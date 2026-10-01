using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ClickHouse.Driver;
using Kronikol.Constants;
using Kronikol.Extensions.ClickHouse;
using Kronikol.Extensions.ClickHouse.Driver;
using Kronikol.PlantUml;
using Kronikol.Sql;
using Kronikol.Tests.ClickHouse.Fakes;
using Kronikol.Tracking;
using Xunit;

namespace Kronikol.Tests.ClickHouse;

/// <summary>
/// <see cref="TrackingClickHouseClient"/> over a fake client: what each member records. The driver behaviour a fake
/// cannot stand in for (single-pass rows held by reference, batches, the server's counts) is in
/// <see cref="ClickHouseDriverClientRealServerTests"/>.
/// </summary>
public class TrackingClickHouseClientTests : IDisposable
{
    private readonly string _testId = Guid.NewGuid().ToString();
    private readonly FakeClickHouseClient _inner = new();
    private readonly ClickHouseTrackingOptions _options;

    public TrackingClickHouseClientTests()
    {
        _options = new ClickHouseTrackingOptions { CurrentTestInfoFetcher = () => ("TestMethod", _testId) };
    }

    public void Dispose() => TestPhaseContext.Reset();

    private RequestResponseLog[] Logs()
        => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private TrackingClickHouseClient Client() => _inner.WithClickHouseDriverTestTracking(_options);

    private static IEnumerable<object[]> Rows(int count, Func<int, object[]>? row = null)
    {
        for (var i = 1; i <= count; i++)
            yield return row?.Invoke(i) ?? [(uint)i, $"item{i}"];
    }

    private static (RequestResponseLog Request, RequestResponseLog Response) Single(RequestResponseLog[] logs)
    {
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
        Assert.Equal(logs[0].RequestResponseId, logs[1].RequestResponseId);
        return (logs[0], logs[1]);
    }

    private static JsonElement[] JsonRows(string? content)
    {
        Assert.NotNull(content);
        var json = content.Split("\n... (")[0];
        return JsonDocument.Parse(json).RootElement.EnumerateArray().ToArray();
    }

    // ─── InsertBinaryAsync ──────────────────────────────────────

    [Fact]
    public async Task An_insert_is_one_call_to_INSERT_INTO_its_table_with_its_rows_as_the_body_and_the_count_it_returned()
    {
        var sent = await Client().InsertBinaryAsync("orders", ["id", "item"], [[1u, "tea"], [2u, "toast"]]);

        Assert.Equal(2, sent);
        var (request, response) = Single(Logs());
        Assert.Equal("INSERT INTO orders", request.Method.Value?.ToString());
        Assert.Equal("clickhouse://ch-host/analytics/orders", request.Uri.ToString());
        Assert.Equal("""[{"id":1,"item":"tea"},{"id":2,"item":"toast"}]""", request.Content);
        Assert.Equal("2 rows affected", response.Content);
        Assert.Equal("OK", response.StatusCode?.Value?.ToString());
        Assert.Equal(DependencyCategories.ClickHouse, request.DependencyCategory);
    }

    [Fact]
    public async Task The_request_keeps_the_time_the_insert_started_although_it_is_logged_after_the_call()
    {
        await Client().InsertBinaryAsync("orders", ["id", "item"], Rows(2));

        var (request, response) = Single(Logs());
        Assert.True(request.Timestamp <= _inner.InsertStarted, $"request {request.Timestamp:O}, insert started {_inner.InsertStarted:O}");
        Assert.True(response.Timestamp >= _inner.InsertEnded, $"response {response.Timestamp:O}, insert ended {_inner.InsertEnded:O}");
    }

    [Fact]
    public async Task Twelve_rows_show_the_first_ten_and_a_footer_for_the_rest()
    {
        await Client().InsertBinaryAsync("orders", ["id", "item"], Rows(12));

        var (request, response) = Single(Logs());
        var rows = JsonRows(request.Content);
        Assert.Equal(10, rows.Length);
        Assert.Equal(10, rows[^1].GetProperty("id").GetInt32());
        Assert.EndsWith("\n... (2 more rows not shown)", request.Content);
        Assert.Equal("12 rows affected", response.Content);
    }

    [Fact]
    public async Task A_long_value_is_cut_with_its_full_length_and_bytes_show_their_length()
    {
        var longText = new string('x', 600);
        await Client().InsertBinaryAsync("blobs", ["id", "text", "data"], [[1u, longText, new byte[] { 1, 2, 3 }]]);

        var row = JsonRows(Logs()[0].Content).Single();
        Assert.Equal(new string('x', 500) + "... (600 chars)", row.GetProperty("text").GetString());
        Assert.Equal("[bytes: 3]", row.GetProperty("data").GetString());
    }

    [Theory]
    [InlineData(SqlResponseDetail.RowCountAndColumns, "12 rows [id, item]")]
    [InlineData(SqlResponseDetail.RowCountOnly, "12 rows")]
    public async Task ResponseDetail_governs_the_inserted_rows_as_it_governs_returned_ones(SqlResponseDetail detail, string expected)
    {
        _options.ResponseDetail = detail;

        await Client().InsertBinaryAsync("orders", ["id", "item"], Rows(12));

        Assert.Equal(expected, Logs()[0].Content);
    }

    [Fact]
    public async Task Rows_sent_with_no_column_names_are_drawn_as_arrays()
    {
        await Client().InsertBinaryAsync("orders", null!, [[1u, "tea"]]);

        Assert.Equal("""[[1,"tea"]]""", Logs()[0].Content);
    }

    [Fact]
    public async Task The_rows_are_pulled_once_by_the_driver_and_never_by_the_tracker()
    {
        var starts = 0;
        IEnumerable<object[]> Source()
        {
            starts++;
            foreach (var row in Rows(3)) yield return row;
        }

        await Client().InsertBinaryAsync("orders", ["id", "item"], Source());

        Assert.Equal(1, starts);
        Assert.Equal(1, _inner.InsertEnumerations);
    }

    // The driver keeps each row's reference and serializes it when it sends (plan F4): a record taken as rows go by
    // would show 10, 11 and 12 where the server stored 12 three times.
    [Fact]
    public async Task Rows_are_formatted_after_the_call_from_the_references_the_driver_sent()
    {
        IEnumerable<object[]> Reused()
        {
            var row = new object[2];
            for (uint id = 10; id <= 12; id++)
            {
                row[0] = id;
                row[1] = $"item{id}";
                yield return row;
            }
        }

        await Client().InsertBinaryAsync("orders", ["id", "item"], Reused());

        var ids = JsonRows(Logs()[0].Content).Select(r => r.GetProperty("id").GetInt32());
        Assert.Equal(new[] { 12, 12, 12 }, ids);
    }

    private sealed class Order
    {
        public uint Id { get; init; }
        public string Item { get; init; } = "";
    }

    [Fact]
    public async Task A_typed_insert_shows_its_rows_by_property()
    {
        var sent = await Client().InsertBinaryAsync("orders", new[] { new Order { Id = 1, Item = "tea" } });

        Assert.Equal(1, sent);
        var (request, response) = Single(Logs());
        Assert.Equal("INSERT INTO orders", request.Method.Value?.ToString());
        Assert.Equal("""[{"Id":1,"Item":"tea"}]""", request.Content);
        Assert.Equal("1 rows affected", response.Content);
    }

    // T14: the diagram draws the inserted rows as indented JSON, with the cap's footer on a line of its own.
    [Fact]
    public async Task The_diagram_draws_the_inserted_rows_indented_with_the_footer_on_its_own_line()
    {
        await Client().InsertBinaryAsync("orders", ["id", "item"], Rows(12));

        var plantUml = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(Logs()).Single().PlantUmls.First().PlainText.Replace("\r\n", "\n");

        Assert.Contains("\"item\": \"item10\"", plantUml);
        Assert.DoesNotContain("""[{"id":1,""", plantUml); // the one-line raw form is gone
        Assert.Contains(plantUml.Split('\n'), l => l.Trim() == "... (2 more rows not shown)");
    }

    // ─── The SQL members ────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task ExecuteNonQuery_records_its_statement_and_no_count_since_the_client_returns_0_for_every_write(int driverResult)
    {
        _inner.NonQueryResult = driverResult;

        await Client().ExecuteNonQueryAsync("DELETE FROM orders WHERE id = 1");

        var (request, response) = Single(Logs());
        Assert.Equal("DELETE FROM orders", request.Method.Value?.ToString());
        Assert.Equal("DELETE FROM orders WHERE id = 1", request.Content);
        Assert.Null(response.Content);
        Assert.Equal("OK", response.StatusCode?.Value?.ToString());
    }

    [Fact]
    public async Task ExecuteScalar_shows_its_value()
    {
        _inner.ScalarResult = 7UL;

        var value = await Client().ExecuteScalarAsync("SELECT count() FROM orders");

        Assert.Equal(7UL, value);
        Assert.Equal("7", Single(Logs()).Response.Content);
    }

    [Fact]
    public async Task ExecuteReader_records_the_call_with_no_rows()
    {
        await Client().ExecuteReaderAsync("SELECT id FROM orders");

        var (request, response) = Single(Logs());
        Assert.Equal("SELECT FROM orders", request.Method.Value?.ToString());
        Assert.Null(response.Content);
    }

    [Fact]
    public async Task ExecuteRawResult_records_the_call_and_leaves_the_body_to_the_caller()
    {
        await Client().ExecuteRawResultAsync("SELECT id FROM orders FORMAT JSONEachRow");

        var (_, response) = Single(Logs());
        Assert.Null(response.Content);
    }

    [Fact]
    public async Task QueryAsync_records_the_rows_read_when_it_is_enumerated()
    {
        _inner.QueryRows.AddRange([new Order { Id = 1, Item = "tea" }, new Order { Id = 2, Item = "toast" }]);

        var read = new List<Order>();
        await foreach (var order in Client().QueryAsync<Order>("SELECT Id, Item FROM orders"))
            read.Add(order);

        Assert.Equal(2, read.Count);
        var (request, response) = Single(Logs());
        Assert.Equal("SELECT FROM orders", request.Method.Value?.ToString());
        Assert.Equal("""[{"Id":1,"Item":"tea"},{"Id":2,"Item":"toast"}]""", response.Content);
    }

    [Fact]
    public void QueryAsync_records_nothing_when_it_is_never_enumerated()
    {
        _ = Client().QueryAsync<Order>("SELECT Id, Item FROM orders");

        Assert.Empty(Logs());
        Assert.Equal(0, _inner.QueryEnumerations);
    }

    [Fact]
    public async Task QueryAsync_left_early_records_the_rows_read_so_far()
    {
        _inner.QueryRows.AddRange([new Order { Id = 1, Item = "tea" }, new Order { Id = 2, Item = "toast" }]);

        await foreach (var _ in Client().QueryAsync<Order>("SELECT Id, Item FROM orders"))
            break;

        Assert.Equal("""[{"Id":1,"Item":"tea"}]""", Single(Logs()).Response.Content);
    }

    [Fact]
    public async Task The_database_a_call_names_in_its_QueryOptions_is_the_one_drawn()
    {
        await Client().ExecuteNonQueryAsync("DELETE FROM orders WHERE id = 1", options: new QueryOptions { Database = "archive" });

        Assert.Equal("clickhouse://ch-host/archive/orders", Logs()[0].Uri.ToString());
    }

    // ─── Raw streams ────────────────────────────────────────────

    [Fact]
    public async Task A_raw_stream_insert_is_never_read_by_the_tracker_and_shows_the_servers_written_rows()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Add("X-ClickHouse-Summary", """{"read_rows":"0","read_bytes":"0","written_rows":"3","written_bytes":"60"}""");
        _inner.StreamResponse = response;
        using var stream = new CountingStream(Encoding.UTF8.GetBytes("1,a\n2,b\n3,c\n"));

        await Client().InsertRawStreamAsync("orders", stream, "CSV", ["id", "item"]);

        Assert.Equal(0, stream.Reads);
        var (request, logged) = Single(Logs());
        Assert.Equal("INSERT INTO orders", request.Method.Value?.ToString());
        Assert.Equal("INSERT INTO orders (id, item) FORMAT CSV", request.Content);
        Assert.Equal("3 rows affected", logged.Content);
    }

    [Fact]
    public async Task A_raw_stream_whose_response_has_no_summary_records_the_call_with_no_count()
    {
        using var stream = new CountingStream([]);

        await Client().PostStreamAsync("INSERT INTO orders FORMAT CSV", stream, isCompressed: false, CancellationToken.None);

        Assert.Null(Single(Logs()).Response.Content);
    }

    // ─── Failures ───────────────────────────────────────────────

    public static TheoryData<string> RecordedMembers => new()
    {
        "ExecuteNonQueryAsync", "ExecuteScalarAsync", "ExecuteReaderAsync", "ExecuteRawResultAsync", "InsertBinaryAsync",
        "InsertBinaryAsync<T>", "InsertRawStreamAsync", "PostStreamAsync", "PostStreamAsync(callback)", "QueryAsync"
    };

    private static async Task Call(IClickHouseClient client, string member)
    {
        switch (member)
        {
            case "ExecuteNonQueryAsync": await client.ExecuteNonQueryAsync("DELETE FROM orders WHERE id = 1"); break;
            case "ExecuteScalarAsync": await client.ExecuteScalarAsync("SELECT count() FROM orders"); break;
            case "ExecuteReaderAsync": await client.ExecuteReaderAsync("SELECT id FROM orders"); break;
            case "ExecuteRawResultAsync": await client.ExecuteRawResultAsync("SELECT id FROM orders"); break;
            case "InsertBinaryAsync": await client.InsertBinaryAsync("orders", ["id", "item"], Rows(3)); break;
            case "InsertBinaryAsync<T>": await client.InsertBinaryAsync("orders", new[] { new Order(), new Order(), new Order() }); break;
            case "InsertRawStreamAsync": await client.InsertRawStreamAsync("orders", new MemoryStream(), "CSV"); break;
            case "PostStreamAsync": await client.PostStreamAsync("INSERT INTO orders FORMAT CSV", new MemoryStream(), false, CancellationToken.None); break;
            case "PostStreamAsync(callback)": await client.PostStreamAsync("INSERT INTO orders FORMAT CSV", (_, _) => Task.CompletedTask, false, CancellationToken.None); break;
            case "QueryAsync": await foreach (var _ in client.QueryAsync<Order>("SELECT Id, Item FROM orders")) { } break;
            default: throw new ArgumentOutOfRangeException(nameof(member), member, null);
        }
    }

    [Theory]
    [MemberData(nameof(RecordedMembers))]
    public async Task A_call_that_fails_records_an_Error_response_with_its_message_and_rethrows_the_same_exception(string member)
    {
        var rejected = new InvalidOperationException("Code: 60. DB::Exception: Unknown table expression identifier 'orders'");
        _inner.Throw = rejected;

        var thrown = await Record.ExceptionAsync(() => Call(Client(), member));

        Assert.Same(rejected, thrown);
        var (_, response) = Single(Logs());
        Assert.Equal("Error", response.StatusCode?.Value?.ToString());
        Assert.Equal(rejected.Message, response.Content);
    }

    [Fact]
    public async Task An_insert_that_fails_part_way_shows_the_rows_the_driver_had_taken()
    {
        _inner.Throw = new InvalidOperationException("rejected");

        await Record.ExceptionAsync(() => Client().InsertBinaryAsync("orders", ["id", "item"], Rows(3)));

        Assert.Equal(2, JsonRows(Logs()[0].Content).Length);
    }

    // ─── Options the shared tracker applies ─────────────────────

    [Fact]
    public async Task At_Summarised_an_insert_is_drawn_as_INSERT_with_no_request_note()
    {
        _options.Verbosity = SqlTrackingVerbosityLevel.Summarised;

        await Client().InsertBinaryAsync("orders", ["id", "item"], Rows(2));

        var (request, _) = Single(Logs());
        Assert.Equal("INSERT", request.Method.Value?.ToString());
        Assert.Null(request.Content);
    }

    [Fact]
    public async Task An_excluded_operation_records_nothing()
    {
        _options.ExcludedOperations = [UnifiedSqlOperation.Insert];

        await Client().InsertBinaryAsync("orders", ["id", "item"], Rows(2));

        Assert.Empty(Logs());
        Assert.Equal(1, _inner.InsertEnumerations);
    }

    [Fact]
    public async Task Nothing_is_recorded_during_setup_when_setup_is_not_tracked()
    {
        _options.TrackDuringSetup = false;
        TestPhaseContext.Current = TestPhase.Setup;

        await Client().InsertBinaryAsync("orders", ["id", "item"], Rows(2));
        await Client().ExecuteNonQueryAsync("DELETE FROM orders WHERE id = 1");

        Assert.Empty(Logs());
    }

    // ─── What is forwarded and not recorded ─────────────────────

    [Fact]
    public async Task Ping_registration_and_connections_are_forwarded_and_not_recorded()
    {
        var client = Client();

        Assert.True(await client.PingAsync());
        client.RegisterBinaryInsertType<Order>();
        client.RegisterPocoType<Order>();
        client.RegisterJsonSerializationType<Order>();
        client.RegisterJsonSerializationType(typeof(Order));
        using var connection = client.CreateConnection();
        Assert.Same(_inner.Settings, client.Settings);

        Assert.Empty(Logs());
        Assert.Equal(new[] { "PingAsync", "RegisterBinaryInsertType", "RegisterPocoType", "RegisterJsonSerializationType",
            "RegisterJsonSerializationType(Type)", "CreateConnection" }, _inner.Calls);
    }

    [Fact]
    public void Wrapping_a_tracked_client_returns_it_unchanged_and_dispose_forwards()
    {
        var client = Client();

        Assert.Same(client, client.WithClickHouseDriverTestTracking(_options));
        Assert.Same(_inner, client.InnerClient);
        client.Dispose();
        Assert.True(_inner.WasDisposed);
    }

    // ─── When the driver adds a member (plan section 4.7) ───────

    // How each member of IClickHouseClient is handled. A driver release that adds a member turns this red before a
    // consumer meets the TypeLoadException a class compiled against the older interface throws on it.
    private static readonly Dictionary<string, string> HandledMembers = new()
    {
        ["get_Settings()"] = "forwarded",
        ["ExecuteNonQueryAsync(String, ClickHouseParameterCollection, QueryOptions, CancellationToken)"] = "recorded",
        ["ExecuteScalarAsync(String, ClickHouseParameterCollection, QueryOptions, CancellationToken)"] = "recorded",
        ["ExecuteReaderAsync(String, ClickHouseParameterCollection, QueryOptions, CancellationToken)"] = "recorded",
        ["ExecuteRawResultAsync(String, QueryOptions, CancellationToken)"] = "recorded",
        ["InsertBinaryAsync(String, IEnumerable`1, IEnumerable`1, InsertOptions, CancellationToken)"] = "recorded",
        ["InsertBinaryAsync(String, IEnumerable`1, InsertOptions, CancellationToken)"] = "recorded",
        ["InsertRawStreamAsync(String, Stream, String, IEnumerable`1, Boolean, QueryOptions, CancellationToken)"] = "recorded",
        ["PostStreamAsync(String, Stream, Boolean, CancellationToken, QueryOptions)"] = "recorded",
        ["PostStreamAsync(String, Func`3, Boolean, CancellationToken, QueryOptions)"] = "recorded",
        ["QueryAsync(String, ClickHouseParameterCollection, QueryOptions, CancellationToken)"] = "recorded",
        ["PingAsync(QueryOptions, CancellationToken)"] = "forwarded",
        ["RegisterBinaryInsertType()"] = "forwarded",
        ["RegisterPocoType()"] = "forwarded",
        ["RegisterJsonSerializationType()"] = "forwarded",
        ["RegisterJsonSerializationType(Type)"] = "forwarded",
        ["CreateConnection()"] = "forwarded",
    };

    [Fact]
    public void Every_member_of_IClickHouseClient_is_named_in_the_handled_list()
    {
        var members = typeof(IClickHouseClient)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})")
            .OrderBy(m => m, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(HandledMembers.Keys.OrderBy(m => m, StringComparer.Ordinal), members);
    }
}
