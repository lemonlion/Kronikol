using System.Collections;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using Kronikol.Sql;

namespace Kronikol.Tests.Sql;

/// <summary>
/// The decorator stands where the provider's own reader stood, so it has to behave as that reader does: every member
/// answered by the provider (#197), no read the application did not make, every result set, and nothing thrown from the
/// capture or the note into the application's call (#193 plan, F3, F4, F5, F7).
/// </summary>
public class TrackingDbDataReaderFidelityTests
{
    private static TrackingDbDataReader Track(DbDataReader inner, Action<string?>? onComplete = null,
        SqlResponseDetail detail = SqlResponseDetail.FullRows, int maxRows = 10)
        => new(inner, detail, maxRows, 500, onComplete ?? (_ => { }));

    private static TrackingDbDataReader TrackSequential(DbDataReader inner, Action<string?> onComplete)
        => new(inner, SqlResponseDetail.FullRows, 10, 500, onComplete, CommandBehavior.SequentialAccess);

    // ─── #197: the provider's own conversions ─────────────────

    [Fact]
    public void GetFieldValue_returns_the_provider_readers_own_conversion()
    {
        // Npgsql's GetValue gives a DateTime for timestamptz and an int[] for an array, and converts in its own
        // GetFieldValue<T>. DbDataReader's default GetFieldValue<T> is (T)GetValue(ordinal), so every EF Core query that
        // read a DateTimeOffset or a List<int> through the decorator threw InvalidCastException.
        var at = new DateTime(2026, 10, 26, 12, 0, 0);
        using var reader = Track(new ProviderLikeReader(new Set(["At", "Tags"], [[at, new[] { 1, 2 }]])));

        Assert.True(reader.Read());
        Assert.Equal(new DateTimeOffset(at, TimeSpan.Zero), reader.GetFieldValue<DateTimeOffset>(0));
        Assert.Equal([1, 2], reader.GetFieldValue<List<int>>(1));
    }

    [Fact]
    public async Task GetFieldValueAsync_returns_the_provider_readers_own_conversion()
    {
        var at = new DateTime(2026, 10, 26, 12, 0, 0);
        var inner = new ProviderLikeReader(new Set(["At"], [[at]]));
        await using var reader = Track(inner);

        Assert.True(await reader.ReadAsync());
        Assert.Equal(new DateTimeOffset(at, TimeSpan.Zero), await reader.GetFieldValueAsync<DateTimeOffset>(0));
        Assert.Contains("GetFieldValueAsync", inner.Calls);
    }

    [Fact]
    public async Task Every_virtual_member_is_answered_by_the_provider_reader()
    {
        var inner = new ProviderLikeReader(new Set(["Id", "Name"], [[1, "a"]]));
        using var reader = Track(inner);
        Assert.True(reader.Read());

        Assert.Equal(1, reader.VisibleFieldCount); // the provider hides a column; DbDataReader's default says 2
        Assert.Equal([1, 2, 3], ((MemoryStream)reader.GetStream(0)).ToArray());
        Assert.Equal("text", reader.GetTextReader(1).ReadToEnd());
        Assert.Equal(typeof(ProviderValue), reader.GetProviderSpecificFieldType(0));
        Assert.IsType<ProviderValue>(reader.GetProviderSpecificValue(0));
        var values = new object[2];
        Assert.Equal(1, reader.GetProviderSpecificValues(values));
        Assert.IsType<ProviderValue>(values[0]);
        Assert.Equal("schema", reader.GetSchemaTable()!.TableName);
        Assert.Equal("schema", (await reader.GetSchemaTableAsync())!.TableName);
        Assert.False(await reader.IsDBNullAsync(1));
        Assert.Equal("nested", reader.GetData(0).GetName(0));

        foreach (var member in (string[])["VisibleFieldCount", "GetStream", "GetTextReader", "GetProviderSpecificFieldType",
                     "GetProviderSpecificValue", "GetProviderSpecificValues", "GetSchemaTable", "GetSchemaTableAsync",
                     "IsDBNullAsync", "GetData"])
            Assert.Contains(member, inner.Calls);
    }

    [Fact]
    public async Task The_column_schema_is_the_provider_readers()
    {
        // GetColumnSchema() asks for IDbColumnSchemaGenerator and otherwise falls back to GetSchemaTable(), which
        // DbDataReader does not implement: through the decorator it threw although CanGetColumnSchema() said true.
        var inner = new ProviderLikeReader(new Set(["Id"], [[1]]));
        using var reader = Track(inner);

        Assert.True(reader.CanGetColumnSchema());
        Assert.Equal("Id", Assert.Single(reader.GetColumnSchema()).ColumnName);
        Assert.Equal("Id", Assert.Single(await reader.GetColumnSchemaAsync()).ColumnName);
        Assert.Contains("GetColumnSchema", inner.Calls);
    }

