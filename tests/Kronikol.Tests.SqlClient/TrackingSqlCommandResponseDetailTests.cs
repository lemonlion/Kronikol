using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Kronikol.Extensions.SqlClient;
using Kronikol.Sql;
using Kronikol.Tracking;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Kronikol.Tests.SqlClient;

/// <summary>
/// First pipeline coverage for the DbConnection-wrapping side of this extension: SELECT response
/// detail follows the effective verbosity (actual rows at Raw/Detailed, count+columns at
/// Summarised) unless ResponseDetail is set explicitly.
/// </summary>
public class TrackingSqlCommandResponseDetailTests : IDisposable
{
    private readonly string _testId = Guid.NewGuid().ToString();
    private readonly SqlClientTrackingOptions _options;
    private readonly TrackingSqlConnection _trackingConnection;

    public TrackingSqlCommandResponseDetailTests()
    {
        TrackingComponentRegistry.Clear();
        _options = new SqlClientTrackingOptions { CurrentTestInfoFetcher = () => ("TestMethod", _testId) };
        _trackingConnection = new TrackingSqlConnection(
            new SqlConnection("Server=localhost;Database=analytics"), _options);
    }

    public void Dispose()
    {
        _trackingConnection.Dispose();
        TrackingComponentRegistry.Clear();
    }

    private RequestResponseLog[] GetLogsForTest()
        => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private string? RunSelectAndGetResponseContent()
    {
        using var cmd = new TrackingSqlCommand(new RowReaderFakeDbCommand(), _trackingConnection);
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

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => new RowReaderFakeDataReader();
    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
        => Task.FromResult<DbDataReader>(new RowReaderFakeDataReader());
    public override int ExecuteNonQuery() => 1;
    public override object? ExecuteScalar() => 1;
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
