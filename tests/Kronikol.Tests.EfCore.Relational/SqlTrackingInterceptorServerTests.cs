using System.Text.Json;
using Kronikol.Extensions.EfCore.Relational;
using Kronikol.Reports;
using Kronikol.Tracking;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Kronikol.Tests.EfCore.Relational;

/// <summary>
/// The interceptor on the providers whose own code needs their own reader, against real servers
/// (plans/SQL_RESPONSE_CAPTURE_PLAN.md facts 1, 2, 4 and 5). Until 5.11.1 every Npgsql SaveChanges failed with
/// "Unable to cast object of type 'TrackingDbDataReader' to type 'NpgsqlDataReader'" (#193), and a query on SQL Server
/// that read a spatial column failed the same way.
/// </summary>
public abstract class InterceptorServerFacts : IAsyncLifetime
{
    protected readonly string TestId = Guid.NewGuid().ToString();
    protected readonly string Service = "Db-" + Guid.NewGuid().ToString("N")[..8];
    protected readonly string Database = "krkl_" + Guid.NewGuid().ToString("N")[..12];
    private readonly List<DbContext> _created = [];

    protected SqlTrackingInterceptor Interceptor() => new(new SqlTrackingInterceptorOptions
    {
        ServiceName = Service,
        CurrentTestInfoFetcher = () => ("Interceptor on a real server", TestId),
    });

    protected RequestResponseLog[] Responses() => RequestResponseLogger.RequestAndResponseLogs
        .Where(l => l.TestId == TestId && l.Type == RequestResponseType.Response).ToArray();

    protected async Task<T> CreatedAsync<T>(T context) where T : DbContext
    {
        await context.Database.EnsureCreatedAsync();
        _created.Add(context);
        return context;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (var context in _created)
        {
            ProcessDiagnostics.Remove(SqlTrackingInterceptor.NotCapturedKey($"SqlTrackingInterceptor ({Service})", context.GetType()));
            await context.Database.EnsureDeletedAsync();
            await context.DisposeAsync();
        }
        GC.SuppressFinalize(this);
    }
}

public class PostgresInterceptorTests(PostgresServerFixture server) : InterceptorServerFacts, IClassFixture<PostgresServerFixture>
{
    public sealed class Order
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public DateTimeOffset At { get; set; }
        public List<int> Tags { get; set; } = [];
        public JsonDocument? Data { get; set; }
    }

    private sealed class ShopContext(string connectionString, SqlTrackingInterceptor interceptor) : DbContext
    {
        public DbSet<Order> Orders => Set<Order>();

        protected override void OnConfiguring(DbContextOptionsBuilder builder) =>
            builder.UseNpgsql(connectionString).AddInterceptors(interceptor);
    }

    private static readonly DateTimeOffset At = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private Task<ShopContext> ShopAsync() => CreatedAsync(new ShopContext(server.Require(Database), Interceptor()));

    [Fact]
    public async Task Every_SaveChanges_succeeds_and_says_how_many_rows_it_changed()
    {
        var context = await ShopAsync();
        var before = Responses().Length;

        var first = new Order { Name = "one", At = At };
        context.Orders.Add(first);
        context.SaveChanges();
        context.Orders.Add(new Order { Name = "two", At = At });
        await context.SaveChangesAsync();
        first.Name = "renamed";
        await context.SaveChangesAsync();
        context.Orders.Remove(first);
        context.SaveChanges();
        context.Orders.AddRange(new Order { Name = "a", At = At }, new Order { Name = "b", At = At }, new Order { Name = "c", At = At });
        await context.SaveChangesAsync();

        Assert.True(first.Id > 0);
        Assert.Equal(
            ["1 rows affected", "1 rows affected", "1 rows affected", "1 rows affected", "3 rows affected"],
            Responses()[before..].Select(r => r.Content));
    }

    [Fact]
    public async Task Values_Npgsql_converts_reach_the_application_and_the_note()
    {
        var context = await ShopAsync();
        context.Orders.Add(new Order { Name = "one", At = At, Tags = [1, 2], Data = JsonDocument.Parse("""{"a":1}""") });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        Assert.Equal(At, await context.Orders.Select(o => o.At).SingleAsync());
        Assert.Equal([1, 2], context.Orders.Select(o => o.Tags).Single());
        Assert.Equal(1, context.Orders.Select(o => o.Data).Single()!.RootElement.GetProperty("a").GetInt32());
        var whole = await context.Orders.SingleAsync();

        Assert.Equal(("one", At, 2), (whole.Name, whole.At, whole.Tags.Count));
        Assert.Contains("\"Name\":\"one\"", Responses()[^1].Content);
    }
}