    [Fact]
    public void Enumerating_the_decorator_captures_the_rows_it_hands_out()
    {
        string? note = null;
        var reader = Track(new ProviderLikeReader(new Set(["Name"], [["Alice"], ["Bob"]])), c => note = c);

        var names = new List<string>();
        foreach (IDataRecord record in reader)
            names.Add(record.GetString(0));
        reader.Close();

        Assert.Equal(["Alice", "Bob"], names);
        Assert.Equal("""[{"Name":"Alice"},{"Name":"Bob"}]""", note);
    }

    // ─── F4: no read the application did not make ─────────────

    [Fact]
    public void A_sequential_reader_is_read_only_as_the_application_reads_it()
    {
        // Dapper opens every query reader with CommandBehavior.SequentialAccess, where SqlClient and Npgsql let each
        // column be read once and in order. The decorator read the whole row first, so the application's first read threw.
        string? note = null;
        var inner = new ProviderLikeReader(sequential: true, new Set(["A", "B", "C"], [[1, "x", null], [2, "y", null]]));
        var reader = TrackSequential(inner, c => note = c);

        var seen = new List<string>();
        while (reader.Read())
            seen.Add($"{reader.GetInt32(0)}{reader.GetString(1)}{reader.IsDBNull(2)}");
        reader.Close();

        Assert.Equal(["1xTrue", "2yTrue"], seen);
        Assert.Equal("""[{"A":1,"B":"x","C":null},{"A":2,"B":"y","C":null}]""", note);
    }

    [Fact]
    public void Under_sequential_access_the_note_holds_what_the_application_read_by_any_getter()
    {
        string? note = null;
        var at = new DateTime(2026, 10, 26, 12, 0, 0);
        var inner = new ProviderLikeReader(sequential: true, new Set(["Id", "At", "Skipped", "Tags"], [[7, at, "never read", new[] { 1 }]]));
        var reader = TrackSequential(inner, c => note = c);

        Assert.True(reader.Read());
        Assert.Equal(7, reader.GetFieldValue<int>(0));
        Assert.Equal(new DateTimeOffset(at, TimeSpan.Zero), reader.GetFieldValue<DateTimeOffset>(1));
        Assert.Equal([1], reader.GetFieldValue<List<int>>(3));
        Assert.False(reader.Read());
        reader.Close();

        Assert.Equal("""[{"Id":7,"At":"2026-10-26T12:00:00+00:00","Tags":[1]}]""", note);
    }

    [Fact]
    public void Under_sequential_access_GetValues_is_recorded()
    {
        string? note = null;
        var reader = TrackSequential(new ProviderLikeReader(sequential: true, new Set(["A", "B"], [[1, null]])), c => note = c);

        Assert.True(reader.Read());
        var values = new object[2];
        Assert.Equal(2, reader.GetValues(values));
        reader.Close();

        Assert.Equal("""[{"A":1,"B":null}]""", note);
    }

    [Fact]
    public void A_cell_the_capture_cannot_read_leaves_the_applications_row_alone()
    {
        // SqlClient's GetValue on a geography column needs Microsoft.SqlServer.Types and throws without it, while the
        // application reads the column another way. The capture's exception escaped from the application's Read().
        string? note = null;
        var reader = Track(new ProviderLikeReader(new Set(["Id", "Shape"], [[1, new Unreadable([9, 9])]])), c => note = c);

        Assert.True(reader.Read());
        Assert.Equal(new byte[] { 9, 9 }, reader.GetFieldValue<byte[]>(1));
        reader.Close();

        Assert.Equal("""[{"Id":1,"Shape":"(not readable as a value: InvalidOperationException)"}]""", note);
    }

    // ─── F5: nothing thrown from the note ─────────────────────

    [Fact]
    public void A_value_whose_text_or_properties_throw_does_not_fail_the_application()
    {
        string? note = null;
        var reader = Track(new ProviderLikeReader(new Set(["Id", "Odd"], [[1, new Hostile()]])), c => note = c);

        Assert.True(reader.Read());
        Assert.False(reader.Read());
        Assert.Null(Record.Exception(reader.Close));

        Assert.Equal("""[{"Id":1,"Odd":"(Hostile: InvalidOperationException)"}]""", note);
    }

