using System.Data;
using System.Data.Common;
using System.Text.Json;
using Kronikol.Sql;

namespace Kronikol.Tests.Sql;

public class TrackingDbDataReaderTests
{
    // ─── Read captures row data ─────────────────────────────

    [Fact]
    public void Read_CapturesRowData_UpToMaxRows()
    {
        string? captured = null;
        var inner = new FakeDbDataReader(
            ["Name", "Age"],
            [["Alice", 30], ["Bob", 25], ["Carol", 40]]);

        var reader = new TrackingDbDataReader(
            inner, SqlResponseDetail.FullRows, maxRows: 2, maxValueLen: 500,
            content => captured = content);

        while (reader.Read()) { }
        reader.Close();

        Assert.NotNull(captured);
        Assert.Contains("Alice", captured);
        Assert.Contains("Bob", captured);
        Assert.DoesNotContain("Carol", captured);
        Assert.Contains("... (1 more rows not shown)", captured);
    }

    [Fact]
    public void Capped_content_is_a_complete_json_document_plus_a_footnote_on_its_own_line()
    {
        // The note formatter (PlantUmlCreator.TryFormatTruncatedJson) splits this exact shape off
        // to indent the rows; a change to the marker's wording or placement silently un-prettifies
        // every capped database note in the report.
        string? captured = null;
        var inner = new FakeDbDataReader(["Name"], [["Alice"], ["Bob"], ["Carol"]]);

        var reader = new TrackingDbDataReader(
            inner, SqlResponseDetail.FullRows, maxRows: 2, maxValueLen: 500,
            content => captured = content);

        while (reader.Read()) { }
        reader.Close();

        var lines = captured!.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal("... (1 more rows not shown)", lines[1]);
        Assert.Equal(2, JsonDocument.Parse(lines[0]).RootElement.GetArrayLength());
    }

    [Fact]
    public void Read_EmptyResultSet_LogsZeroRows()
    {
        string? captured = null;
        var inner = new FakeDbDataReader(["Id"], []);

        var reader = new TrackingDbDataReader(
            inner, SqlResponseDetail.FullRows, maxRows: 5, maxValueLen: 500,
            content => captured = content);

        while (reader.Read()) { }
        reader.Close();

        Assert.Equal("0 rows", captured);
    }

    [Fact]
    public void Read_StopsCapturing_AfterMaxRows_ButContinuesReading()
    {
        var inner = new FakeDbDataReader(
            ["Id"],
            [["1"], ["2"], ["3"], ["4"], ["5"]]);

        var reader = new TrackingDbDataReader(
            inner, SqlResponseDetail.FullRows, maxRows: 2, maxValueLen: 500, _ => { });

        var count = 0;
        while (reader.Read()) count++;

        Assert.Equal(5, count);
    }

    // ─── Value handling ─────────────────────────────────────

    [Fact]
    public void Read_HandlesNullValues()
    {
        string? captured = null;
        var inner = new FakeDbDataReader(
            ["Name", "Age"],
            [["Alice", DBNull.Value]]);

        var reader = new TrackingDbDataReader(
            inner, SqlResponseDetail.FullRows, maxRows: 5, maxValueLen: 500,
            content => captured = content);

        while (reader.Read()) { }
        reader.Close();

        Assert.NotNull(captured);
        Assert.Contains("null", captured);
    }

    [Fact]
    public void Read_TruncatesLargeValues()
    {
        string? captured = null;
        var largeValue = new string('x', 600);
        var inner = new FakeDbDataReader(["Data"], [[largeValue]]);

        var reader = new TrackingDbDataReader(
            inner, SqlResponseDetail.FullRows, maxRows: 5, maxValueLen: 500,
            content => captured = content);

        while (reader.Read()) { }
        reader.Close();

        Assert.NotNull(captured);
        Assert.True(captured!.Length < largeValue.Length);
        Assert.Contains("... (600 chars)", captured);
    }

    [Fact]
    public void Read_HandlesBinaryValues()
    {
        string? captured = null;
        var inner = new FakeDbDataReader(["Blob"], [[new byte[] { 1, 2, 3 }]]);

        var reader = new TrackingDbDataReader(
            inner, SqlResponseDetail.FullRows, maxRows: 5, maxValueLen: 500,
            content => captured = content);

        while (reader.Read()) { }
        reader.Close();

        Assert.NotNull(captured);
        Assert.Contains("[bytes: 3]", captured);
    }

