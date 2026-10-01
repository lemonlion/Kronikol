using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Kronikol.Extensions.Npgsql;
using Kronikol.Sql;
using Kronikol.Tracking;
using Npgsql;
using Xunit;

namespace Kronikol.Tests.Npgsql;

/// <summary>
/// First pipeline coverage for the DbConnection-wrapping side of this extension: SELECT response
/// detail follows the effective verbosity (actual rows at Raw/Detailed, count+columns at
/// Summarised) unless ResponseDetail is set explicitly.
/// </summary>
public class TrackingNpgsqlCommandResponseDetailTests : IDisposable
{
    private readonly string _testId = Guid.NewGuid().ToString();
    private readonly NpgsqlTrackingOptions _options;
    private readonly TrackingNpgsqlConnection _trackingConnection;

    public TrackingNpgsqlCommandResponseDetailTests()
    {
        TrackingComponentRegistry.Clear();
        _options = new NpgsqlTrackingOptions { CurrentTestInfoFetcher = () => ("TestMethod", _testId) };
        _trackingConnection = new TrackingNpgsqlConnection(
            new NpgsqlConnection("Host=localhost;Database=analytics"), _options);
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

    private static async Task Execute(System.Data.Common.DbCommand cmd, string path)
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
        var rejected = new InvalidOperationException("relation \"no_such_table_126\" does not exist");
        using var cmd = new TrackingNpgsqlCommand(new RowReaderFakeDbCommand { ThrowOnExecute = rejected }, _trackingConnection);
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

    [Theory]
    [InlineData("Commit")]
    [InlineData("Rollback")]
    public void A_commit_or_rollback_that_fails_records_an_Error_response_with_its_message_and_rethrows_the_same_exception(string operation)
    {
        var rejected = new InvalidOperationException("the transaction was aborted by the server");
        using var tx = new TrackingNpgsqlTransaction(new RejectingFakeDbTransaction(rejected), _trackingConnection);

        var thrown = Record.Exception(() =>
        {
            if (operation == "Commit") tx.Commit();
            else tx.Rollback();
        });

        Assert.Same(rejected, thrown);
        var logs = GetLogsForTest();
        // BEGIN TRANSACTION and its response, then the statement that failed and its response.
        Assert.Equal(4, logs.Length);
        Assert.Equal(RequestResponseType.Request, logs[2].Type);
        Assert.Equal(RequestResponseType.Response, logs[3].Type);
        Assert.Equal(logs[2].RequestResponseId, logs[3].RequestResponseId);
        Assert.Equal("Error", logs[3].StatusCode?.Value?.ToString());
        Assert.Equal(rejected.Message, logs[3].Content);
    }

    private string? RunSelectAndGetResponseContent()
    {
        using var cmd = new TrackingNpgsqlCommand(new RowReaderFakeDbCommand(), _trackingConnection);
        cmd.CommandText = "SELECT id, name FROM breakfasts";
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read()) { }
        }
        return GetLogsForTest().Last(l => l.Type == RequestResponseType.Response).Content;
    }

    [Fact]
    public void Select_at_default_detailed_verbosity_logs_actual_rows()
    {
        var content = RunSelectAndGetResponseContent();

        Assert.NotNull(content);
        Assert.Contains("\"name\":\"Pancakes\"", content);
        Assert.Contains("\"name\":\"Waffles\"", content);
    }

    [Fact]
    public void Select_at_summarised_verbosity_logs_count_and_columns()
    {
        _options.Verbosity = SqlTrackingVerbosityLevel.Summarised;
        Assert.Equal("2 rows [id, name]", RunSelectAndGetResponseContent());
    }

    [Fact]
    public void Explicit_ResponseDetail_wins_over_verbosity()
    {
        _options.ResponseDetail = SqlResponseDetail.RowCountAndColumns;
        Assert.Equal("2 rows [id, name]", RunSelectAndGetResponseContent());
    }
}

/// <summary>A DbCommand whose reader yields two rows of (id, name).</summary>
internal sealed class RowReaderFakeDbCommand : DbCommand
{
    [AllowNull]
    public override string CommandText { get; set; } = "";
    public override int CommandTimeout { get; set; } = 30;
    public override CommandType CommandType { get; set; } = CommandType.Text;
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get; set; }
    protected override DbTransaction? DbTransaction { get; set; }
    protected override DbParameterCollection DbParameterCollection { get; } = new RowReaderFakeParameterCollection();

    /// <summary>When set, every Execute* fails with it, as a driver does for a statement the server rejects.</summary>
    public Exception? ThrowOnExecute { get; init; }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        => ThrowOnExecute is { } e ? throw e : new RowReaderFakeDataReader();
    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
        => ThrowOnExecute is { } e
            ? Task.FromException<DbDataReader>(e)
            : Task.FromResult<DbDataReader>(new RowReaderFakeDataReader());
    public override int ExecuteNonQuery() => ThrowOnExecute is { } e ? throw e : 1;
    public override object? ExecuteScalar() => ThrowOnExecute is { } e ? throw e : 1;
    public override void Prepare() { }
    public override void Cancel() { }
    protected override DbParameter CreateDbParameter() => throw new NotSupportedException();
}