    [Fact]
    public void A_provider_object_is_written_as_its_text_not_its_properties()
    {
        // System.Text.Json reflected over a SQL Server geography value's properties and one of them threw from
        // RelationalDataReader.DisposeAsync, hiding the application's own exception.
        string? note = null;
        var reader = Track(new ProviderLikeReader(new Set(["Shape"], [[new ProviderValue("POINT (1 2)")]])), c => note = c);

        while (reader.Read()) { }
        reader.Close();

        Assert.Equal("""[{"Shape":"POINT (1 2)"}]""", note);
    }

    [Fact]
    public void A_logger_that_throws_does_not_fail_Read_or_Close()
    {
        var reader = Track(new ProviderLikeReader(new Set(["Id"], [[1]])), _ => throw new InvalidOperationException("logger"));

        Assert.Null(Record.Exception(() => { while (reader.Read()) { } }));
        Assert.Null(Record.Exception(reader.Close));
    }

    [Fact]
    public void A_logger_that_throws_does_not_fail_Dispose()
    {
        var reader = Track(new ProviderLikeReader(new Set(["Id"], [[1]])), _ => throw new InvalidOperationException("logger"));

        Assert.True(reader.Read());
        Assert.Null(Record.Exception(reader.Dispose));
    }

    [Fact]
    public async Task A_logger_that_throws_does_not_fail_CloseAsync_or_DisposeAsync()
    {
        var closing = Track(new ProviderLikeReader(new Set(["Id"], [[1]])), _ => throw new InvalidOperationException("logger"));
        Assert.True(await closing.ReadAsync());
        Assert.Null(await Record.ExceptionAsync(closing.CloseAsync));

        var disposing = Track(new ProviderLikeReader(new Set(["Id"], [[1]])), _ => throw new InvalidOperationException("logger"));
        Assert.True(await disposing.ReadAsync());
        Assert.Null(await Record.ExceptionAsync(async () => await disposing.DisposeAsync()));
    }

    // ─── F7: every result set ─────────────────────────────────

    [Fact]
    public void Every_result_set_read_to_the_end_is_in_the_note_each_on_its_own_line()
    {
        // The note was written when the first Read() found no row, so a second result set was never in it.
        string? note = null;
        var reader = Track(new ProviderLikeReader(new Set(["a"], [[1], [2]]), new Set(["b"], [["x"]])), c => note = c);

        do { while (reader.Read()) { } } while (reader.NextResult());
        reader.Close();

        Assert.Equal("[{\"a\":1},{\"a\":2}]\n[{\"b\":\"x\"}]", note);
    }

    [Fact]
    public void Result_sets_read_one_row_each_are_kept_apart()
    {
        // EF Core reads one row of each set of a SQL Server SaveChanges batch and moves on; the rows were one list.
        string? note = null;
        var reader = Track(new ProviderLikeReader(new Set(["Id"], [[3]]), new Set(["Id"], [[2]])), c => note = c);

        Assert.True(reader.Read());
        Assert.True(reader.NextResult());
        Assert.True(reader.Read());
        Assert.False(reader.NextResult());
        reader.Close();

        Assert.Equal("[{\"Id\":3}]\n[{\"Id\":2}]", note);
    }

    [Fact]
    public void Each_result_set_names_its_own_columns()
    {
        string? note = null;
        var reader = Track(new ProviderLikeReader(new Set(["a"], [[1], [2]]), new Set(["b"], [["x"]])), c => note = c,
            SqlResponseDetail.RowCountAndColumns);

        do { while (reader.Read()) { } } while (reader.NextResult());
        reader.Close();

        Assert.Equal("2 rows [a]\n1 row [b]", note);
    }

    [Fact]
    public void One_result_set_gives_the_note_5_10_1_gave()
    {
        string? note = null;
        var reader = Track(new ProviderLikeReader(new Set(["Id", "At", "Note", "Tags", "Price"],
            [[1, new DateTime(2026, 10, 26, 12, 0, 0), null, new[] { 1, 2 }, 12.5m]])), c => note = c);

        while (reader.Read()) { }
        reader.Close();

        Assert.Equal("""[{"Id":1,"At":"2026-10-26T12:00:00","Note":null,"Tags":[1,2],"Price":12.5}]""", note);
    }

