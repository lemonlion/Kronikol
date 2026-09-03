using System.Data.Common;
using Kronikol.Extensions.ClickHouse;
using Kronikol.Extensions.ClickHouse.Client;
using Kronikol.Extensions.ClickHouse.Octonica;
using Kronikol.Tests.ClickHouse.Fakes;
using Kronikol.Tracking;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// Both supported clients name their connection type `ClickHouseConnection`; alias via global::
// to avoid clashing with the `Kronikol.Tests.ClickHouse` namespace.
using ClickHouseClientConnection = global::ClickHouse.Client.ADO.ClickHouseConnection;
using OctonicaConnection = global::Octonica.ClickHouseClient.ClickHouseConnection;

namespace Kronikol.Tests.ClickHouse;

public class ClickHouseConnectionExtensionsTests
{
    [Fact]
    public void WithClickHouseTestTracking_wraps_ClickHouseClient_connection()
    {
        using var inner = new ClickHouseClientConnection();
        using var tracked = inner.WithClickHouseTestTracking();

        Assert.IsType<TrackingClickHouseConnection>(tracked);
        Assert.Same(inner, tracked.InnerConnection);
    }

    [Fact]
    public void WithClickHouseTestTracking_wraps_Octonica_connection()
    {
        using var inner = new OctonicaConnection();
        using var tracked = inner.WithClickHouseTestTracking();

        Assert.IsType<TrackingClickHouseConnection>(tracked);
        Assert.Same(inner, tracked.InnerConnection);
    }

    [Fact]
    public void WithClickHouseTestTracking_uses_provided_options()
    {
        using var inner = new ClickHouseClientConnection();
        using var tracked = inner.WithClickHouseTestTracking(new ClickHouseTrackingOptions { ServiceName = "Warehouse" });

        Assert.Contains("Warehouse", tracked.ComponentName);
    }
}

public class ClickHouseConnectionDetectionTests
{
    [Fact]
    public void IsClickHouseConnection_true_for_ClickHouseClient()
    {
        using var conn = new ClickHouseClientConnection();
        Assert.True(Kronikol.Extensions.ClickHouse.ClickHouseServiceCollectionExtensions.IsClickHouseConnection(conn));
    }

    [Fact]
    public void IsClickHouseConnection_true_for_Octonica()
    {
        using var conn = new OctonicaConnection();
        Assert.True(Kronikol.Extensions.ClickHouse.ClickHouseServiceCollectionExtensions.IsClickHouseConnection(conn));
    }

    [Fact]
    public void IsClickHouseConnection_false_for_non_clickhouse()
    {
        using var conn = new FakeDbConnection();
        Assert.False(Kronikol.Extensions.ClickHouse.ClickHouseServiceCollectionExtensions.IsClickHouseConnection(conn));
    }
}

public class ClickHouseServiceCollectionExtensionsTests
{
    [Fact]
    public void AddClickHouseTestTracking_decorates_ClickHouseClient_connection()
    {
        var services = new ServiceCollection();
        services.AddTransient<DbConnection>(_ => new ClickHouseClientConnection());
        services.AddClickHouseTestTracking();

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<DbConnection>();

        Assert.IsType<TrackingClickHouseConnection>(resolved);
    }

    [Fact]
    public void AddClickHouseTestTracking_decorates_Octonica_connection()
    {
        var services = new ServiceCollection();
        services.AddTransient<DbConnection>(_ => new OctonicaConnection());
        services.AddClickHouseTestTracking();

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<DbConnection>();

        Assert.IsType<TrackingClickHouseConnection>(resolved);
    }

    [Fact]
    public void AddClickHouseTestTracking_leaves_non_clickhouse_connection_untouched()
    {
        var services = new ServiceCollection();
        services.AddTransient<DbConnection, FakeDbConnection>();
        services.AddClickHouseTestTracking();

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<DbConnection>();

        Assert.IsType<FakeDbConnection>(resolved);
    }

    [Fact]
    public void AddClickHouseTestTracking_applies_configuration()
    {
        var services = new ServiceCollection();
        services.AddTransient<DbConnection>(_ => new ClickHouseClientConnection());
        services.AddClickHouseTestTracking(o => o.ServiceName = "CustomCH");

        using var provider = services.BuildServiceProvider();
        var resolved = (TrackingClickHouseConnection)provider.GetRequiredService<DbConnection>();

        Assert.Contains("CustomCH", resolved.ComponentName);
    }
}

public class ClickHouseClientDriverAdapterTests
{
    [Fact]
    public void Resolve_prefers_positive_WrittenRows_over_driver_result()
    {
        // The driver's return value is a body-parsing artifact (0 for INSERT); the
        // X-ClickHouse-Summary stats are authoritative.
        var stats = new global::ClickHouse.Client.ADO.QueryStats(0, 0, WrittenRows: 3, 0, 0, 0, 0, 0);
        Assert.Equal(3, ClickHouseClientDriverAdapter.Resolve(stats, driverResult: 0));
        Assert.Equal(3, ClickHouseClientDriverAdapter.Resolve(stats, driverResult: 7));
    }

