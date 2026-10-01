using DotNet.Testcontainers.Builders;
using Testcontainers.ClickHouse;
using Xunit;

namespace Kronikol.Tests.ClickHouse;

/// <summary>
/// A real ClickHouse server, for the driver behaviour no fake can stand in for: the server a connection string in
/// <see cref="EnvironmentVariable"/> names when it is set, otherwise one Testcontainers starts when a Docker endpoint
/// answers (CI's Linux runners). Without either the facts that need it skip, saying why.
/// </summary>
public sealed class ClickHouseServerFixture : IAsyncLifetime
{
    public const string EnvironmentVariable = "KRONIKOL_TEST_CLICKHOUSE";
    public const string Image = "clickhouse/clickhouse-server:25.8-alpine";

    private ClickHouseContainer? _container;

    public string? ConnectionString { get; private set; }
    public string? UnavailableReason { get; private set; }

    public async ValueTask InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            ConnectionString = configured;
            return;
        }

        // Testcontainers says when no Docker endpoint answers; any other failure to start the server fails the facts.
        try
        {
            _container = new ClickHouseBuilder().WithImage(Image).Build();
            await _container.StartAsync();
        }
        catch (DockerUnavailableException ex)
        {
            UnavailableReason = $"Docker is not available ({ex.Message.Split('\n')[0].Trim()}) and {EnvironmentVariable} names no ClickHouse server.";
            _container = null;
            return;
        }
        ConnectionString = _container.GetConnectionString();
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    /// <summary>The server's connection string; skips the calling fact when there is no server.</summary>
    public string Require()
    {
        Assert.SkipWhen(ConnectionString is null, UnavailableReason ?? "");
        return ConnectionString!;
    }
}
