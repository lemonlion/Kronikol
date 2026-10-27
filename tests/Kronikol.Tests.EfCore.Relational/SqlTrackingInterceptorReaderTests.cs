using System.Reflection;
using Kronikol.Extensions.EfCore.Relational;
using Kronikol.Reports;
using Kronikol.Sql;
using Kronikol.Tracking;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Kronikol.Tests.EfCore.Relational;

/// <summary>
/// The reader EF Core gets back from the interceptor, through a real provider (#193, #197; plans/SQL_RESPONSE_CAPTURE_PLAN.md
/// F1 to F3, facts 3, 13 and 15). A TrackingDbDataReader stands in for the provider's own reader only where nothing EF
/// Core runs on it needs the provider's type: a SaveChanges batch on a provider whose update pipeline the IL scan
/// cleared, and any command on a context whose every value is read through DbDataReader's own methods.
/// </summary>
public class SqlTrackingInterceptorReaderTests : IDisposable
{
    private readonly string _testId = Guid.NewGuid().ToString();
    private readonly string _service = "Db-" + Guid.NewGuid().ToString("N")[..8];
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public SqlTrackingInterceptorReaderTests() => _connection.Open();

    public void Dispose()
    {
        _connection.Dispose();
        ProcessDiagnostics.Remove(SqlTrackingInterceptor.NotCapturedKey($"SqlTrackingInterceptor ({_service})", typeof(NoteContext)));
        ProcessDiagnostics.Remove(SqlCaptureFailure.KeyFor($"SqlTrackingInterceptor ({_service})"));
    }

    private RequestResponseLog[] Responses() => RequestResponseLogger.RequestAndResponseLogs
        .Where(l => l.TestId == _testId && l.Type == RequestResponseType.Response).ToArray();

    private SqlTrackingInterceptor Interceptor(string? providerName = null)
    {
        var options = new SqlTrackingInterceptorOptions
        {
            ServiceName = _service,
            CurrentTestInfoFetcher = () => ("Reader handed back", _testId),
        };
        return providerName is null
            ? new SqlTrackingInterceptor(options)
            : new SqlTrackingInterceptor(options) { ProviderNameOf = _ => providerName };
    }

    // ─── Fact 3: the values the provider converts reach the application ─────────

    public sealed class Event
    {
        public int Id { get; set; }
        public DateTimeOffset At { get; set; }
        public DateOnly Day { get; set; }
        public TimeOnly Time { get; set; }
        public TimeSpan Span { get; set; }
    }

    private sealed class EventContext(SqliteConnection connection, SqlTrackingInterceptor interceptor) : DbContext
    {
        public DbSet<Event> Events => Set<Event>();

        protected override void OnConfiguring(DbContextOptionsBuilder builder) =>
            builder.UseSqlite(connection).AddInterceptors(interceptor);
    }