public class SqlServerInterceptorTests(SqlServerServerFixture server) : InterceptorServerFacts, IClassFixture<SqlServerServerFixture>
{
    public sealed class Place
    {
        public int Id { get; set; }
        public int Rank { get; set; }
        public Point Location { get; set; } = null!;
    }

    public sealed class Event
    {
        public int Id { get; set; }
        public DateOnly Day { get; set; }
        public TimeOnly Time { get; set; }
    }

    private sealed class PlaceContext(string connectionString, SqlTrackingInterceptor interceptor) : DbContext
    {
        public DbSet<Place> Places => Set<Place>();

        protected override void OnConfiguring(DbContextOptionsBuilder builder) =>
            builder.UseSqlServer(connectionString, sql => sql.UseNetTopologySuite()).AddInterceptors(interceptor);
    }

    private sealed class CalendarContext(string connectionString, SqlTrackingInterceptor interceptor) : DbContext
    {
        public DbSet<Event> Events => Set<Event>();

        protected override void OnConfiguring(DbContextOptionsBuilder builder) =>
            builder.UseSqlServer(connectionString).AddInterceptors(interceptor);
    }

    [Fact]
    public async Task DateOnly_and_TimeOnly_reach_the_application_and_SaveChanges_notes_are_as_before()
    {
        var context = await CreatedAsync(new CalendarContext(server.Require(Database), Interceptor()));
        var before = Responses().Length;

        var single = new Event { Day = new DateOnly(2026, 10, 26), Time = new TimeOnly(12, 30) };
        context.Events.Add(single);
        await context.SaveChangesAsync();
        var batch = new[] { new Event(), new Event(), new Event() };
        context.Events.AddRange(batch);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        Assert.Equal(new DateOnly(2026, 10, 26), await context.Events.Where(e => e.Id == single.Id).Select(e => e.Day).SingleAsync());
        Assert.Equal(new TimeOnly(12, 30), context.Events.Where(e => e.Id == single.Id).Select(e => e.Time).Single());

        var notes = Responses()[before..];
        Assert.Equal($$"""[{"Id":{{single.Id}}}]""", notes[0].Content);
        var batchRows = JsonDocument.Parse(notes[1].Content!).RootElement.EnumerateArray().ToArray();
        Assert.Equal(batch.Select(e => e.Id).Order(), batchRows.Select(r => r.GetProperty("Id").GetInt32()).Order());
        Assert.All(batchRows, row => Assert.True(row.TryGetProperty("_Position", out _)));
    }

    [Fact]
    public async Task A_spatial_context_keeps_SqlDataReader_and_its_notes_count_rows()
    {
        var context = await CreatedAsync(new PlaceContext(server.Require(Database), Interceptor()));
        context.Places.AddRange(
            new Place { Rank = 1, Location = new Point(1, 2) { SRID = 4326 } },
            new Place { Rank = 2, Location = new Point(3, 4) { SRID = 4326 } });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var before = Responses().Length;

        var places = await context.Places.OrderBy(p => p.Rank).ToListAsync();
        var ranks = context.Places.Select(p => p.Rank).ToList();

        Assert.Equal((1.0, 2.0), (places[0].Location.X, places[0].Location.Y));
        Assert.Equal(2, ranks.Count);
        Assert.Equal(["2 rows", "2 rows"], Responses()[before..].Select(r => r.Content));
        var diagnostic = Assert.Single(ProcessDiagnostics.Entries, e => e.Message.StartsWith($"SqlTrackingInterceptor ({Service}):", StringComparison.Ordinal));
        Assert.Contains("Place.Location is read through SqlDataReader.GetSqlBytes", diagnostic.Message);
    }
}
