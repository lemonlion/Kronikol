using DotNet.Testcontainers.Builders;
using Microsoft.Data.SqlClient;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace Kronikol.Tests.EfCore.Relational;

/// <summary>
/// A real PostgreSQL server, for what only Npgsql's EF Core provider does: its update pipeline casts the reader of every
/// SaveChanges batch to <c>NpgsqlDataReader</c> (#193). The server a connection string in
/// <see cref="EnvironmentVariable"/> names when it is set, otherwise one Testcontainers starts when a Docker endpoint
/// answers (CI's Linux runners). Without either the facts that need it skip, saying why.
/// </summary>
public sealed class PostgresServerFixture : IAsyncLifetime
{
    public const string EnvironmentVariable = "KRONIKOL_TEST_POSTGRES";
    public const string Image = "postgres:17-alpine";

    private PostgreSqlContainer? _container;
    private string? _server;
    private string? _unavailableReason;

    public async ValueTask InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            _server = configured;
            return;
        }

        // Testcontainers says when no Docker endpoint answers; any other failure to start the server fails the facts.
        try
        {
            _container = new PostgreSqlBuilder(Image).Build();
            await _container.StartAsync();
        }
        catch (DockerUnavailableException ex)
        {
            _unavailableReason = $"Docker is not available ({ex.Message.Split('\n')[0].Trim()}) and {EnvironmentVariable} names no PostgreSQL server.";
            _container = null;
            return;
        }
        _server = _container.GetConnectionString();
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    /// <summary>A connection string to <paramref name="database"/> on the server; skips the calling fact when there is no server.</summary>
    public string Require(string database)
    {
        Assert.SkipWhen(_server is null, _unavailableReason ?? "");
        return new NpgsqlConnectionStringBuilder(_server) { Database = database }.ConnectionString;
    }
}

/// <summary>
/// A real SQL Server, for what only its EF Core provider does: a spatial column is read through
/// <c>SqlDataReader.GetSqlBytes</c>, so EF Core's shaper casts the reader to <c>SqlDataReader</c>. The server a
/// connection string in <see cref="EnvironmentVariable"/> names when it is set (on Windows it can name LocalDB, whose
/// data source <c>(localdb)\MSSQLLocalDB</c> is no host), otherwise one Testcontainers starts when a Docker endpoint
/// answers. Without either the facts that need it skip, saying why.
/// </summary>
public sealed class SqlServerServerFixture : IAsyncLifetime
{
    public const string EnvironmentVariable = "KRONIKOL_TEST_SQLSERVER";
    public const string Image = "mcr.microsoft.com/mssql/server:2022-CU16-ubuntu-22.04";

    private MsSqlContainer? _container;
    private string? _server;
    private string? _unavailableReason;

    public async ValueTask InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            _server = configured;
            return;
        }

        try
        {
            _container = new MsSqlBuilder(Image).Build();
            await _container.StartAsync();
        }
        catch (DockerUnavailableException ex)
        {
            _unavailableReason = $"Docker is not available ({ex.Message.Split('\n')[0].Trim()}) and {EnvironmentVariable} names no SQL Server.";
            _container = null;
            return;
        }
        _server = _container.GetConnectionString();
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    /// <summary>A connection string to <paramref name="database"/> on the server; skips the calling fact when there is no server.</summary>
    public string Require(string database)
    {
        Assert.SkipWhen(_server is null, _unavailableReason ?? "");
        return new SqlConnectionStringBuilder(_server) { InitialCatalog = database, TrustServerCertificate = true }.ConnectionString;
    }
}
