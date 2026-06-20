using System.Data.Common;
using Kronikol.Extensions.ClickHouse;
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
