using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Kronikol.Extensions.Spanner;
using Kronikol.Tracking;

namespace Kronikol.Tests.Spanner;

public class TrackingSpannerCommandFailureTests : IDisposable
{
    private readonly string _testId = Guid.NewGuid().ToString();
    private readonly SpannerTrackingOptions _options;
    private readonly TrackingSpannerConnection _trackingConnection;

    public TrackingSpannerCommandFailureTests()
    {
        TrackingComponentRegistry.Clear();
        _options = new SpannerTrackingOptions { CurrentTestInfoFetcher = () => ("TestMethod", _testId) };
        _trackingConnection = new TrackingSpannerConnection(new FailureFakeDbConnection(), _options);
    }

    public void Dispose()
    {
        _trackingConnection.Dispose();
        TrackingComponentRegistry.Clear();
    }

    private RequestResponseLog[] GetLogsForTest()
        => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    public static TheoryData<string> ExecutePaths => new()
    {
        "ExecuteNonQuery", "ExecuteNonQueryAsync", "ExecuteScalar", "ExecuteScalarAsync", "ExecuteReader", "ExecuteReaderAsync"
    };

    private static async Task Execute(DbCommand cmd, string path)
    {
        switch (path)
        {
            case "ExecuteNonQuery": cmd.ExecuteNonQuery(); break;
            case "ExecuteNonQueryAsync": await cmd.ExecuteNonQueryAsync(); break;
            case "ExecuteScalar": cmd.ExecuteScalar(); break;
            case "ExecuteScalarAsync": await cmd.ExecuteScalarAsync(); break;
            case "ExecuteReader": cmd.ExecuteReader().Dispose(); break;
            case "ExecuteReaderAsync": await (await cmd.ExecuteReaderAsync()).DisposeAsync(); break;
            default: throw new ArgumentOutOfRangeException(nameof(path), path, null);
        }
    }

    [Theory]
    [MemberData(nameof(ExecutePaths))]
    public async Task A_statement_that_fails_records_an_Error_response_with_its_message_and_rethrows_the_same_exception(string path)
    {
        var rejected = new InvalidOperationException("Table not found: no_such_table_126");
        using var cmd = new TrackingSpannerCommand(new FailureFakeDbCommand(rejected), _trackingConnection, _options);
        cmd.CommandText = "SELECT * FROM no_such_table_126";

        var thrown = await Record.ExceptionAsync(() => Execute(cmd, path));

        Assert.Same(rejected, thrown);
        var logs = GetLogsForTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
        Assert.Equal(logs[0].RequestResponseId, logs[1].RequestResponseId);
        Assert.Equal("Error", logs[1].StatusCode?.Value?.ToString());
        Assert.Equal(rejected.Message, logs[1].Content);
    }
}

internal sealed class FailureFakeDbConnection : DbConnection
{
    [AllowNull]
    public override string ConnectionString { get; set; } = "";
    public override string Database => "orders";
    public override string DataSource => "spanner-host";
    public override string ServerVersion => "1";
    public override ConnectionState State => ConnectionState.Open;
    public override void Open() { }
    public override void Close() { }
    public override void ChangeDatabase(string databaseName) { }
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
    protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
}

/// <summary>A command whose every Execute* fails, as a driver's does for a statement the server rejects.</summary>
internal sealed class FailureFakeDbCommand(Exception rejection) : DbCommand
{
    [AllowNull]
    public override string CommandText { get; set; } = "";
    public override int CommandTimeout { get; set; } = 30;
    public override CommandType CommandType { get; set; } = CommandType.Text;
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get; set; }
    protected override DbTransaction? DbTransaction { get; set; }
    protected override DbParameterCollection DbParameterCollection { get; } = new EmptyFakeParameterCollection();

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw rejection;
    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
        => Task.FromException<DbDataReader>(rejection);
    public override int ExecuteNonQuery() => throw rejection;
    public override object? ExecuteScalar() => throw rejection;
    public override void Prepare() { }
    public override void Cancel() { }
    protected override DbParameter CreateDbParameter() => throw new NotSupportedException();
}

internal sealed class EmptyFakeParameterCollection : DbParameterCollection
{
    public override int Count => 0;
    public override object SyncRoot { get; } = new();
    public override int Add(object value) => throw new NotSupportedException();
    public override void AddRange(Array values) => throw new NotSupportedException();
    public override void Clear() { }
    public override bool Contains(object value) => false;
    public override bool Contains(string value) => false;
    public override void CopyTo(Array array, int index) { }
    public override System.Collections.IEnumerator GetEnumerator() => Array.Empty<DbParameter>().GetEnumerator();
    protected override DbParameter GetParameter(int index) => throw new IndexOutOfRangeException();
    protected override DbParameter GetParameter(string parameterName) => throw new IndexOutOfRangeException();
    public override int IndexOf(object value) => -1;
    public override int IndexOf(string parameterName) => -1;
    public override void Insert(int index, object value) => throw new NotSupportedException();
    public override void Remove(object value) { }
    public override void RemoveAt(int index) => throw new IndexOutOfRangeException();
    public override void RemoveAt(string parameterName) => throw new IndexOutOfRangeException();
    protected override void SetParameter(int index, DbParameter value) => throw new IndexOutOfRangeException();
    protected override void SetParameter(string parameterName, DbParameter value) => throw new IndexOutOfRangeException();
}