    [Fact]
    public void An_empty_result_set_still_names_its_columns_at_Summarised()
    {
        string? note = null;
        var reader = Track(new ProviderLikeReader(new Set(["Id", "Name"], [])), c => note = c, SqlResponseDetail.RowCountAndColumns);

        Assert.False(reader.Read());
        reader.Close();

        Assert.Equal("0 rows [Id, Name]", note);
    }

    [Fact]
    public void The_note_is_written_once_whichever_way_the_reader_ends()
    {
        var notes = new List<string?>();
        var reader = Track(new ProviderLikeReader(new Set(["Id"], [[1]])), notes.Add);

        while (reader.Read()) { }
        Assert.False(reader.NextResult());
        reader.Close();
        reader.Dispose();

        Assert.Equal(["""[{"Id":1}]"""], notes);
    }

    // ─── The provider-like reader ─────────────────────────────

    internal sealed record Set(string[] Columns, object?[][] Rows);

    /// <summary>A value the provider's GetValue cannot produce (a UDT without its assembly) but GetFieldValue can.</summary>
    internal sealed record Unreadable(byte[] Bytes);

    /// <summary>A provider type with public properties (a geography, a range, a network address).</summary>
    internal sealed class ProviderValue(string text)
    {
        public string Text => text;
        public bool IsNull => throw new InvalidOperationException("Data is Null.");
        public override string ToString() => text;
    }

    internal sealed class Hostile
    {
        public string Boom => throw new InvalidOperationException("getter");
        public override string ToString() => throw new InvalidOperationException("ToString");
    }

    private sealed class Column : DbColumn
    {
        public Column(string name) => ColumnName = name;
    }

    /// <summary>
    /// A reader that behaves as a provider's does where the decorator used to differ: GetFieldValue converts what
    /// GetValue does not, under SequentialAccess a column is read once and in order, a column can be unreadable as an
    /// object, results come in several sets, and the schema, stream and provider-specific members answer.
    /// </summary>
    internal sealed class ProviderLikeReader : DbDataReader, IDbColumnSchemaGenerator
    {
        private readonly Set[] _sets;
        private readonly bool _sequential;
        private readonly HashSet<int> _consumed = [];
        private int _set;
        private int _row = -1;
        private int _lastConsumed = -1;
        private bool _closed;

        public List<string> Calls { get; } = [];

        public ProviderLikeReader(params Set[] sets) : this(false, sets) { }

        public ProviderLikeReader(bool sequential, params Set[] sets)
        {
            _sequential = sequential;
            _sets = sets;
        }

        private Set Current => _sets[_set];

        private object? Consume(int ordinal)
        {
            if (_row < 0 || _row >= Current.Rows.Length)
                throw new InvalidOperationException("No row is available.");
            if (_sequential)
            {
                if (ordinal < _lastConsumed || _consumed.Contains(ordinal))
                    throw new InvalidOperationException(
                        $"Invalid attempt to read from column ordinal '{ordinal}'. With CommandBehavior.SequentialAccess, " +
                        $"you may only read from column ordinal '{_lastConsumed + 1}' or greater.");
                _consumed.Add(ordinal);
                _lastConsumed = ordinal;
            }
            return Current.Rows[_row][ordinal];
        }

        public override bool Read()
        {
            _consumed.Clear();
            _lastConsumed = -1;
            if (_row < Current.Rows.Length) _row++;
            return _row < Current.Rows.Length;
        }

        public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Read());

        public override bool NextResult()
        {
            _consumed.Clear();
            _lastConsumed = -1;
            if (_set + 1 >= _sets.Length)
            {
                _row = Current.Rows.Length;
                return false;
            }
            _set++;
            _row = -1;
            return true;
        }

        public override Task<bool> NextResultAsync(CancellationToken cancellationToken) => Task.FromResult(NextResult());

        public override int FieldCount => Current.Columns.Length;

        public override int VisibleFieldCount
        {
            get
            {
                Calls.Add(nameof(VisibleFieldCount));
                return Current.Columns.Length - 1;
            }
        }

        public override int RecordsAffected => 7;
        public override bool HasRows => Current.Rows.Length > 0;
        public override bool IsClosed => _closed;
        public override int Depth => 0;
        public override void Close() => _closed = true;

        public override string GetName(int ordinal) => Current.Columns[ordinal];
        public override int GetOrdinal(string name) => Array.IndexOf(Current.Columns, name);
        public override Type GetFieldType(int ordinal) => typeof(object);
        public override string GetDataTypeName(int ordinal) => "fake";

