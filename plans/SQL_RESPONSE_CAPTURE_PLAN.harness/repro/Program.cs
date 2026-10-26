// Issue #193 reproduction: every case runs once without and once with Kronikol's SqlTrackingInterceptor.
// Usage: dotnet run -- [pg] [mssql] [sqlite] [dapper]   (no argument = all)
using Dapper;
using Kronikol.Extensions.EfCore.Relational;
using Kronikol.Extensions.Npgsql;
using Kronikol.Extensions.SqlClient;
using Kronikol.Tracking;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Npgsql;

var only = args.Length == 0 ? null : new HashSet<string>(args);
bool Want(string p) => only is null || only.Contains(p);

const string PgCs = "Host=localhost;Port=55432;Username=postgres;Password=k193pass;Database=k193";
const string MsCs = @"Server=(localdb)\MSSQLLocalDB;Database=k193;Trusted_Connection=True;TrustServerCertificate=True";
const string MsTcpCs = "Server=localhost,51433;User Id=sa;Password=K193_pass!word;TrustServerCertificate=True;Database=k193";
var sqlitePath =Path.Combine(AppContext.BaseDirectory, "k193.db");
var SqliteCs = $"Data Source={sqlitePath}";

var results = new List<(string Provider, string Case, string Plain, string Tracked)>();

if (args.Contains("ado")) { await AdoProbe.Run(PgCs); return; }
if (args.Contains("uri")) { UriProbe.Run(PgCs, SqliteCs); return; }
if (args.Contains("msprobe")) { var sw = System.Diagnostics.Stopwatch.StartNew(); try { await using var pc = new SqlConnection(MsTcpCs.Replace("Database=k193", "Database=master") + ";Connect Timeout=90"); await pc.OpenAsync(); Console.WriteLine($"open {sw.ElapsedMilliseconds} ms"); await using var cmd = pc.CreateCommand(); cmd.CommandTimeout = 300; cmd.CommandText = "SELECT @@VERSION"; Console.WriteLine(cmd.ExecuteScalar()); Console.WriteLine($"select {sw.ElapsedMilliseconds} ms"); cmd.CommandText = "IF DB_ID('k193') IS NULL CREATE DATABASE k193"; cmd.ExecuteNonQuery(); Console.WriteLine($"create {sw.ElapsedMilliseconds} ms"); } catch (Exception ex) { Console.WriteLine($"{sw.ElapsedMilliseconds} ms: {ex}"); } return; }
if (Want("pg")) await RunProvider("pg", o => o.UseNpgsql(PgCs));
if (Want("mssql")) await RunProvider("mssql", o => o.UseSqlServer(MsCs, x => { x.UseNetTopologySuite(); x.UseHierarchyId(); }));
if (Want("mstcp")) await RunProvider("mstcp", o => o.UseSqlServer(MsTcpCs, x => { x.UseNetTopologySuite(); x.UseHierarchyId(); }));
if (Want("sqlite")) await RunProvider("sqlite", o => o.UseSqlite(SqliteCs));
if (Want("dapper")) await RunDapper();

Console.WriteLine();
Console.WriteLine($"{"provider",-7} | {"case",-38} | {"no interceptor",-14} | with interceptor");
foreach (var r in results)
    Console.WriteLine($"{r.Provider,-7} | {r.Case,-38} | {r.Plain,-14} | {r.Tracked}");

