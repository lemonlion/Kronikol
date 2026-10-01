using System.Data.Common;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO;
using Kronikol.Extensions.ClickHouse;
using Kronikol.Extensions.ClickHouse.Driver;
using Kronikol.Tests.ClickHouse.Fakes;
using Kronikol.Tracking;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kronikol.Tests.ClickHouse;

/// <summary>A data source whose client is the fake, to see what the decorator hands out.</summary>
public sealed class FakeClickHouseDataSource : IClickHouseDataSource
{
    public FakeClickHouseClient Client { get; } = new();
    public int GetClientCalls { get; private set; }

    public string ConnectionString => "Host=ch-host";
    public IClickHouseConnection CreateConnection() => new ClickHouseConnection(ConnectionString);
    public IClickHouseConnection OpenConnection() => throw new NotSupportedException();
    public Task<IClickHouseConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public IClickHouseClient GetClient()
    {
        GetClientCalls++;
        return Client;
    }
}

public class TrackingClickHouseDataSourceTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private ClickHouseTrackingOptions Options() => new() { CurrentTestInfoFetcher = () => ("TestMethod", _testId) };

    private RequestResponseLog[] Logs()
        => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    [Fact]
    public void GetClient_returns_one_tracked_client_around_the_data_sources_own()
    {
        var inner = new FakeClickHouseDataSource();
        var dataSource = new TrackingClickHouseDataSource(inner, Options());

        var first = dataSource.GetClient();
        var second = dataSource.GetClient();

        var tracked = Assert.IsType<TrackingClickHouseClient>(first);
        Assert.Same(first, second);
        Assert.Same(inner.Client, tracked.InnerClient);
        Assert.Equal(1, inner.GetClientCalls);
    }

    [Fact]
    public async Task A_call_through_the_data_sources_client_is_recorded()
    {
        var dataSource = new TrackingClickHouseDataSource(new FakeClickHouseDataSource(), Options());

        await dataSource.GetClient().ExecuteNonQueryAsync("DELETE FROM orders WHERE id = 1");

        Assert.Equal(2, Logs().Length);
    }

    // Plan F2: the driver's connection hands out its own concrete commands, so a data source's connections cannot be
    // handed back tracked. They are forwarded as they are.
    [Fact]
    public void Connections_and_the_connection_string_are_forwarded_untracked()
    {
        var inner = new FakeClickHouseDataSource();
        var dataSource = new TrackingClickHouseDataSource(inner, Options());

        Assert.IsType<ClickHouseConnection>(dataSource.CreateConnection());
        Assert.Equal(inner.ConnectionString, dataSource.ConnectionString);
        Assert.Same(inner, dataSource.InnerDataSource);
    }

    // ─── AddClickHouseDriverTestTracking ────────────────────────

    // This project references ClickHouse.Client too, whose AddClickHouseDataSource has the driver's name, namespace and
    // signature, so the driver's is called through its own assembly.
    private static void AddDriverDataSource(IServiceCollection services, string connectionString, object? serviceKey = null)
    {
        var extensions = typeof(ClickHouseDataSource).Assembly
            .GetType("Microsoft.Extensions.DependencyInjection.ClickHouseServiceCollectionExtensions", throwOnError: true)!;
        var method = extensions.GetMethods().Single(m => m.Name == "AddClickHouseDataSource"
            && m.GetParameters().Select(p => p.ParameterType).Take(3).SequenceEqual([typeof(IServiceCollection), typeof(string), typeof(HttpClient)]));
        method.Invoke(null, [services, connectionString, null, ServiceLifetime.Transient, ServiceLifetime.Singleton, serviceKey]);
    }

    [Fact]
    public async Task AddClickHouseDriverTestTracking_decorates_an_IClickHouseClient_registration_with_its_options()
    {
        var services = new ServiceCollection();
        var fake = new FakeClickHouseClient();
        services.AddSingleton<IClickHouseClient>(fake);
        services.AddClickHouseDriverTestTracking(o => o.CurrentTestInfoFetcher = () => ("TestMethod", _testId));

        using var provider = services.BuildServiceProvider();
        var client = Assert.IsType<TrackingClickHouseClient>(provider.GetRequiredService<IClickHouseClient>());
        await client.ExecuteNonQueryAsync("DELETE FROM orders WHERE id = 1");

        Assert.Same(fake, client.InnerClient);
        Assert.Equal(2, Logs().Length);
    }

    [Fact]
    public void AddClickHouseDriverTestTracking_decorates_the_drivers_data_source_and_its_connections()
    {
        var services = new ServiceCollection();
        AddDriverDataSource(services, "Host=localhost;Port=8123");
        services.AddClickHouseDriverTestTracking();

        using var provider = services.BuildServiceProvider();
        var dataSource = Assert.IsType<TrackingClickHouseDataSource>(provider.GetRequiredService<IClickHouseDataSource>());

        Assert.IsType<TrackingClickHouseClient>(dataSource.GetClient());
        Assert.Same(dataSource.GetClient(), dataSource.GetClient());
        Assert.IsType<TrackingClickHouseConnection>(provider.GetRequiredService<DbConnection>());
    }

    // The driver registers a keyed data source's services keyed; a keyed descriptor used to make the decoration throw.
    [Fact]
    public void AddClickHouseDriverTestTracking_decorates_a_keyed_data_source_under_its_key()
    {
        var services = new ServiceCollection();
        AddDriverDataSource(services, "Host=localhost;Port=8123", serviceKey: "analytics");
        services.AddClickHouseDriverTestTracking();

        using var provider = services.BuildServiceProvider();

        var dataSource = provider.GetRequiredKeyedService<IClickHouseDataSource>("analytics");
        Assert.IsType<TrackingClickHouseClient>(Assert.IsType<TrackingClickHouseDataSource>(dataSource).GetClient());
        Assert.IsType<TrackingClickHouseConnection>(provider.GetRequiredKeyedService<DbConnection>("analytics"));
        Assert.Null(provider.GetService<IClickHouseDataSource>());
    }

    // Plan F1: the concrete client is sealed, so a registration of it cannot be decorated; it is left as it is.
    [Fact]
    public void A_concrete_ClickHouseClient_registration_is_left_alone()
    {
        var services = new ServiceCollection();
        using var concrete = new ClickHouseClient("Host=localhost;Port=8123");
        services.AddSingleton(concrete);
        services.AddClickHouseDriverTestTracking();

        using var provider = services.BuildServiceProvider();

        Assert.Same(concrete, provider.GetRequiredService<ClickHouseClient>());
    }
}
