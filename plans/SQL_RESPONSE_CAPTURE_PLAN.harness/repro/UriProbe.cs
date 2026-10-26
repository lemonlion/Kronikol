public static class UriProbe
{
    public static void Run(string pgCs, string sqliteCs)
    {
        Console.WriteLine($"Npgsql DataSource  = {new Npgsql.NpgsqlConnection(pgCs).DataSource}");
        Console.WriteLine($"Sqlite DataSource  = {new Microsoft.Data.Sqlite.SqliteConnection(sqliteCs).DataSource}");
        Console.WriteLine($"SqlClient (localdb)= {new Microsoft.Data.SqlClient.SqlConnection(@"Server=(localdb)\MSSQLLocalDB").DataSource}");
        string[] sources = [@"(localdb)\MSSQLLocalDB", @".\SQLEXPRESS", @"localhost\SQLEXPRESS", @"myhost\inst,1433", "tcp:myserver,1433",
            @"np:\.\pipe\sql\query", "myserver.database.windows.net,1433", "tcp://localhost:5432", "/var/run/postgresql",
            "host1:5432,host2:5432", "spanner-emulator:9010", @"C:\data\app.db", ":memory:", "[::1]:5432"];
        string[] dbs = ["k193", "My Db", "db#1", "main"];
        foreach (var raw in sources)
        {
            var ds = raw.Replace(',', ':');
            var detailed = Try($"sql://{ds}/k193/Orders");
            Console.WriteLine($"{raw,-36} Detailed/Raw: {detailed}");
        }
        foreach (var db in dbs)
            Console.WriteLine($"database '{db}': Summarised sql://{{db}}/Orders: {Try($"sql://{db}/Orders")}, Detailed: {Try($"sql://localhost/{db}/Orders")}");
    }

    static string Try(string s)
    {
        try { var u = new Uri(s); return $"ok host='{u.Host}' path='{u.AbsolutePath}'"; }
        catch (Exception ex) { return $"THROWS {ex.GetType().Name}"; }
    }
}