        public override bool IsDBNull(int ordinal)
        {
            if (_row < 0 || _row >= Current.Rows.Length)
                throw new InvalidOperationException("No row is available.");
            if (_sequential && ordinal < _lastConsumed)
                throw new InvalidOperationException($"Invalid attempt to read from column ordinal '{ordinal}'.");
            return Current.Rows[_row][ordinal] is null;
        }

        public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(IsDBNullAsync));
            return Task.FromResult(IsDBNull(ordinal));
        }

        public override object GetValue(int ordinal) => Consume(ordinal) switch
        {
            null => DBNull.Value,
            Unreadable => throw new InvalidOperationException("Microsoft.SqlServer.Types is not available."),
            var value => value,
        };

        public override T GetFieldValue<T>(int ordinal)
        {
            Calls.Add($"GetFieldValue<{typeof(T).Name}>");
            object converted = Consume(ordinal) switch
            {
                DateTime at when typeof(T) == typeof(DateTimeOffset) => new DateTimeOffset(at, TimeSpan.Zero),
                int[] array when typeof(T) == typeof(List<int>) => array.ToList(),
                Unreadable unreadable when typeof(T) == typeof(byte[]) => unreadable.Bytes,
                var value => value!,
            };
            return (T)converted;
        }

        public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken)
        {
            Calls.Add("GetFieldValueAsync");
            return Task.FromResult(GetFieldValue<T>(ordinal));
        }

        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(GetOrdinal(name));
        public override bool GetBoolean(int ordinal) => (bool)Consume(ordinal)!;
        public override byte GetByte(int ordinal) => (byte)Consume(ordinal)!;
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
        public override char GetChar(int ordinal) => (char)Consume(ordinal)!;
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
        public override DateTime GetDateTime(int ordinal) => (DateTime)Consume(ordinal)!;
        public override decimal GetDecimal(int ordinal) => (decimal)Consume(ordinal)!;
        public override double GetDouble(int ordinal) => (double)Consume(ordinal)!;
        public override float GetFloat(int ordinal) => (float)Consume(ordinal)!;
        public override Guid GetGuid(int ordinal) => (Guid)Consume(ordinal)!;
        public override short GetInt16(int ordinal) => (short)Consume(ordinal)!;
        public override int GetInt32(int ordinal) => (int)Consume(ordinal)!;
        public override long GetInt64(int ordinal) => (long)Consume(ordinal)!;
        public override string GetString(int ordinal) => (string)Consume(ordinal)!;

        public override int GetValues(object[] values)
        {
            var count = Math.Min(values.Length, FieldCount);
            for (var i = 0; i < count; i++)
                values[i] = GetValue(i);
            return count;
        }

        public override Stream GetStream(int ordinal)
        {
            Calls.Add(nameof(GetStream));
            return new MemoryStream([1, 2, 3]);
        }

        public override TextReader GetTextReader(int ordinal)
        {
            Calls.Add(nameof(GetTextReader));
            return new StringReader("text");
        }

        public override Type GetProviderSpecificFieldType(int ordinal)
        {
            Calls.Add(nameof(GetProviderSpecificFieldType));
            return typeof(ProviderValue);
        }

        public override object GetProviderSpecificValue(int ordinal)
        {
            Calls.Add(nameof(GetProviderSpecificValue));
            return new ProviderValue("specific");
        }

        public override int GetProviderSpecificValues(object[] values)
        {
            Calls.Add(nameof(GetProviderSpecificValues));
            values[0] = new ProviderValue("specific");
            return 1;
        }

        public override DataTable GetSchemaTable()
        {
            Calls.Add(nameof(GetSchemaTable));
            return new DataTable("schema");
        }

        public override Task<DataTable?> GetSchemaTableAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add(nameof(GetSchemaTableAsync));
            return Task.FromResult<DataTable?>(new DataTable("schema"));
        }

        public ReadOnlyCollection<DbColumn> GetColumnSchema()
        {
            Calls.Add(nameof(GetColumnSchema));
            return new ReadOnlyCollection<DbColumn>([new Column(Current.Columns[0])]);
        }

        public override Task<ReadOnlyCollection<DbColumn>> GetColumnSchemaAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add(nameof(GetColumnSchemaAsync));
            return Task.FromResult(GetColumnSchema());
        }

        protected override DbDataReader GetDbDataReader(int ordinal)
        {
            Calls.Add("GetData");
            return new ProviderLikeReader(new Set(["nested"], [["x"]]));
        }

        public override IEnumerator GetEnumerator() => new DbEnumerator(this);
    }
}
