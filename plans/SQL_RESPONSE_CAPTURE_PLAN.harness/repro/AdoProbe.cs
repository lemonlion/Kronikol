using Kronikol.Extensions.Npgsql;
using Kronikol.Tracking;
using Npgsql;

public static class AdoProbe
{
    // Raw ADO.NET through TrackingNpgsqlConnection: two result sets, each read to the end, default CommandBehavior.
    public static async Task Run(string pgCs)
    {
        Func<(string Name, string Id)?> testInfo = () => ("Repro 193", "repro-ado");
        await using var conn = new NpgsqlConnection(pgCs.Replace("Database=k193", "Database=postgres"))
            .WithTestTracking(new NpgsqlTrackingOptions { CurrentTestInfoFetcher = testInfo });
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 AS a UNION ALL SELECT 2; SELECT 'x' AS b";
        var seen = new List<string>();
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            do { while (await r.ReadAsync()) seen.Add(r.GetValue(0).ToString()!); } while (await r.NextResultAsync());
        }
        Console.WriteLine($"ADO two result sets via TrackingNpgsqlConnection: app read [{string.Join(", ", seen)}]");
        Console.WriteLine($"  logged response = {RequestResponseLogger.RequestAndResponseLogs.LastOrDefault(l => l.TestId == "repro-ado" && l.Type == RequestResponseType.Response)?.Content ?? "(none)"}");
    }
}
