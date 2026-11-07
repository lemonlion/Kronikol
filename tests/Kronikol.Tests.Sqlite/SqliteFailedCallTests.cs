using Kronikol.Extensions.Sqlite;
using Kronikol.History;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Kronikol.Tests.Sqlite;

/// <summary>
/// A SQL call that fails, on real SQLite (plans/FAILED_CALLS_PLAN.md F9, F10, §5.7): a command that fails, and a reader that fails
/// partway, are each answered <c>Error</c> with why, where the reader was answered at Close as a success with the rows read so far;
/// and an answer belongs to its request's test, whatever the fetcher reads by the time the call ends.
/// </summary>
public class SqliteFailedCallTests : IDisposable
{
    private string _testId = Guid.NewGuid().ToString();
    private readonly string _requestTestId;
    private readonly SqliteConnection _inner = new("Data Source=:memory:");
    private readonly TrackingSqliteConnection _tracking;

    public SqliteFailedCallTests()
    {
        _requestTestId = _testId;
        _tracking = new TrackingSqliteConnection(_inner, new SqliteTrackingOptions
        {
            ServiceName = "SQLite",
            CurrentTestInfoFetcher = () => ("Orders are stored", _testId)
        });
        _tracking.Open();
    }

    public void Dispose()
    {
        _tracking.Dispose();
        _inner.Dispose();
    }

    private RequestResponseLog[] Logs() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _requestTestId).ToArray();

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public void A_command_that_fails_is_answered_Error_with_why()
    {
        using var command = _tracking.CreateCommand();
        command.CommandText = "SELECT * FROM NoSuchTable";

        var caught = Assert.Throws<SqliteException>(() => command.ExecuteReader());

        FailedCallContract.AssertAnswered(Logs(), caught, status: "Error");
    }

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public void A_reader_whose_second_row_fails_is_answered_Error_where_it_was_answered_as_a_success()
    {
        using var command = _tracking.CreateCommand();
        command.CommandText = "SELECT 1 UNION ALL SELECT abs(-9223372036854775808)";

        var caught = Assert.Throws<SqliteException>(() =>
        {
            using var reader = command.ExecuteReader();
            while (reader.Read()) { }
        });

        Assert.Contains("integer overflow", caught.Message, StringComparison.Ordinal);
        FailedCallContract.AssertAnswered(Logs(), caught, status: "Error");
    }

    [Fact]
    public void History_rules_before_12_read_a_reader_that_failed_partway_as_the_success_it_was_answered_with()
    {
        using var command = _tracking.CreateCommand();
        command.CommandText = "SELECT 1 UNION ALL SELECT abs(-9223372036854775808)";

        Assert.Throws<SqliteException>(() =>
        {
            using var reader = command.ExecuteReader();
            while (reader.Read()) { }
        });

        Assert.Equal("Error", Assert.Single(InteractionShape.Calls(Logs(), null, 12)).Status);
        foreach (var rule in InteractionShape.KeptVersions.Where(v => v < 12))
            Assert.Equal("OK", Assert.Single(InteractionShape.Calls(Logs(), null, rule)).Status);
    }

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public void An_answer_belongs_to_its_requests_test_when_the_test_reads_differently_by_the_time_the_call_ends()
    {
        using var command = _tracking.CreateCommand();
        command.CommandText = "SELECT 1";

        using (var reader = command.ExecuteReader())
        {
            while (reader.Read()) { }
            // Other code closes the reader, after the fetcher has moved on (FAILED_CALLS_PLAN.md F9).
            _testId = Guid.NewGuid().ToString();
        }

        var answer = Assert.Single(Logs(), l => l.Type == RequestResponseType.Response);
        Assert.Equal("OK", answer.StatusCode?.Value);
        Assert.Empty(RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId));
    }
}