async Task RunProvider(string provider, Action<DbContextOptionsBuilder> configure)
{
    Ctx Make(bool tracked)
    {
        var b = new DbContextOptionsBuilder<Ctx>();
        configure(b);
        if (tracked)
            b.AddInterceptors(new SqlTrackingInterceptor(new SqlTrackingInterceptorOptions
            {
                CurrentTestInfoFetcher = () => ("Repro 193", "repro-193"),
                ResponseDetail = Environment.GetEnvironmentVariable("K193_DETAIL") is { } d ? Enum.Parse<Kronikol.Sql.SqlResponseDetail>(d) : null,
            }));
        return new Ctx(b.Options, provider);
    }

    var cases = new (string Name, Func<Ctx, Task> Body)[]
    {
        ("SaveChanges insert, generated key", async c => { c.Orders.Add(NewOrder("inserted")); await c.SaveChangesAsync(); }),
        ("SaveChanges insert, sync", c => { c.Orders.Add(NewOrder("inserted-sync")); c.SaveChanges(); return Task.CompletedTask; }),
        ("SaveChanges update, nothing returned", async c => { var id = await c.Orders.Select(o => o.Id).FirstAsync(); var o = new Order { Id = id }; c.Attach(o); o.Name = "updated"; c.Entry(o).Property(x => x.Name).IsModified = true; await c.SaveChangesAsync(); }),
        ("SaveChanges delete", async c => { var id = await c.Orders.Where(o => o.Name.StartsWith("victim")).Select(o => o.Id).FirstAsync(); c.Orders.Remove(new Order { Id = id, Name = "x" }); await c.SaveChangesAsync(); }),
        ("SaveChanges 3 inserts in one batch", async c => { c.Orders.AddRange(NewOrder("b1"), NewOrder("b2"), NewOrder("b3")); await c.SaveChangesAsync(); }),
        ("Query int + string columns", async c => { _ = await c.Orders.Select(o => new { o.Id, o.Name }).ToListAsync(); }),
        ("Query DateTimeOffset column", async c => { _ = await c.Orders.Select(o => o.At).ToListAsync(); }),
        ("Query DateOnly column", async c => { _ = await c.Orders.Select(o => o.Day).ToListAsync(); }),
        ("Query TimeOnly column", async c => { _ = await c.Orders.Select(o => o.Time).ToListAsync(); }),
        ("Query decimal + Guid columns", async c => { _ = await c.Orders.Select(o => new { o.Amount, o.Ref }).ToListAsync(); }),
        ("Query whole entity, sync", c => { _ = c.Orders.AsNoTracking().ToList(); return Task.CompletedTask; }),
        ("ExecuteUpdate (bulk)", async c => { await c.Orders.Where(o => o.Name == "nobody").ExecuteUpdateAsync(s => s.SetProperty(o => o.Name, "n")); }),
        ("SqlQuery<int> raw", async c => { _ = await c.Database.SqlQueryRaw<int>("SELECT 1 AS \"Value\"").ToListAsync(); }),
    };
    if (provider == "pg")
        cases = [.. cases,
            ("Query int[] as List<int> (pg)", async c => { _ = await c.Tagged.Select(t => t.Tags).ToListAsync(); }),
            ("SaveChanges insert List<int> (pg)", async c => { c.Tagged.Add(new Tagged { Tags = [7, 8] }); await c.SaveChangesAsync(); })];
    if (provider.StartsWith("ms"))
        cases = [.. cases,
            ("Query geography Point (mssql NTS)", async c => { _ = await c.Places.Select(p => p.Location).ToListAsync(); }),
            ("Query hierarchyid (mssql)", async c => { _ = await c.Places.Select(p => p.Path).ToListAsync(); }),
            ("SaveChanges two tables, batch (mssql)", async c => { c.Orders.Add(NewOrder("two-a")); c.Places.Add(new Place { Location = new Point(3, 4) { SRID = 4326 }, Path = HierarchyId.Parse("/2/") }); await c.SaveChangesAsync(); }),
            ("FromSql two result sets (mssql)", async c => { _ = await c.Database.SqlQueryRaw<int>("SELECT 1 AS [Value]; SELECT 2 AS [Value]").ToListAsync(); })];

    var caseFilter = Environment.GetEnvironmentVariable("K193_CASE");
    foreach (var (name, body) in cases.Where(c => caseFilter is null || c.Name.Contains(caseFilter)))
    {
        var plain = await Attempt(provider, () => Make(false), body);
        var tracked = await Attempt(provider, () => Make(true), body);
        if (name.Contains("batch") || name.Contains("generated key") || name.Contains("two ") || name.Contains("int + string") || name.Contains("geography") || name.Contains("DateTimeOffset") || name.Contains("update")) Console.WriteLine($"[{provider}] {name}: logged response = {RequestResponseLogger.RequestAndResponseLogs.LastOrDefault(l => l.TestId == "repro-193" && l.Type == RequestResponseType.Response)?.Content ?? "(none)"}");
        results.Add((provider, name, plain, tracked));
    }
}

async Task<string> Attempt(string provider, Func<Ctx> make, Func<Ctx, Task> body)
{
    try
    {
        await using (var setup = new Ctx(Clean(provider), provider))
        {
            // reset the schema and seed with no interceptor (make(false) contexts only reach here through Attempt's caller)
            await setup.Database.EnsureDeletedAsync();
            await setup.Database.EnsureCreatedAsync();
        }
        await using (var seed = new Ctx(Clean(provider), provider))
        {
            seed.Orders.AddRange(NewOrder("seed"), NewOrder("victim"));
            if (provider == "pg") seed.Tagged.Add(new Tagged { Tags = [1, 2, 3] });
            if (provider.StartsWith("ms")) seed.Places.Add(new Place { Location = new Point(1, 2) { SRID = 4326 }, Path = HierarchyId.Parse("/1/") });
            await seed.SaveChangesAsync();
        }
        await using var ctx = make();
        await body(ctx);
        return "OK";
    }
    catch (Exception ex)
    {
        var inner = ex;
        while (inner.InnerException is not null) inner = inner.InnerException;
        var msg = inner.Message.Replace('\n', ' ');
        if (msg.Length > 150) msg = msg[..150];
        if (Environment.GetEnvironmentVariable("K193_STACK") == "1") Console.WriteLine(inner.StackTrace);
        return $"FAIL {inner.GetType().Name}: {msg}";
    }
}

