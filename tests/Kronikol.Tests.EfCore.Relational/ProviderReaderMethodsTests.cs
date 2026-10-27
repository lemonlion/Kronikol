using System.Text.Json;
using Kronikol.Extensions.EfCore.Relational;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Kronikol.Tests.EfCore.Relational;

/// <summary>
/// Whether a context's queries read some value through a provider's own reader method, for which EF Core's shaper casts
/// the reader to the provider's type (plans/SQL_RESPONSE_CAPTURE_PLAN.md F2, fact 14). Building a model needs no server.
/// </summary>
public class ProviderReaderMethodsTests
{
    public sealed class Place
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public Point Location { get; set; } = null!;
    }

    public sealed class Shop
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public DateTimeOffset At { get; set; }
        public DateOnly Day { get; set; }
    }

    public sealed class Basket
    {
        public int Id { get; set; }
        public List<int> Tags { get; set; } = [];
        public JsonDocument? Data { get; set; }
    }

    private sealed class SpatialContext : DbContext
    {
        public DbSet<Place> Places => Set<Place>();

        protected override void OnConfiguring(DbContextOptionsBuilder builder) =>
            builder.UseSqlServer("Server=localhost;Database=unused", sql => sql.UseNetTopologySuite());
    }

    private sealed class SqlServerContext : DbContext
    {
        public DbSet<Shop> Shops => Set<Shop>();

        protected override void OnConfiguring(DbContextOptionsBuilder builder) => builder.UseSqlServer("Server=localhost;Database=unused");
    }

    private sealed class NpgsqlContext : DbContext
    {
        public DbSet<Basket> Baskets => Set<Basket>();

        protected override void OnConfiguring(DbContextOptionsBuilder builder) => builder.UseNpgsql("Host=localhost;Database=unused");
    }

    private sealed class SqliteContext : DbContext
    {
        public DbSet<Shop> Shops => Set<Shop>();

        protected override void OnConfiguring(DbContextOptionsBuilder builder) => builder.UseSqlite("Data Source=:memory:");
    }

    [Fact]
    public void A_SQL_Server_spatial_column_is_read_through_SqlDataReader()
    {
        using var context = new SpatialContext();

        Assert.Equal("Place.Location is read through SqlDataReader.GetSqlBytes", ProviderReaderMethods.FirstUse(context));
    }

    [Fact]
    public void Plain_SQL_Server_columns_are_read_through_DbDataReader()
    {
        using var context = new SqlServerContext();

        Assert.Null(ProviderReaderMethods.FirstUse(context));
    }

    [Fact]
    public void Npgsql_arrays_and_jsonb_are_read_through_DbDataReader()
    {
        using var context = new NpgsqlContext();

        Assert.Null(ProviderReaderMethods.FirstUse(context));
    }

    [Fact]
    public void Sqlite_columns_are_read_through_DbDataReader()
    {
        using var context = new SqliteContext();

        Assert.Null(ProviderReaderMethods.FirstUse(context));
        Assert.True(ProviderReaderMethods.IsKnown(context.Model));
    }
}
