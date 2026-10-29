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

    // Oracle names its server and the service it reaches in one data source, and ODP.NET's Database is empty, so until
    // F26 (plans/CLIENT_FLOORS_PLAN.md) every Oracle arrow was drawn against localhost and a database called unknown.
    [Theory]
    [InlineData("127.0.0.1:21521/FREEPDB1", "", "127.0.0.1:21521", "FREEPDB1")]
    [InlineData("localhost/XE", "", "localhost", "XE")]
    [InlineData("localhost/XE", null, "localhost", "XE")]
    [InlineData("//db.example.com:1521/orcl.example.com", "", "db.example.com:1521", "orcl.example.com")]
    [InlineData("db.example.com:1521/sales:dedicated/inst1", "", "db.example.com:1521", "sales")]
    [InlineData("db.example.com:1521/sales?connect_timeout=5", "", "db.example.com:1521", "sales")]
    [InlineData("tcp://db.example.com:1521/sales", "", "db.example.com:1521", "sales")]
    [InlineData("(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=dbhost)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=orcl)))", "", "dbhost:1521", "orcl")]
    [InlineData("(DESCRIPTION = (ADDRESS_LIST = (ADDRESS = (PROTOCOL = TCP)(HOST = h1)(PORT = 1522)))(CONNECT_DATA = (SID = ORCL)))", "", "h1:1522", "ORCL")]
    [InlineData("(description=(address=(protocol=tcp)(host=10.0.0.5)(port=1521))(connect_data=(service_name=pdb1)))", "", "10.0.0.5:1521", "pdb1")]
    [InlineData("127.0.0.1:21521/FREEPDB1", "SALES", "127.0.0.1:21521", "SALES")]
    [InlineData("(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=dbhost)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=orcl)))", "SALES", "dbhost:1521", "SALES")]
    public void An_Oracle_data_source_names_its_server_and_the_service_it_reaches(string dataSource, string? database, string expectedHost, string expectedDatabase)
    {
        var (host, named) = SqlEndpoint.Address(dataSource, database);

        Assert.Equal(expectedHost, new Uri($"sql://{host}/db").Authority);
        Assert.Equal(expectedDatabase, named);
    }

    // What every other provider reports reads as it did: a relative SQLite path names a file, not a server and a service,
    // as its connection names a database (main); a TNS alias names neither a host nor a service it can be read from.
    [Theory]
    [InlineData("data/app.db", "main", "data-app.db", "main")]
    [InlineData("localhost:5432", "orders", "localhost:5432", "orders")]
    [InlineData("localhost,1433", "k193", "localhost:1433", "k193")]
    [InlineData(@"(localdb)\MSSQLLocalDB", "k193", "localdb-mssqllocaldb", "k193")]
    [InlineData("ORCL", "", "orcl", null)]
    [InlineData("", "", "localhost", null)]
    [InlineData(null, null, "localhost", null)]
    [InlineData("(DESCRIPTION=(CONNECT_DATA=(SERVICE_NAME=orcl)))", "", "localhost", "orcl")]
    [InlineData("(DESCRIPTION=", "", "localhost", null)]
    public void Any_other_data_source_reads_as_before(string? dataSource, string? database, string expectedHost, string? expectedDatabase)
    {
        var (host, named) = SqlEndpoint.Address(dataSource, database);

        Assert.Equal(expectedHost, new Uri($"sql://{host}/db").Authority);
        Assert.Equal(expectedDatabase, string.IsNullOrEmpty(named) ? null : named);
    }

    [Fact]
    public void No_data_source_makes_the_address_throw_or_fail()
    {
        string?[] awkward =
        [
            "(DESCRIPTION=(HOST=a b)(PORT=1521))", "(DESCRIPTION=(HOST=(x))", "(DESCRIPTION=(HOST=h)(PORT=99999))",
            "(DESCRIPTION=(HOST=::1)(PORT=1521))", "(DESCRIPTION=(HOST=é.example))", "(description_list=(host=h))",
            "//", "/x", "h:1/", "h:/x", "[::1]:1521/svc", "[bad]:1/x", "[::1/x", "a b:1521/svc", "tcps://h:2484/s",
            "h:1521/", "h:1521//", "?x/y", "h/:", ":1521/svc", null, "",
        ];
        foreach (var source in awkward)
        {
            foreach (var database in new[] { null, "", "db" })
            {
                var (host, _) = SqlEndpoint.Address(source, database);
                Assert.True(Uri.TryCreate($"sql://{host}/db", UriKind.Absolute, out _), $"'{source}' gave host '{host}'");
            }
        }
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