DbContextOptions<Ctx> Clean(string provider)
{
    var b = new DbContextOptionsBuilder<Ctx>();
    switch (provider)
    {
        case "pg": b.UseNpgsql(PgCs); break;
        case "mssql": b.UseSqlServer(MsCs, x => { x.UseNetTopologySuite(); x.UseHierarchyId(); }); break;
        case "mstcp": b.UseSqlServer(MsTcpCs, x => { x.UseNetTopologySuite(); x.UseHierarchyId(); }); break;
        default: b.UseSqlite(SqliteCs); break;
    }
    return b.Options;
}

static Order NewOrder(string name) => new()
{
    Name = name,
    At = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero),
    Day = new DateOnly(2026, 10, 26),
    Time = new TimeOnly(12, 30),
    Amount = 12.34m,
    Ref = Guid.NewGuid(),
};

async Task RunDapper()
{
    var testInfo = () => ((string Name, string Id)?)("Repro 193", "repro-193");
    foreach (var (name, open) in new (string, Func<System.Data.Common.DbConnection>)[]
    {
        ("Dapper QueryAsync via TrackingSqlConnection", () => new SqlConnection(MsCs.Replace("Database=k193", "Database=master")).WithTestTracking(new SqlClientTrackingOptions { CurrentTestInfoFetcher = testInfo })),
        ("Dapper Query (sync) via TrackingSqlConnection", () => new SqlConnection(MsCs.Replace("Database=k193", "Database=master")).WithTestTracking(new SqlClientTrackingOptions { CurrentTestInfoFetcher = testInfo })),
        ("Dapper Query (sync) via TrackingNpgsqlConnection", () => new NpgsqlConnection(PgCs.Replace("Database=k193", "Database=postgres")).WithTestTracking(new NpgsqlTrackingOptions { CurrentTestInfoFetcher = testInfo })),
        ("Dapper QueryAsync via TrackingNpgsqlConnection", () => new NpgsqlConnection(PgCs.Replace("Database=k193", "Database=postgres")).WithTestTracking(new NpgsqlTrackingOptions { CurrentTestInfoFetcher = testInfo })),
    })
    {
        string outcome;
        try
        {
            await using var conn = open();
            await conn.OpenAsync();
            var sql = "SELECT 1 AS A, 'x' AS B UNION ALL SELECT 2, 'y'";
            var rows = name.Contains("sync") ? conn.Query<Row>(sql).ToList() : (await conn.QueryAsync<Row>(sql)).ToList();
            outcome = rows.Count == 2 ? "OK" : $"rows={rows.Count}";
            Console.WriteLine($"[dapper] {name}: logged response = {RequestResponseLogger.RequestAndResponseLogs.LastOrDefault(l => l.TestId == "repro-193" && l.Type == RequestResponseType.Response)?.Content ?? "(none)"}");
        }
        catch (Exception ex)
        {
            var e0 = ex; while (e0.InnerException is not null) e0 = e0.InnerException; ex = e0;
            var m = ex.Message.Replace('\n', ' ');
            outcome = $"FAIL {ex.GetType().Name}: {(m.Length > 150 ? m[..150] : m)}";
        }
        results.Add(("dapper", name, "-", outcome));
    }
}

public sealed class Row { public int A { get; set; } public string B { get; set; } = ""; }

public sealed class Order
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public DateOnly Day { get; set; }
    public TimeOnly Time { get; set; }
    public decimal Amount { get; set; }
    public Guid Ref { get; set; }
}

public sealed class Tagged { public int Id { get; set; } public List<int> Tags { get; set; } = []; }
public sealed class Place { public int Id { get; set; } public Point Location { get; set; } = null!; public HierarchyId Path { get; set; } = null!; }

public sealed class Ctx(DbContextOptions<Ctx> options, string provider) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Tagged> Tagged => Set<Tagged>();
    public DbSet<Place> Places => Set<Place>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Order>().Property(o => o.Amount).HasPrecision(18, 2);
        if (provider != "pg") b.Ignore<Tagged>();
        if (!provider.StartsWith("ms")) b.Ignore<Place>();
        else b.Entity<Place>().Property(p => p.Location).HasColumnType("geography");
    }
}
