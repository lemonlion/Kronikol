using System.Data.Common;
using ClickHouse.Driver;
using Microsoft.AspNetCore.Http;

namespace Kronikol.Extensions.ClickHouse.Driver;

/// <summary>
/// Decorator for ClickHouse.Driver's <see cref="IClickHouseDataSource"/> whose <see cref="GetClient"/> returns a
/// <see cref="TrackingClickHouseClient"/>: one per data source, around the data source's own client, made on first use.
/// <para>
/// Its connections are not tracked. The driver's connection hands out its own concrete commands, so no wrapper can
/// stand in for one. Code that wants tracked connections takes a <see cref="DbConnection"/>, which
/// <see cref="ClickHouseDriverTrackingExtensions.AddClickHouseDriverTestTracking"/> decorates, or wraps one with
/// <c>WithClickHouseDriverTestTracking()</c>.
/// </para>
/// </summary>
public sealed class TrackingClickHouseDataSource : IClickHouseDataSource
{
    private readonly IClickHouseDataSource _inner;
    private readonly Lazy<IClickHouseClient> _client;

    public TrackingClickHouseDataSource(IClickHouseDataSource inner, ClickHouseTrackingOptions? options = null, IHttpContextAccessor? httpContextAccessor = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        var opts = options ?? new ClickHouseTrackingOptions();
        _client = new Lazy<IClickHouseClient>(() => _inner.GetClient() switch
        {
            TrackingClickHouseClient tracked => tracked,
            var client => new TrackingClickHouseClient(client, opts, httpContextAccessor),
        });
    }

    /// <summary>The data source this one forwards to.</summary>
    public IClickHouseDataSource InnerDataSource => _inner;

    public string ConnectionString => _inner.ConnectionString;

    public IClickHouseClient GetClient() => _client.Value;

    public IClickHouseConnection CreateConnection() => _inner.CreateConnection();

    public IClickHouseConnection OpenConnection() => _inner.OpenConnection();

    public Task<IClickHouseConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        => _inner.OpenConnectionAsync(cancellationToken);
}