    private static readonly Event Sample = new()
    {
        At = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(2)),
        Day = new DateOnly(2026, 10, 26),
        Time = new TimeOnly(12, 30),
        Span = TimeSpan.FromSeconds(90),
    };

    [Fact]
    public async Task Sqlite_values_read_through_GetFieldValue_reach_the_application_and_the_note()
    {
        await using var context = new EventContext(_connection, Interceptor());
        await context.Database.EnsureCreatedAsync();
        context.Events.Add(new Event { At = Sample.At, Day = Sample.Day, Time = Sample.Time, Span = Sample.Span });
        await context.SaveChangesAsync();

        Assert.Equal(Sample.At, context.Events.AsNoTracking().Select(e => e.At).Single());
        Assert.Equal(Sample.Day, await context.Events.AsNoTracking().Select(e => e.Day).SingleAsync());
        Assert.Equal(Sample.Time, context.Events.AsNoTracking().Select(e => e.Time).Single());
        Assert.Equal(Sample.Span, context.Events.AsNoTracking().Select(e => e.Span).Single());
        var whole = await context.Events.AsNoTracking().SingleAsync();

        Assert.Equal((Sample.At, Sample.Day, Sample.Time, Sample.Span), (whole.At, whole.Day, whole.Time, whole.Span));
        Assert.Contains("2026-10-26", Responses()[^1].Content);
    }

    // ─── Fact 5 for SQLite, fact 13: SaveChanges ─────────────────────────────────

    [Fact]
    public async Task Sqlite_SaveChanges_note_is_the_rows_it_returned_as_before()
    {
        await using var context = new EventContext(_connection, Interceptor());
        await context.Database.EnsureCreatedAsync();
        var before = Responses().Length;

        context.Events.Add(new Event { At = Sample.At });
        await context.SaveChangesAsync();

        Assert.Equal("""[{"Id":1}]""", Assert.Single(Responses()[before..]).Content);
    }

    [Fact]
    public async Task SaveChanges_on_a_provider_not_on_the_allow_list_gets_its_own_reader_and_a_row_count()
    {
        await using var context = new EventContext(_connection, Interceptor(providerName: "Npgsql.EntityFrameworkCore.PostgreSQL"));
        await context.Database.EnsureCreatedAsync();
        var before = Responses().Length;

        var added = new Event { At = Sample.At };
        context.Events.Add(added);
        context.SaveChanges();
        context.Events.Add(new Event { At = Sample.At });
        await context.SaveChangesAsync();

        Assert.Equal(1, added.Id);
        Assert.Equal(["1 rows affected", "1 rows affected"], Responses()[before..].Select(r => r.Content));
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", true)]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite", true)]
    [InlineData("Pomelo.EntityFrameworkCore.MySql", true)]
    [InlineData("MySql.EntityFrameworkCore", true)]
    [InlineData("FirebirdSql.EntityFrameworkCore.Firebird", true)]
    [InlineData("Oracle.EntityFrameworkCore", true)]
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL", false)]
    [InlineData("Google.Cloud.EntityFrameworkCore.Spanner", false)]
    [InlineData("Some.Other.Provider", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void The_update_pipelines_cleared_to_take_any_reader_are_named(string? provider, bool cleared)
    {
        Assert.Equal(cleared, SqlTrackingInterceptor.UpdatePipelineTakesAnyReader(provider));
    }

    // ─── Facts 14 and 15: a context with a value read through the provider's own reader ──

    public sealed class Note
    {
        public int Id { get; set; }
        public string Text { get; set; } = "";
    }

    /// <summary>
    /// Text read through <see cref="SqliteDataReader.GetString"/> itself, as SQL Server's spatial columns are read through
    /// SqlDataReader.GetSqlBytes: EF Core's shaper casts the reader to SqliteDataReader for it, so any other reader fails.
    /// </summary>
    private sealed class ProviderReadTextMapping : StringTypeMapping
    {
        public const string StoreType = "provider_text";

        public ProviderReadTextMapping() : base(StoreType, System.Data.DbType.String) { }

        private ProviderReadTextMapping(RelationalTypeMappingParameters parameters) : base(parameters) { }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) => new ProviderReadTextMapping(parameters);

        public override MethodInfo GetDataReaderMethod() => typeof(SqliteDataReader).GetMethod(nameof(SqliteDataReader.GetString), [typeof(int)])!;
    }

    private sealed class ProviderReadTextPlugin : IRelationalTypeMappingSourcePlugin
    {
        public RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo) =>
            mappingInfo.StoreTypeName == ProviderReadTextMapping.StoreType ? new ProviderReadTextMapping() : null;
    }

    private sealed class ProviderReadTextExtension : IDbContextOptionsExtension
    {
        public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);

        public void ApplyServices(IServiceCollection services) =>
            services.AddSingleton<IRelationalTypeMappingSourcePlugin, ProviderReadTextPlugin>();

        public void Validate(IDbContextOptions options) { }

        private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
        {
            public override bool IsDatabaseProvider => false;
            public override string LogFragment => "";
            public override int GetServiceProviderHashCode() => 0;
            public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;
            public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) { }
        }
    }

    private sealed class NoteContext(SqliteConnection connection, SqlTrackingInterceptor interceptor) : DbContext
    {
        public DbSet<Note> Notes => Set<Note>();

        protected override void OnConfiguring(DbContextOptionsBuilder builder)
        {
            builder.UseSqlite(connection).AddInterceptors(interceptor);
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(new ProviderReadTextExtension());
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Note>().Property(n => n.Text).HasColumnType(ProviderReadTextMapping.StoreType);
    }

    private async Task<NoteContext> NotesAsync(int count)
    {
        var context = new NoteContext(_connection, Interceptor());
        await context.Database.EnsureCreatedAsync();
        for (var i = 1; i <= count; i++)
            context.Notes.Add(new Note { Text = $"note {i}" });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return context;
    }

    [Fact]
    public async Task A_context_reading_through_the_providers_reader_gets_it_back_and_a_row_count()
    {
        await using var context = await NotesAsync(2);
        var before = Responses().Length;

        var all = context.Notes.OrderBy(n => n.Id).ToList();
        var first = await context.Notes.OrderBy(n => n.Id).FirstAsync();
        var single = context.Notes.Single(n => n.Id == 2);
        using (var abandoned = context.Notes.OrderBy(n => n.Id).AsEnumerable().GetEnumerator())
            abandoned.MoveNext();
        var listed = await context.Notes.ToListAsync();

        Assert.Equal(["note 1", "note 2"], all.Select(n => n.Text));
        Assert.Equal(("note 1", "note 2", 2), (first.Text, single.Text, listed.Count));
        Assert.Equal(["2 rows", "1 rows", "1 rows", "1 rows", "2 rows"], Responses()[before..].Select(r => r.Content));
    }

    [Fact]
    public async Task A_context_reading_through_the_providers_reader_is_named_once_in_the_diagnostics()
    {
        await using var context = await NotesAsync(1);

        _ = context.Notes.ToList();
        _ = context.Notes.ToList();

        var diagnostic = Assert.Single(ProcessDiagnostics.Entries, e =>
            e.Kind == DiagnosticKind.CaptureDegraded && e.Message.StartsWith($"SqlTrackingInterceptor ({_service})", StringComparison.Ordinal));
        Assert.Contains(nameof(NoteContext), diagnostic.Message);
        Assert.Contains("Note.Text is read through SqliteDataReader.GetString", diagnostic.Message);
    }

    [Fact]
    public async Task The_verdict_on_a_model_is_kept_with_the_model()
    {
        await using var notes = await NotesAsync(0);
        await using var events = new EventContext(_connection, Interceptor());

        Assert.Equal("Note.Text is read through SqliteDataReader.GetString", ProviderReaderMethods.FirstUse(notes));
        Assert.Null(ProviderReaderMethods.FirstUse(events));
        Assert.True(ProviderReaderMethods.IsKnown(notes.Model));
        Assert.True(ProviderReaderMethods.IsKnown(events.Model));
    }
}