    [Fact]
    public void Resolve_falls_back_to_driver_result_when_stats_missing_or_zero()
    {
        Assert.Equal(5, ClickHouseClientDriverAdapter.Resolve(null, driverResult: 5));
        var zeroStats = new global::ClickHouse.Client.ADO.QueryStats(0, 0, WrittenRows: 0, 0, 0, 0, 0, 0);
        Assert.Equal(0, ClickHouseClientDriverAdapter.Resolve(zeroStats, driverResult: 0));
    }

    [Fact]
    public void Resolve_clamps_counts_beyond_int_range()
    {
        var hugeStats = new global::ClickHouse.Client.ADO.QueryStats(0, 0, WrittenRows: long.MaxValue, 0, 0, 0, 0, 0);
        Assert.Equal(int.MaxValue, ClickHouseClientDriverAdapter.Resolve(hugeStats, driverResult: 0));
    }

    [Fact]
    public void Unexecuted_ClickHouseClient_command_resolves_to_driver_result()
    {
        // Before any execution QueryStats is null; the adapter must take the fallback path
        // against the real driver type, not throw.
        using var conn = new ClickHouseClientConnection();
        using var cmd = conn.CreateCommand();
        Assert.Equal(9, ClickHouseClientDriverAdapter.Instance.ResolveRowsAffected(cmd, 9));
    }

    [Fact]
    public void Non_ClickHouseClient_command_resolves_to_driver_result()
    {
        Assert.Equal(4, ClickHouseClientDriverAdapter.Instance.ResolveRowsAffected(new FakeDbCommand(), 4));
    }
}

public class DriverPairingExtensionTests
{
    [Fact]
    public void WithClickHouseClientTestTracking_pairs_the_ClickHouseClient_adapter()
    {
        using var inner = new ClickHouseClientConnection();
        var options = new ClickHouseTrackingOptions();
        using var tracked = inner.WithClickHouseClientTestTracking(options);

        Assert.Same(ClickHouseClientDriverAdapter.Instance, options.DriverAdapter);
        Assert.IsType<TrackingClickHouseConnection>(tracked);
    }

    [Fact]
    public void WithOctonicaClickHouseTestTracking_pairs_the_Octonica_adapter()
    {
        using var inner = new OctonicaConnection();
        var options = new ClickHouseTrackingOptions();
        using var tracked = inner.WithOctonicaClickHouseTestTracking(options);

        Assert.Same(OctonicaClickHouseDriverAdapter.Instance, options.DriverAdapter);
        Assert.IsType<TrackingClickHouseConnection>(tracked);
    }

    [Fact]
    public void Pairing_respects_an_adapter_already_set_on_options()
    {
        using var inner = new ClickHouseClientConnection();
        var custom = new Fakes.FakeDriverAdapter(1);
        var options = new ClickHouseTrackingOptions { DriverAdapter = custom };
        using var tracked = inner.WithClickHouseClientTestTracking(options);

        Assert.Same(custom, options.DriverAdapter);
    }

    [Fact]
    public void AddClickHouseClientTestTracking_decorates_and_pairs()
    {
        var services = new ServiceCollection();
        services.AddTransient<DbConnection>(_ => new ClickHouseClientConnection());
        services.AddClickHouseClientTestTracking();

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<DbConnection>();

        Assert.IsType<TrackingClickHouseConnection>(resolved);
    }

    [Fact]
    public void AddOctonicaClickHouseTestTracking_decorates_and_pairs()
    {
        var services = new ServiceCollection();
        services.AddTransient<DbConnection>(_ => new OctonicaConnection());
        services.AddOctonicaClickHouseTestTracking();

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<DbConnection>();

        Assert.IsType<TrackingClickHouseConnection>(resolved);
    }

    [Fact]
    public void Octonica_adapter_passes_driver_result_through()
    {
        Assert.Equal(3, OctonicaClickHouseDriverAdapter.Instance.ResolveRowsAffected(new FakeDbCommand(), 3));
    }
}

public class TrackingClickHouseTransactionTests : IDisposable
{
    private readonly string _testId = Guid.NewGuid().ToString();
    private readonly FakeDbConnection _fakeConnection = new();
    private readonly TrackingClickHouseConnection _trackingConnection;

    private RequestResponseLog[] GetLogsForTest()
        => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    public TrackingClickHouseTransactionTests()
    {
        TrackingComponentRegistry.Clear();
        var options = new ClickHouseTrackingOptions { CurrentTestInfoFetcher = () => ("TestMethod", _testId) };
        _trackingConnection = new TrackingClickHouseConnection(_fakeConnection, options);
    }

    public void Dispose()
    {
        _trackingConnection.Dispose();
        TrackingComponentRegistry.Clear();
    }

    [Fact]
    public void BeginTransaction_logs_begin()
    {
        using var tx = _trackingConnection.BeginTransaction();
        Assert.Equal(2, GetLogsForTest().Length); // BEGIN request + response
    }

    [Fact]
    public void Commit_logs_commit()
    {
        using var tx = _trackingConnection.BeginTransaction();
        tx.Commit();

        // BEGIN (2) + COMMIT (2)
        Assert.Equal(4, GetLogsForTest().Length);
    }

    [Fact]
    public void Rollback_logs_rollback()
    {
        using var tx = _trackingConnection.BeginTransaction();
        tx.Rollback();

        Assert.Equal(4, GetLogsForTest().Length);
    }
}
