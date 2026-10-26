// Live test without a database: EF Core 10.0.14 SQL Server + NetTopologySuite + HierarchyId.
// A connection interceptor suppresses the open; a command interceptor supplies the rows from a fake
// DbDataReader (ReaderExecuting) and wraps the result in a pure decorator (ReaderExecuted), the same
// substitution Kronikol makes. Prints each mapping's GetDataReaderMethod() and what each query does.
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NetTopologySuite.Geometries;

var placeSql = "";
Func<string[], object?[]> rowFor = _ => [];

var connectionInterceptor = new SuppressOpen();
var commandInterceptor = new FakeRows(text =>
{
    placeSql = text;
    var cols = Regex.Matches(text.Split("FROM")[0], @"\[p\]\.\[(\w+)\]").Select(m => m.Groups[1].Value).ToArray();
    var types = cols.Select(c => c switch { "Id" => typeof(int), _ => typeof(byte[]) }).ToArray();
    return new FakeReader(cols, types, rowFor(cols));
});

using (var ctx = new Ctx(connectionInterceptor, commandInterceptor))
{
    var et = ctx.Model.FindEntityType(typeof(Place))!;
    foreach (var name in new[] { "Location", "Node" })
    {
        var m = et.FindProperty(name)!.GetRelationalTypeMapping();
        var rm = m.GetDataReaderMethod();
        Console.WriteLine($"{name}: mapping {m.GetType().Name} store type '{m.StoreType}' -> GetDataReaderMethod() = {rm.DeclaringType!.FullName}.{rm.Name}({string.Join(",", rm.GetParameters().Select(p => p.ParameterType.Name))}){(rm.IsGenericMethod ? "<" + string.Join(",", rm.GetGenericArguments().Select(a => a.Name)) + ">" : "")}");
    }
}

Run("geography NOT NULL, hierarchyid NULL", cols => cols.Select(c => c switch { "Id" => (object?)1, "Location" => new byte[] { 0xE6, 0x10, 0, 0, 1, 0x0C, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, _ => null }).ToArray());
Run("geography NULL, hierarchyid NULL", cols => cols.Select(c => c switch { "Id" => (object?)1, _ => null }).ToArray());
Run("geography NULL, hierarchyid NOT NULL ('/1/' as SqlHierarchyId, what SqlDataReader.GetFieldValue<SqlHierarchyId> returns)",
    cols => cols.Select(c => c switch { "Id" => (object?)1, "Node" => Microsoft.SqlServer.Types.SqlHierarchyId.Parse(new System.Data.SqlTypes.SqlString("/1/")), _ => null }).ToArray());

Console.WriteLine();
Console.WriteLine("SQL: " + placeSql.Replace("\n", " ").Replace("\r", ""));

void Run(string title, Func<string[], object?[]> row)
{
    rowFor = row;
    FakeReader.Log.Clear();
    Console.WriteLine();
    Console.WriteLine($"=== {title}");
    foreach (var async in new[] { false, true })
    {
        try
        {
            using var ctx = new Ctx(connectionInterceptor, commandInterceptor);
            var list = async ? ctx.Places.AsNoTracking().ToListAsync().GetAwaiter().GetResult() : ctx.Places.AsNoTracking().ToList();
            var p = list.Single();
            Console.WriteLine($"  {(async ? "async" : "sync ")}: OK  Location={(p.Location == null ? "null" : p.Location.GeometryType)} Node={(p.Node == null ? "null" : p.Node.ToString())}");
        }
        catch (Exception ex)
        {
            var root = ex;
            while (root.InnerException != null) root = root.InnerException;
            var frame = (root.StackTrace ?? "").Split('\n').FirstOrDefault()?.Trim();
            Console.WriteLine($"  {(async ? "async" : "sync ")}: {ex.GetType().Name}{(root != ex ? " -> " + root.GetType().Name : "")}: {root.Message}");
            Console.WriteLine($"         first frame: {frame}");
        }
        Console.WriteLine($"         reader calls: {string.Join(", ", FakeReader.Log.Distinct())}");
        FakeReader.Log.Clear();
    }
}

public class Place
{
    public int Id { get; set; }
    public Geometry? Location { get; set; }
    public HierarchyId? Node { get; set; }
}

public class Ctx(params IInterceptor[] interceptors) : DbContext
{
    public DbSet<Place> Places => Set<Place>();

    protected override void OnConfiguring(DbContextOptionsBuilder o)
        => o.UseSqlServer("Server=nonexistent.invalid;Database=x;Integrated Security=true;TrustServerCertificate=true",
                s => { s.UseNetTopologySuite(); s.UseHierarchyId(); })
            .AddInterceptors(interceptors);

    protected override void OnModelCreating(ModelBuilder b)
        => b.Entity<Place>().Property(p => p.Location).HasColumnType("geography");
}

public sealed class SuppressOpen : DbConnectionInterceptor
{
    public override InterceptionResult ConnectionOpening(DbConnection c, ConnectionEventData e, InterceptionResult r) => InterceptionResult.Suppress();
    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection c, ConnectionEventData e, InterceptionResult r, CancellationToken ct = default)
        => ValueTask.FromResult(InterceptionResult.Suppress());
    public override InterceptionResult ConnectionClosing(DbConnection c, ConnectionEventData e, InterceptionResult r) => InterceptionResult.Suppress();
    public override ValueTask<InterceptionResult> ConnectionClosingAsync(DbConnection c, ConnectionEventData e, InterceptionResult r) => ValueTask.FromResult(InterceptionResult.Suppress());
}

public sealed class FakeRows(Func<string, DbDataReader> make) : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand cmd, CommandEventData e, InterceptionResult<DbDataReader> r)
        => InterceptionResult<DbDataReader>.SuppressWithResult(make(cmd.CommandText));
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand cmd, CommandEventData e, InterceptionResult<DbDataReader> r, CancellationToken ct = default)
        => ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(make(cmd.CommandText)));
    public override DbDataReader ReaderExecuted(DbCommand cmd, CommandExecutedEventData e, DbDataReader result) => new WrappingReader(result);
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand cmd, CommandExecutedEventData e, DbDataReader result, CancellationToken ct = default)
        => ValueTask.FromResult<DbDataReader>(new WrappingReader(result));
}
