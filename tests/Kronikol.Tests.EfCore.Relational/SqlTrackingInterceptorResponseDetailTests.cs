using System.Collections;
using System.Data.Common;
using Kronikol.Extensions.EfCore.Relational;
using Kronikol.Tracking;

namespace Kronikol.Tests.EfCore.Relational;

/// <summary>
/// Reader response detail follows the effective verbosity (actual rows at Raw/Detailed,
/// count+columns at Summarised) unless ResponseDetail is set explicitly.
/// </summary>
public class SqlTrackingInterceptorResponseDetailTests : IDisposable
{
    private readonly string _testId = Guid.NewGuid().ToString();

    public void Dispose() => TestPhaseContext.Reset();

    private RequestResponseLog[] GetLogsFromThisTest()
        => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private SqlTrackingInterceptorOptions MakeOptions() => new()
    {
        CurrentTestInfoFetcher = () => ("My Test", _testId),
    };

    private string? RunSelectAndGetResponseContent(SqlTrackingInterceptorOptions options)
    {
        var interceptor = new SqlTrackingInterceptor(options);
        var command = new StubDbCommand("SELECT [id], [name] FROM [breakfasts]", "MyDb", "localhost");

        var wrapped = interceptor.ReaderExecuted(command, null!, new RowStubDataReader());
        while (wrapped.Read()) { }
        wrapped.Close();

        return GetLogsFromThisTest().Last(l => l.Type == RequestResponseType.Response).Content;
    }

    [Fact]
    public void Select_at_default_detailed_verbosity_logs_actual_rows()
    {
        var content = RunSelectAndGetResponseContent(MakeOptions());

        Assert.NotNull(content);
        Assert.Contains("\"name\":\"Pancakes\"", content);
        Assert.Contains("\"name\":\"Waffles\"", content);
    }

    [Fact]
    public void Select_at_summarised_verbosity_logs_count_and_columns()
    {
        var options = MakeOptions();
        options.Verbosity = SqlTrackingVerbosity.Summarised;

        Assert.Equal("2 rows [id, name]", RunSelectAndGetResponseContent(options));
    }

    [Fact]
    public void Explicit_ResponseDetail_wins_over_verbosity()
    {
        var options = MakeOptions();
        options.ResponseDetail = Kronikol.Sql.SqlResponseDetail.RowCountAndColumns;

        Assert.Equal("2 rows [id, name]", RunSelectAndGetResponseContent(options));
    }
}

/// <summary>A reader with actual data — two rows of (id, name).</summary>
internal sealed class RowStubDataReader : DbDataReader
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
