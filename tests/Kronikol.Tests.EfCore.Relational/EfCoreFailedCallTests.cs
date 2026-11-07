using System.Data.Common;
using Kronikol.Extensions.EfCore.Relational;
using Kronikol.History;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kronikol.Tests.EfCore.Relational;

/// <summary>
/// An EF Core command that fails or is cancelled (plans/FAILED_CALLS_PLAN.md F8, §5.7): a failed command's answer carries why, and
/// a cancelled one, which EF Core hands to CommandCanceled with no exception, is answered where it was left alone.
/// </summary>
public class EfCoreFailedCallTests : IDisposable
{
    private readonly string _testId = Guid.NewGuid().ToString();
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public EfCoreFailedCallTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private RequestResponseLog[] Logs() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private Orders Context(params IInterceptor[] after)
    {
        var tracking = new SqlTrackingInterceptor(new SqlTrackingInterceptorOptions
        {
            ServiceName = "SQLite",
            CurrentTestInfoFetcher = () => ("Orders are kept", _testId)
        });
        return new Orders(new DbContextOptionsBuilder<Orders>().UseSqlite(_connection).AddInterceptors([tracking, .. after]).Options);
    }

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public async Task A_command_that_fails_is_answered_Error_with_why()
    {
        await using var db = Context();

        var caught = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("SELECT * FROM NoSuchTable", TestContext.Current.CancellationToken));

        FailedCallContract.AssertAnswered(Logs(), caught, status: "Error");
    }

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public async Task A_cancelled_command_is_answered_where_it_was_left_alone()
    {
        using var cancel = new CancellationTokenSource();
        await using var db = Context(new CancelOnExecuting(cancel));
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var before = Logs().Length;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.Rows.ToListAsync(cancel.Token));

        var call = Logs().Skip(before).ToArray();
        var request = Assert.Single(call, l => l.Type == RequestResponseType.Request);
        var answer = Assert.Single(call, l => l.Type == RequestResponseType.Response);
        Assert.Equal(request.RequestResponseId, answer.RequestResponseId);
        Assert.Equal("Error", answer.StatusCode?.Value);
        Assert.Equal("The command was cancelled.", answer.Error);
        foreach (var rule in InteractionShape.KeptVersions.Where(v => v < 12))
            Assert.Null(InteractionShape.Calls(call, null, rule).Single().Status);
    }

    public sealed class Orders(DbContextOptions<Orders> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
    }

    public sealed class Row
    {
        public int Id { get; set; }
    }

    // Registered after the tracking interceptor, so the request is logged before the token is cancelled.
    private sealed class CancelOnExecuting(CancellationTokenSource cancel) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("\"Rows\"", StringComparison.Ordinal))
                cancel.Cancel();
            return ValueTask.FromResult(result);
        }
    }
}
