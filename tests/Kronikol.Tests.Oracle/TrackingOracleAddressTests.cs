using Kronikol.Extensions.Oracle;
using Kronikol.Sql;
using Kronikol.Tracking;
using Oracle.ManagedDataAccess.Client;
using Xunit;

namespace Kronikol.Tests.Oracle;

/// <summary>
/// The address an Oracle arrow names: the server and the service its data source names, as an EZConnect string or a
/// connect descriptor, since ODP.NET reports no database. Until F26 (plans/CLIENT_FLOORS_PLAN.md) every arrow named
/// <c>localhost</c> and a database called <c>unknown</c>, which the client against Oracle Database Free showed
/// (CLIENT_FLOORS_PLAN.harness/results/r2c-oracle-floor.txt).
/// </summary>
public class TrackingOracleAddressTests : IDisposable
{
    private readonly string _testId = Guid.NewGuid().ToString();

    public TrackingOracleAddressTests() => TrackingComponentRegistry.Clear();

    public void Dispose() => TrackingComponentRegistry.Clear();

    [Theory]
    [InlineData("127.0.0.1:21521/FREEPDB1", SqlTrackingVerbosityLevel.Detailed, "oracle://127.0.0.1:21521/FREEPDB1/breakfasts")]
    [InlineData("127.0.0.1:21521/FREEPDB1", SqlTrackingVerbosityLevel.Raw, "oracle://127.0.0.1:21521/FREEPDB1")]
    [InlineData("127.0.0.1:21521/FREEPDB1", SqlTrackingVerbosityLevel.Summarised, "oracle:///FREEPDB1/breakfasts")]
    [InlineData("localhost/XE", SqlTrackingVerbosityLevel.Detailed, "oracle://localhost/XE/breakfasts")]
    [InlineData("(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=dbhost)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=orcl)))", SqlTrackingVerbosityLevel.Detailed, "oracle://dbhost:1521/orcl/breakfasts")]
    public void A_statement_is_drawn_against_the_server_and_service_its_data_source_names(string dataSource, SqlTrackingVerbosityLevel verbosity, string expected)
    {
        var options = new OracleTrackingOptions { CurrentTestInfoFetcher = () => ("TestMethod", _testId), Verbosity = verbosity };
        using var connection = new TrackingOracleConnection(new OracleConnection($"User Id=app;Password=pw;Data Source={dataSource}"), options);
        using var command = new TrackingOracleCommand(new RowReaderFakeDbCommand(), connection);
        command.CommandText = "SELECT id, name FROM breakfasts";

        using (var reader = command.ExecuteReader())
        {
            while (reader.Read()) { }
        }

        var request = RequestResponseLogger.RequestAndResponseLogs.Single(l => l.TestId == _testId && l.Type == RequestResponseType.Request);
        Assert.Equal(expected, request.Uri.ToString());
    }
}