    // ─── Close / Dispose logging ────────────────────────────

    [Fact]
    public void Close_LogsFormattedContent()
    {
        string? captured = null;
        var inner = new FakeDbDataReader(["Name"], [["Alice"]]);

        var reader = new TrackingDbDataReader(
            inner, SqlResponseDetail.FullRows, maxRows: 5, maxValueLen: 500,
            content => captured = content);

        while (reader.Read()) { }
        reader.Close();

        Assert.NotNull(captured);
        Assert.Contains("Alice", captured);
    }

    [Fact]
    public void Dispose_LogsFormattedContent_IfNotAlreadyLogged()
    {
        string? captured = null;
        var inner = new FakeDbDataReader(["Name"], [["Alice"]]);

        var reader = new TrackingDbDataReader(
            inner, SqlResponseDetail.FullRows, maxRows: 5, maxValueLen: 500,
            content => captured = content);

        while (reader.Read()) { }
        reader.Dispose();

        Assert.NotNull(captured);
    }

    [Fact]
    public void CloseAndDispose_LogsOnlyOnce()
    {
        var callCount = 0;
        var inner = new FakeDbDataReader(["Name"], [["Alice"]]);

        var reader = new TrackingDbDataReader(
            inner, SqlResponseDetail.FullRows, maxRows: 5, maxValueLen: 500,
            _ => callCount++);

        while (reader.Read()) { }
        reader.Close();
        reader.Dispose();

        Assert.Equal(1, callCount);
    }

    // ─── Detail levels ──────────────────────────────────────

    [Fact]
    public void RowCountOnly_ReturnsCountOnly()
    {
        string? captured = null;
        var inner = new FakeDbDataReader(["Name", "Age"], [["Alice", 30], ["Bob", 25]]);

        var reader = new TrackingDbDataReader(
            inner, SqlResponseDetail.RowCountOnly, maxRows: 5, maxValueLen: 500,
            content => captured = content);

        while (reader.Read()) { }
        reader.Close();

        Assert.Equal("2 rows", captured);
    }

    [Fact]
    public void RowCountAndColumns_ReturnsCountWithColumnNames()
    {
        string? captured = null;
        var inner = new FakeDbDataReader(["Name", "Age"], [["Alice", 30], ["Bob", 25]]);

        var reader = new TrackingDbDataReader(
            inner, SqlResponseDetail.RowCountAndColumns, maxRows: 5, maxValueLen: 500,
            content => captured = content);

        while (reader.Read()) { }
        reader.Close();

        Assert.Equal("2 rows [Name, Age]", captured);
    }

    [Fact]
    public void FullRows_ProducesValidJson()
    {
        string? captured = null;
        var inner = new FakeDbDataReader(["Name", "Age"], [["Alice", 30]]);

        var reader = new TrackingDbDataReader(
            inner, SqlResponseDetail.FullRows, maxRows: 5, maxValueLen: 500,
            content => captured = content);

        while (reader.Read()) { }
        reader.Close();

        Assert.NotNull(captured);
        // Content should be valid JSON (no row label prefix)
        var parsed = JsonSerializer.Deserialize<JsonElement>(captured!);
        Assert.Equal(JsonValueKind.Array, parsed.ValueKind);
        Assert.Equal(1, parsed.GetArrayLength());
    }

    [Fact]
    public void FullRows_SingleRow_SaysSingular()
    {
        string? captured = null;
        var inner = new FakeDbDataReader(["Name"], [["Alice"]]);

        var reader = new TrackingDbDataReader(
            inner, SqlResponseDetail.FullRows, maxRows: 5, maxValueLen: 500,
            content => captured = content);

        while (reader.Read()) { }
        reader.Close();

        Assert.StartsWith("[", captured!);
    }

    [Fact]
    public void MaxRowsZero_ShowsCountAndColumnsOnly()
    {
        string? captured = null;
        var inner = new FakeDbDataReader(["Name"], [["Alice"], ["Bob"]]);

        var reader = new TrackingDbDataReader(
            inner, SqlResponseDetail.FullRows, maxRows: 0, maxValueLen: 500,
            content => captured = content);

        while (reader.Read()) { }
        reader.Close();

        Assert.Equal("2 rows [Name]", captured);
    }

