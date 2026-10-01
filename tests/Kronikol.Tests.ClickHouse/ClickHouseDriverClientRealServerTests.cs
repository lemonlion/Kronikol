using System.Text;
using System.Text.Json;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO;
using Kronikol.Extensions.ClickHouse;
using Kronikol.Extensions.ClickHouse.Driver;
using Kronikol.Tracking;
using Xunit;

namespace Kronikol.Tests.ClickHouse;

/// <summary>
/// ClickHouse.Driver against a real server (plan section 5): the issue's own sequence (#126), and the driver
/// behaviour the design rests on, which a fake would have to imitate: rows pulled once and held by reference, batches,
/// the counts the client does and does not report, and failures.
/// </summary>
public class ClickHouseDriverClientRealServerTests(ClickHouseServerFixture server) : IClassFixture<ClickHouseServerFixture>
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private ClickHouseTrackingOptions Options() => new() { CurrentTestInfoFetcher = () => ("RealServer", _testId) };

    private RequestResponseLog[] Logs()
        => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private RequestResponseLog[] Requests() => Logs().Where(l => l.Type == RequestResponseType.Request).ToArray();

    private RequestResponseLog ResponseTo(RequestResponseLog request)
        => Logs().Single(l => l.Type == RequestResponseType.Response && l.RequestResponseId == request.RequestResponseId);

    private static string NewTable() => $"orders_{Guid.NewGuid():N}";

    private static async Task<string> CreateTable(IClickHouseClient untracked, string columns = "id UInt32, item String")
    {
        var table = NewTable();
        await untracked.ExecuteNonQueryAsync($"CREATE TABLE {table} ({columns}) ENGINE = MergeTree ORDER BY tuple()");
        return table;
    }

    private static int[] RecordedIds(string? content)
        => JsonDocument.Parse(content!.Split("\n... (")[0]).RootElement.EnumerateArray()
            .Select(r => r.GetProperty("id").GetInt32()).ToArray();

    public sealed class OrderRow
    {
        public uint id { get; set; }
        public string item { get; set; } = "";
    }

    [Fact]
    public async Task The_issues_sequence_records_four_calls_with_the_inserted_rows_and_the_count()
    {
        var connectionString = server.Require();
        var options = Options();
        await using var connection = new ClickHouseConnection(connectionString).WithClickHouseDriverTestTracking(options);
        await connection.OpenAsync();
        var table = NewTable();
        await using (var create = connection.CreateCommand())
        {
            create.CommandText = $"CREATE TABLE {table} (id UInt32, item String) ENGINE = MergeTree ORDER BY id";
            await create.ExecuteNonQueryAsync();
        }

        using var dataSource = new ClickHouseDataSource(connectionString);
        var client = dataSource.GetClient().WithClickHouseDriverTestTracking(options);
        var inserted = await client.InsertBinaryAsync(table, ["id", "item"], [[1u, "tea"], [2u, "toast"]]);
        await client.ExecuteNonQueryAsync($"DELETE FROM {table} WHERE id = 1");
        object? count;
        await using (var select = connection.CreateCommand())
        {
            select.CommandText = $"SELECT count() FROM {table}";
            count = await select.ExecuteScalarAsync();
        }

        Assert.Equal(2, inserted);
        Assert.Equal(1UL, Convert.ToUInt64(count));
        var requests = Requests();
        Assert.Equal(
            new[] { $"CREATE TABLE {table}", $"INSERT INTO {table}", $"DELETE FROM {table}", $"SELECT FROM {table}" },
            requests.Select(r => r.Method.Value?.ToString()));
        Assert.Equal("""[{"id":1,"item":"tea"},{"id":2,"item":"toast"}]""", requests[1].Content);
        Assert.EndsWith($"/{table}", requests[1].Uri.ToString());
        Assert.Equal("2 rows affected", ResponseTo(requests[1]).Content);
        Assert.Null(ResponseTo(requests[2]).Content);
        Assert.Equal("1", ResponseTo(requests[3]).Content);
    }

    // Plan F4: the driver holds each row's reference and serializes it when it sends, so an iterator that reuses one
    // array stores its last values in every row. The record shows what was stored, and the source is pulled once.
    [Fact]
    public async Task Rows_from_an_iterator_that_reuses_one_array_are_recorded_as_the_driver_wrote_them()
    {
        using var dataSource = new ClickHouseDataSource(server.Require());
        var table = await CreateTable(dataSource.GetClient());
        var starts = 0;
        IEnumerable<object[]> Reused()
        {
            starts++;
            var row = new object[2];
            for (uint id = 10; id <= 12; id++)
            {
                row[0] = id;
                row[1] = $"item{id}";
                yield return row;
            }
        }

        await dataSource.GetClient().WithClickHouseDriverTestTracking(Options()).InsertBinaryAsync(table, ["id", "item"], Reused());

        var stored = await dataSource.GetClient().ExecuteScalarAsync(
            $"SELECT arrayStringConcat(arraySort(groupArray(toString(id))), ',') FROM {table}");
        Assert.Equal("12,12,12", stored);
        Assert.Equal(1, starts);
        Assert.Equal(new[] { 12, 12, 12 }, RecordedIds(Requests().Single().Content));
    }

    [Fact]
    public async Task Batches_sent_in_parallel_are_one_call_with_every_row_counted()
    {
        using var dataSource = new ClickHouseDataSource(server.Require());
        var table = await CreateTable(dataSource.GetClient());
        var rows = Enumerable.Range(1, 10).Select(i => new object[] { (uint)i, $"item{i}" });

        await dataSource.GetClient().WithClickHouseDriverTestTracking(Options()).InsertBinaryAsync(
            table, ["id", "item"], rows, new InsertOptions { BatchSize = 1, MaxDegreeOfParallelism = 4 });

        var request = Assert.Single(Requests());
        Assert.Equal(10, RecordedIds(request.Content).Length);
        Assert.Equal("10 rows affected", ResponseTo(request).Content);
        Assert.Equal(10UL, Convert.ToUInt64(await dataSource.GetClient().ExecuteScalarAsync($"SELECT count() FROM {table}")));
    }

    [Fact]
    public async Task A_typed_insert_records_its_rows_by_property()
    {
        using var dataSource = new ClickHouseDataSource(server.Require());
        var table = await CreateTable(dataSource.GetClient());
        var client = dataSource.GetClient().WithClickHouseDriverTestTracking(Options());
        client.RegisterBinaryInsertType<OrderRow>();

        var sent = await client.InsertBinaryAsync(table, new[] { new OrderRow { id = 1, item = "tea" } });

        Assert.Equal(1, sent);
        var request = Assert.Single(Requests());
        Assert.Equal($"INSERT INTO {table}", request.Method.Value?.ToString());
        Assert.Equal("""[{"id":1,"item":"tea"}]""", request.Content);
    }

    // Plan F3: the client's ExecuteNonQueryAsync returns 0 for every write, so a count would be false.
    [Fact]
    public async Task A_write_through_the_client_shows_no_count_rather_than_the_drivers_0()
    {
        using var dataSource = new ClickHouseDataSource(server.Require());
        var table = await CreateTable(dataSource.GetClient());

        var driverResult = await dataSource.GetClient().WithClickHouseDriverTestTracking(Options())
            .ExecuteNonQueryAsync($"INSERT INTO {table} VALUES (1, 'a'), (2, 'b')");

        Assert.Equal(0, driverResult);
        var request = Assert.Single(Requests());
        Assert.Equal($"INSERT INTO {table}", request.Method.Value?.ToString());
        Assert.Null(ResponseTo(request).Content);
    }

    [Fact]
    public async Task A_scalar_shows_its_value_a_query_its_rows_and_a_reader_only_its_call()
    {
        using var dataSource = new ClickHouseDataSource(server.Require());
        var table = await CreateTable(dataSource.GetClient());
        await dataSource.GetClient().ExecuteNonQueryAsync($"INSERT INTO {table} VALUES (1, 'tea'), (2, 'toast')");
        var client = dataSource.GetClient().WithClickHouseDriverTestTracking(Options());
        client.RegisterPocoType<OrderRow>();

        await client.ExecuteScalarAsync($"SELECT count() FROM {table}");
        var read = new List<OrderRow>();
        await foreach (var row in client.QueryAsync<OrderRow>($"SELECT id, item FROM {table} ORDER BY id"))
            read.Add(row);
        using (var reader = await client.ExecuteReaderAsync($"SELECT id FROM {table}"))
            while (reader.Read()) { }

        Assert.Equal(2, read.Count);
        var requests = Requests();
        Assert.Equal(3, requests.Length);
        Assert.Equal("2", ResponseTo(requests[0]).Content);
        Assert.Equal("""[{"id":1,"item":"tea"},{"id":2,"item":"toast"}]""", ResponseTo(requests[1]).Content);
        Assert.Null(ResponseTo(requests[2]).Content);
    }

    [Fact]
    public async Task A_statement_the_server_rejects_through_the_client_records_an_Error_response()
    {
        using var dataSource = new ClickHouseDataSource(server.Require());
        var client = dataSource.GetClient().WithClickHouseDriverTestTracking(Options());

        var thrown = await Record.ExceptionAsync(() => client.ExecuteNonQueryAsync("SELECT * FROM no_such_table_126"));

        Assert.NotNull(thrown);
        var request = Assert.Single(Requests());
        var response = ResponseTo(request);
        Assert.Equal("Error", response.StatusCode?.Value?.ToString());
        Assert.Equal(thrown.Message, response.Content);
    }

    // The R1 fix (plan F10) on the real driver: the probe recorded 1 request and 0 responses on 4.4.0.
    [Fact]
    public async Task A_statement_the_server_rejects_through_a_tracked_connection_records_an_Error_response()
    {
        await using var connection = new ClickHouseConnection(server.Require()).WithClickHouseDriverTestTracking(Options());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM no_such_table_126";

        var thrown = await Record.ExceptionAsync(() => command.ExecuteReaderAsync());

        Assert.NotNull(thrown);
        var request = Assert.Single(Requests());
        Assert.Equal("Error", ResponseTo(request).StatusCode?.Value?.ToString());
        Assert.Equal(thrown.Message, ResponseTo(request).Content);
    }

    [Fact]
    public async Task A_raw_stream_insert_shows_the_rows_the_server_wrote()
    {
        using var dataSource = new ClickHouseDataSource(server.Require());
        var table = await CreateTable(dataSource.GetClient());
        using var csv = new MemoryStream(Encoding.UTF8.GetBytes("1,a\n2,b\n3,c\n"));

        using var response = await dataSource.GetClient().WithClickHouseDriverTestTracking(Options())
            .InsertRawStreamAsync(table, csv, "CSV", ["id", "item"]);

        var request = Assert.Single(Requests());
        Assert.Equal($"INSERT INTO {table} (id, item) FORMAT CSV", request.Content);
        Assert.Equal("3 rows affected", ResponseTo(request).Content);
        Assert.Equal(3UL, Convert.ToUInt64(await dataSource.GetClient().ExecuteScalarAsync($"SELECT count() FROM {table}")));
    }
}
