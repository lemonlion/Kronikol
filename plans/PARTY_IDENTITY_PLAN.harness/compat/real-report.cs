#:package Kronikol@5.16.0
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property PublishAot=false
// Writes a small report with the released Kronikol 5.16.0 that declares its parties (R1) and marks every Cow Service
// call as a test double's (R2), so section 7.5's check runs on what a release really writes (strip-report.py, run.sh).
// usage: dotnet run --file real-report.cs -- <reports dir>
using System.Net;
using Kronikol;
using Kronikol.Parties;
using Kronikol.Reports;
using Kronikol.Tracking;

var dir = args[0];

void Pair(string testId, string name, string caller, string service, string url, AttributionSource source, string? category = null, string? testDouble = null)
{
    var (trace, pair) = (Guid.NewGuid(), Guid.NewGuid());
    RequestResponseLogger.Log(new RequestResponseLog(name, testId, HttpMethod.Get, null, new Uri(url), [], service, caller, RequestResponseType.Request, trace, pair, false, DependencyCategory: category)
        { Timestamp = DateTimeOffset.UtcNow, AttributionSource = source, TestDouble = testDouble });
    RequestResponseLogger.Log(new RequestResponseLog(name, testId, HttpMethod.Get, null, new Uri(url), [], service, caller, RequestResponseType.Response, trace, pair, false, HttpStatusCode.OK, DependencyCategory: category)
        { Timestamp = DateTimeOffset.UtcNow.AddMilliseconds(40), AttributionSource = source, TestDouble = testDouble });
}

Pair("t1", "Order pancakes", "Caller", "Breakfast Provider", "http://localhost/pancakes", AttributionSource.TestContext);
Pair("t1", "Order pancakes", "Breakfast Provider", "Cow Service", "http://localhost:5001/milk", AttributionSource.RequestHeader, testDouble: "WireMock");
Pair("t1", "Order pancakes", "Breakfast Provider", "Pantry DB", "http://localhost:5432/pantry", AttributionSource.RequestHeader, "PostgreSQL");
Pair("t2", "Order waffles", "Caller", "Breakfast Provider", "http://localhost/waffles", AttributionSource.TestContext);
Pair("t2", "Order waffles", "Breakfast Provider", "Cow Service", "http://localhost:5001/milk", AttributionSource.RequestHeader, testDouble: "WireMock");

var start = DateTime.UtcNow.AddMinutes(-1);
ReportGenerator.CreateStandardReportsWithDiagrams(
    [new Feature { DisplayName = "Breakfast", Scenarios = [
        new Scenario { Id = "t1", DisplayName = "Order pancakes", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(120) },
        new Scenario { Id = "t2", DisplayName = "Order waffles", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(90) }] }],
    start, DateTime.UtcNow,
    new ReportConfigurationOptions
    {
        ReportsFolderPath = dir,
        SuiteName = "Breakfast.Tests",
        GenerateSpecificationsReport = false,
        GenerateSpecificationsData = false,
        ServiceId = "breakfast-provider",
        Parties = [new PartyDescriptor("Cow Service") { Kind = PartyKind.Double, Note = "WireMock stub" }, new PartyDescriptor("Pantry DB") { Kind = PartyKind.Real, Note = "Testcontainers" }],
    });
Console.WriteLine("written " + dir);
