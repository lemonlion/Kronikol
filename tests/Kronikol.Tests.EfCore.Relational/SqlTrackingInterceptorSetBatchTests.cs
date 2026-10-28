using Kronikol.Extensions.EfCore.Relational;
using Kronikol.Tracking;

namespace Kronikol.Tests.EfCore.Relational;

/// <summary>
/// EF Core opens every SQL Server SaveChanges batch with two SET statements (<c>SET IMPLICIT_TRANSACTIONS OFF;</c>,
/// <c>SET NOCOUNT ON;</c>). Until #192 R1 the classifier stripped only the first, so a batch was <c>Other</c> with no
/// table, and at Summarised was not recorded at all. The call now names its statement, and keeps what 5.16.1 recorded
/// for history's rules 6 and 7 (<see cref="RequestResponseLog.RecordedBefore8"/>), so the change costs no history.
/// </summary>
public class SqlTrackingInterceptorSetBatchTests : IDisposable
{
    private const string Batch = "SET IMPLICIT_TRANSACTIONS OFF;\r\nSET NOCOUNT ON;\r\nINSERT INTO [Orders] ([Status]) OUTPUT INSERTED.[Id] VALUES (@p0);";
    private readonly string _testId = Guid.NewGuid().ToString();

    public void Dispose() => TestPhaseContext.Reset();

    private RequestResponseLog Request(SqlTrackingVerbosity verbosity, string commandText)
    {
        var interceptor = new SqlTrackingInterceptor(new SqlTrackingInterceptorOptions
        {
            ServiceName = "Database",
            CallerName = "TestCaller",
            Verbosity = verbosity,
            CurrentTestInfoFetcher = () => ("My Test", _testId),
        });
        var command = new StubDbCommand(commandText, "MyDb", "localhost");
        interceptor.LogCommandExecuting(command);
        interceptor.LogCommandExecuted(command, rowsAffected: 1);
        return Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == _testId && l.Type == RequestResponseType.Request);
    }

    [Fact]
    public void A_save_changes_batch_is_recorded_at_summarised_and_was_not_before()
    {
        var request = Request(SqlTrackingVerbosity.Summarised, Batch);

        Assert.Equal("Insert", request.Method.Value);
        Assert.Equal("sql:///MyDb/Orders", request.Uri.ToString());
        Assert.Same(RecordedBefore.NotRecorded, request.RecordedBefore8);
    }

    [Fact]
    public void A_save_changes_batch_names_its_statement_at_detailed_and_keeps_its_old_record()
    {
        var request = Request(SqlTrackingVerbosity.Detailed, Batch);

        Assert.Equal("Insert", request.Method.Value);
        Assert.Equal("sql://localhost/MyDb/Orders", request.Uri.ToString());
        var before = Assert.IsType<RecordedBefore>(request.RecordedBefore8);
        Assert.Equal("Other", before.Method.Value);
        Assert.Equal("sql://localhost/MyDb", before.Uri.ToString());
        Assert.False(before.Absent);
    }

    [Fact]
    public void A_save_changes_batch_names_its_keyword_at_raw_and_keeps_its_old_record()
    {
        var request = Request(SqlTrackingVerbosity.Raw, Batch);

        Assert.Equal("INSERT", request.Method.Value);
        var before = Assert.IsType<RecordedBefore>(request.RecordedBefore8);
        Assert.Equal("SQL", before.Method.Value);
        Assert.Equal(request.Uri, before.Uri);
    }

    [Fact]
    public void A_statement_the_old_classifier_read_alike_carries_no_old_record()
    {
        Assert.Null(Request(SqlTrackingVerbosity.Detailed, "SET NOCOUNT ON;\r\nSELECT [o].[Id] FROM [Orders] AS [o]").RecordedBefore8);
    }
}