internal sealed class RowReaderFakeParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> _parameters = [];
    public override int Count => _parameters.Count;
    public override object SyncRoot => ((ICollection)_parameters).SyncRoot;
    public override int Add(object value) { _parameters.Add((DbParameter)value); return _parameters.Count - 1; }
    public override void AddRange(Array values) { foreach (DbParameter p in values) _parameters.Add(p); }
    public override void Clear() => _parameters.Clear();
    public override bool Contains(object value) => _parameters.Contains((DbParameter)value);
    public override bool Contains(string value) => false;
    public override void CopyTo(Array array, int index) => ((ICollection)_parameters).CopyTo(array, index);
    public override IEnumerator GetEnumerator() => _parameters.GetEnumerator();
    public override int IndexOf(object value) => _parameters.IndexOf((DbParameter)value);
    public override int IndexOf(string parameterName) => -1;
    public override void Insert(int index, object value) => _parameters.Insert(index, (DbParameter)value);
    public override void Remove(object value) => _parameters.Remove((DbParameter)value);
    public override void RemoveAt(int index) => _parameters.RemoveAt(index);
    public override void RemoveAt(string parameterName) { }
    protected override DbParameter GetParameter(int index) => _parameters[index];
    protected override DbParameter GetParameter(string parameterName) => throw new NotSupportedException();
    protected override void SetParameter(int index, DbParameter value) => _parameters[index] = value;
    protected override void SetParameter(string parameterName, DbParameter value) { }
}

internal sealed class RowReaderFakeDataReader : DbDataReader
{
    private static readonly (int Id, string Name)[] Rows = [(1, "Pancakes"), (2, "Waffles")];
    private int _index = -1;

    public override int FieldCount => 2;
    public override int RecordsAffected => -1;
    public override bool HasRows => true;
    public override bool IsClosed => false;
    public override int Depth => 0;
    public override bool Read() => ++_index < Rows.Length;
    public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Read());
    public override string GetName(int ordinal) => ordinal == 0 ? "id" : "name";
    public override object GetValue(int ordinal) => ordinal == 0 ? Rows[_index].Id : Rows[_index].Name;
    public override bool IsDBNull(int ordinal) => false;
    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(name == "id" ? 0 : 1);
    public override bool NextResult() => false;
    public override Task<bool> NextResultAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    public override bool GetBoolean(int ordinal) => throw new NotSupportedException();
    public override byte GetByte(int ordinal) => throw new NotSupportedException();
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
    public override char GetChar(int ordinal) => throw new NotSupportedException();
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
    public override string GetDataTypeName(int ordinal) => ordinal == 0 ? "Int32" : "String";
    public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();
    public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();
    public override double GetDouble(int ordinal) => throw new NotSupportedException();
    public override Type GetFieldType(int ordinal) => ordinal == 0 ? typeof(int) : typeof(string);
    public override float GetFloat(int ordinal) => throw new NotSupportedException();
    public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
    public override short GetInt16(int ordinal) => throw new NotSupportedException();
    public override int GetInt32(int ordinal) => Rows[_index].Id;
    public override long GetInt64(int ordinal) => Rows[_index].Id;
    public override string GetString(int ordinal) => Rows[_index].Name;
    public override int GetOrdinal(string name) => name == "id" ? 0 : 1;
    public override int GetValues(object[] values) { values[0] = Rows[_index].Id; values[1] = Rows[_index].Name; return 2; }
    public override IEnumerator GetEnumerator() => Rows.GetEnumerator();
}

/// <summary>A transaction whose commit and rollback fail, as a driver's do when the server rejects them.</summary>
internal sealed class RejectingFakeDbTransaction(Exception rejection) : System.Data.Common.DbTransaction
{
    public override System.Data.IsolationLevel IsolationLevel => System.Data.IsolationLevel.ReadCommitted;
    protected override System.Data.Common.DbConnection? DbConnection => null;
    public override void Commit() => throw rejection;
    public override void Rollback() => throw rejection;
}
