using Kronikol.Extensions.EfCore.Relational;
using Kronikol.Tracking;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kronikol.Tests.EfCore.Relational;

/// <summary>
/// A command the database rejects, run through a real relational provider so EF Core itself calls the
/// interceptor's failure path.
/// </summary>
public class SqlTrackingInterceptorFailureTests : IDisposable
{
    private readonly string _testId = Guid.NewGuid().ToString();
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public SqlTrackingInterceptorFailureTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private RequestResponseLog[] GetLogsForTest()
        => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private DbContext CreateContext() => new(new DbContextOptionsBuilder()
        .UseSqlite(_connection)
        .AddInterceptors(new SqlTrackingInterceptor(new SqlTrackingInterceptorOptions
        {
            CurrentTestInfoFetcher = () => ("My Test", _testId)
        }))
        .Options);

    public static TheoryData<string> ExecutePaths => new() { "ExecuteSqlRaw", "ExecuteSqlRawAsync", "SqlQueryRaw", "SqlQueryRawAsync" };

    [Theory]
    [MemberData(nameof(ExecutePaths))]
    public async Task A_command_that_fails_records_an_Error_response_with_its_message_and_the_exception_reaches_the_caller(string path)
    {
        await using var context = CreateContext();
        const string sql = "SELECT * FROM no_such_table_126";

        var thrown = await Record.ExceptionAsync(async () =>
        {
            switch (path)
            {
                case "ExecuteSqlRaw": context.Database.ExecuteSqlRaw(sql); break;
                case "ExecuteSqlRawAsync": await context.Database.ExecuteSqlRawAsync(sql); break;
                case "SqlQueryRaw": _ = context.Database.SqlQueryRaw<int>(sql).ToList(); break;
                case "SqlQueryRawAsync": _ = await context.Database.SqlQueryRaw<int>(sql).ToListAsync(); break;
                default: throw new ArgumentOutOfRangeException(nameof(path), path, null);
            }
        });

        var rejected = Assert.IsType<SqliteException>(thrown);
        var logs = GetLogsForTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
        Assert.Equal(logs[0].RequestResponseId, logs[1].RequestResponseId);
        Assert.Equal("Error", logs[1].StatusCode?.Value?.ToString());
        Assert.Equal(rejected.Message, logs[1].Content);
    }
}