    [Fact]
    public void NegativeMaxRows_TreatedAsZero()
    {
        string? captured = null;
        var inner = new FakeDbDataReader(["Name"], [["Alice"]]);

        var reader = new TrackingDbDataReader(
            inner, SqlResponseDetail.FullRows, maxRows: -1, maxValueLen: 500,
            content => captured = content);

        while (reader.Read()) { }
        reader.Close();

        Assert.Equal("1 row [Name]", captured);
    }

    // ─── Async ──────────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_CapturesRows()
    {
        string? captured = null;
        var inner = new FakeDbDataReader(["Name"], [["Alice"], ["Bob"]]);

        var reader = new TrackingDbDataReader(
            inner, SqlResponseDetail.FullRows, maxRows: 5, maxValueLen: 500,
            content => captured = content);

        while (await reader.ReadAsync()) { }
        await reader.CloseAsync();

        Assert.NotNull(captured);
        Assert.Contains("Alice", captured);
        Assert.Contains("Bob", captured);
    }

    // ─── Delegation ─────────────────────────────────────────

    [Fact]
    public void DelegatesFieldCountToInner()
    {
        var inner = new FakeDbDataReader(["A", "B", "C"], []);
        var reader = new TrackingDbDataReader(
            inner, SqlResponseDetail.FullRows, 5, 500, _ => { });

        Assert.Equal(3, reader.FieldCount);
    }

    [Fact]
    public void DelegatesGetNameToInner()
    {
        var inner = new FakeDbDataReader(["Name", "Age"], [["Alice", 30]]);
        var reader = new TrackingDbDataReader(
            inner, SqlResponseDetail.FullRows, 5, 500, _ => { });

        reader.Read();
        Assert.Equal("Name", reader.GetName(0));
    }

    [Fact]
    public void DelegatesIndexerToInner()
    {
        var inner = new FakeDbDataReader(["Name"], [["Alice"]]);
        var reader = new TrackingDbDataReader(
            inner, SqlResponseDetail.FullRows, 5, 500, _ => { });

        reader.Read();
        Assert.Equal("Alice", reader["Name"]);
    }

    // ─── FakeDbDataReader ───────────────────────────────────

    private sealed class FakeDbDataReader : DbDataReader
    {
        private readonly string[] _columns;
        private readonly object[][] _rows;
        private int _currentRow = -1;

        public FakeDbDataReader(string[] columns, object[][] rows)
        {
            _columns = columns;
            _rows = rows;
        }

        public override int FieldCount => _columns.Length;
        public override int RecordsAffected => -1;
        public override bool HasRows => _rows.Length > 0;
        public override bool IsClosed => false;
        public override int Depth => 0;

        public override bool Read()
        {
            _currentRow++;
            return _currentRow < _rows.Length;
        }

        public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Read());

        public override string GetName(int ordinal) => _columns[ordinal];

        public override int GetOrdinal(string name) =>
            Array.IndexOf(_columns, name);

        public override object GetValue(int ordinal) => _rows[_currentRow][ordinal];

        public override bool IsDBNull(int ordinal) =>
            _rows[_currentRow][ordinal] is DBNull;

        public override Type GetFieldType(int ordinal)
        {
            if (_currentRow >= 0 && _currentRow < _rows.Length)
            {
                var val = _rows[_currentRow][ordinal];
                return val is DBNull ? typeof(string) : val.GetType();
            }
            return typeof(object);
        }

        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(GetOrdinal(name));

        public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
        public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
        public override char GetChar(int ordinal) => (char)GetValue(ordinal);
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
        public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;
        public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
        public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
        public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
        public override float GetFloat(int ordinal) => (float)GetValue(ordinal);
        public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
        public override short GetInt16(int ordinal) => (short)GetValue(ordinal);
        public override int GetInt32(int ordinal) => (int)GetValue(ordinal);
        public override long GetInt64(int ordinal) => (long)GetValue(ordinal);
        public override string GetString(int ordinal) => (string)GetValue(ordinal);
        public override int GetValues(object[] values)
        {
            var count = Math.Min(values.Length, _columns.Length);
            for (var i = 0; i < count; i++)
                values[i] = GetValue(i);
            return count;
        }
        public override bool NextResult() => false;

        public override System.Collections.IEnumerator GetEnumerator() =>
            throw new NotSupportedException();
    }
}
