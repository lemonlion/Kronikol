using Microsoft.Data.Sqlite;
using Kronikol.Extensions.Sqlite;
using Kronikol.Sql;
using Kronikol.Tracking;
using Xunit;

namespace Kronikol.Tests.Sqlite;

/// <summary>
/// Response payload capture through a real in-memory SQLite database: SELECT detail follows the
/// effective verbosity (actual rows at Raw/Detailed, count+columns at Summarised) unless
/// ResponseDetail is set explicitly; scalars log their value. Before 3.0.74 this wrapper logged
/// empty responses for readers and scalars.
/// </summary>
public class TrackingSqliteResponseCaptureTests : IDisposable
{
    private readonly string _testId = Guid.NewGuid().ToString();
    private readonly SqliteTrackingOptions _options;
    private readonly SqliteConnection _inner;
    private readonly TrackingSqliteConnection _tracking;

    public TrackingSqliteResponseCaptureTests()
    {
        TrackingComponentRegistry.Clear();
        _options = new SqliteTrackingOptions { CurrentTestInfoFetcher = () => ("TestMethod", _testId) };
        _inner = new SqliteConnection("Data Source=:memory:");
        _tracking = new TrackingSqliteConnection(_inner, _options);
        _tracking.Open();

        // Seed through the inner connection so only the facts' own commands are logged.
        using var setup = _inner.CreateCommand();
        setup.CommandText = "CREATE TABLE breakfasts (id INTEGER, name TEXT); INSERT INTO breakfasts VALUES (1, 'Pancakes'), (2, 'Waffles')";
        setup.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _tracking.Dispose();
        TrackingComponentRegistry.Clear();
    }

    private RequestResponseLog[] GetLogsForTest()
        => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private string? RunSelectAndGetResponseContent()
    {
        using (var cmd = _tracking.CreateCommand())
        {
            cmd.CommandText = "SELECT id, name FROM breakfasts ORDER BY id";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) { }
        }
        return GetLogsForTest().Last(l => l.Type == RequestResponseType.Response).Content;
    }

    [Fact]
    public void Select_at_default_detailed_verbosity_logs_actual_rows()
    {
        var content = RunSelectAndGetResponseContent();

        Assert.NotNull(content);
        Assert.Contains("\"name\":\"Pancakes\"", content);
        Assert.Contains("\"name\":\"Waffles\"", content);
    }

    [Fact]
    public void Select_at_summarised_verbosity_logs_count_and_columns()
    {
        _options.Verbosity = SqlTrackingVerbosityLevel.Summarised;
        Assert.Equal("2 rows [id, name]", RunSelectAndGetResponseContent());
    }

    [Fact]
    public void Explicit_ResponseDetail_wins_over_verbosity()
    {
        _options.ResponseDetail = SqlResponseDetail.RowCountAndColumns;
        Assert.Equal("2 rows [id, name]", RunSelectAndGetResponseContent());
    }

    [Fact]
    public void Scalar_logs_its_value()
    {
        using (var cmd = _tracking.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM breakfasts";
            cmd.ExecuteScalar();
        }

        Assert.Equal("2", GetLogsForTest().Last(l => l.Type == RequestResponseType.Response).Content);
    }

    [Fact]
    public void LogResponseContent_false_keeps_responses_empty()
    {
        _options.LogResponseContent = false;
        Assert.Null(RunSelectAndGetResponseContent());
    }
}
