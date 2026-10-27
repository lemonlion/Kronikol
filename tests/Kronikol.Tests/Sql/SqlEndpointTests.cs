using Kronikol.Sql;

namespace Kronikol.Tests.Sql;

/// <summary>
/// The host a <c>sql://</c> arrow is drawn against, made from whatever a connection reports as its data source. Until
/// 5.10.1 the trackers put the data source into <c>new Uri(...)</c> as it was, which threw for LocalDB, every SQL Server
/// named instance, a protocol prefix and an Npgsql host list, so every command on such a connection failed (#193 plan, F6).
/// </summary>
public class SqlEndpointTests
{
    [Theory]
    [InlineData("localhost,1433", "localhost:1433")]
    [InlineData("127.0.0.1,1433", "127.0.0.1:1433")]
    [InlineData("myserver.database.windows.net,1433", "myserver.database.windows.net:1433")]
    [InlineData(@"(localdb)\MSSQLLocalDB", "localdb-mssqllocaldb")]
    [InlineData(@".\SQLEXPRESS", "localhost-sqlexpress")]
    [InlineData(@"(local)\SQLEXPRESS", "localhost-sqlexpress")]
    [InlineData(@"localhost\SQLEXPRESS", "localhost-sqlexpress")]
    [InlineData(@"myhost\inst,1433", "myhost-inst:1433")]
    [InlineData("tcp:myserver,1433", "myserver:1433")]
    [InlineData(@"np:\\.\pipe\sql\query", "localhost")]
    [InlineData(@"\\.\pipe\MSSQL$SQLEXPRESS\sql\query", "localhost")]
    [InlineData("tcp://localhost:55432", "localhost:55432")]
    [InlineData("localhost:5432", "localhost:5432")]
    [InlineData("host1:5432,host2:5432", "host1:5432")]
    [InlineData("host1,host2", "host1")]
    [InlineData("spanner-emulator:9010", "spanner-emulator:9010")]
    [InlineData("/var/run/postgresql", "localhost")]
    [InlineData("unix:/var/run/postgresql/.s.PGSQL.5432", "localhost")]
    [InlineData(@"C:\data\app.db", "localhost")]
    [InlineData("app.db", "app.db")]
    [InlineData(":memory:", "localhost")]
    [InlineData("[::1]:5432", "[::1]:5432")]
    [InlineData("[::1]", "[::1]")]
    [InlineData("", "localhost")]
    [InlineData(null, "localhost")]
    public void A_data_source_becomes_the_host_drawn(string? dataSource, string expected)
    {
        var host = SqlEndpoint.Host(dataSource);

        Assert.Equal(expected, new Uri($"sql://{host}/db").Authority);
    }

    [Theory]
    [InlineData("k193", "k193")]
    [InlineData("My Db", "my-db")]
    [InlineData("db#1", "db-1")]
    [InlineData("main", "main")]
    public void A_database_name_used_as_the_host_at_Summarised_is_a_valid_host(string database, string expected)
    {
        Assert.Equal(expected, new Uri($"sql://{SqlEndpoint.Host(database)}/Orders").Authority);
    }

    [Theory]
    [InlineData("k193", "k193")]
    [InlineData("My Db", "My%20Db")]
    [InlineData("db#1", "db%231")]
    [InlineData("", "unknown")]
    [InlineData(null, "unknown")]
    public void A_name_in_the_path_is_escaped(string? name, string expected)
    {
        Assert.Equal(expected, SqlEndpoint.Segment(name));
    }

    [Theory]
    [InlineData("sql://localhost:1433/k193/Orders")]
    [InlineData("sql://localdb-mssqllocaldb/k193")]
    public void An_address_that_parses_is_kept(string text)
    {
        Assert.Equal(text, SqlEndpoint.Build(text).ToString());
    }

    [Theory]
    [InlineData("sql://(bad host)/k193")]
    [InlineData("sql://a b/c")]
    [InlineData("not a uri at all")]
    [InlineData("")]
    public void An_address_that_does_not_parse_falls_back_and_never_throws(string text)
    {
        Assert.Equal("sql://localhost/", SqlEndpoint.Build(text).ToString());
        Assert.Equal("pg://localhost/", SqlEndpoint.Build(text, "pg").ToString());
    }

    [Fact]
    public void No_data_source_makes_the_host_throw_or_the_address_fail()
    {
        string?[] awkward =
        [
            "(", ")", @"\", @"\\", "::", "[", "]", "[::1", "a,b,c", ",", ",1433", "tcp:", "np:", "tcp://", " ", "\t",
            "a b", "é.example", "host:notaport", "host:99999", @"(localdb)\", @"\inst", "..", ".", "-", "a..b",
        ];
        foreach (var source in awkward)
        {
            var host = SqlEndpoint.Host(source);
            Assert.True(Uri.TryCreate($"sql://{host}/db", UriKind.Absolute, out _), $"'{source}' gave host '{host}'");
        }
    }
}
