using Kronikol.Reports;
using Kronikol.Tracking;

// A call a detached flow held under the first step, confirmed after the second step closed, then a call between steps.
using var isolated = RequestResponseLogger.IsolateForTests();
var testId = "held-" + Guid.NewGuid();

static RequestResponseLog Real(string testId, string service) =>
    new("Test", testId, HttpMethod.Get, null, new Uri("http://" + service + "/x"), [], service, "api",
        RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false);

StepCollector.StartStep(testId, null, "first step", null, null);
RequestResponseLogger.Log(Real(testId, "in-first"));
using (TestIdentityScope.Detach())
{
    TestIdentityScope.CurrentFlow!.Open("Test", testId);
    RequestResponseLogger.Log(Real(testId, "held") with { AttributionSource = AttributionSource.DocumentFlow });
    StepCollector.CompleteStep(testId, true);

    StepCollector.StartStep(testId, null, "second step", null, null);
    RequestResponseLogger.Log(Real(testId, "in-second"));
    StepCollector.CompleteStep(testId, true);

    // the flow's next operation on the owner's document confirms what it held
    TestIdentityScope.CurrentFlow!.Confirm();
    RequestResponseLogger.Log(Real(testId, "confirming-operation") with { AttributionSource = AttributionSource.DocumentOwner });
}
RequestResponseLogger.Log(Real(testId, "between-steps"));

var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();
var features = new[] { new Feature { DisplayName = "F", Scenarios = [new Scenario { Id = testId, DisplayName = "S", Steps = StepCollector.GetSteps(testId) }] } };
var paths = ReportGenerator.AttributeInteractionsToStepPaths(logs, features)[testId];
var calls = logs.Where(l => !l.IsDiagramMarker).Select(l => l.ServiceName).ToArray();
for (var i = 0; i < calls.Length; i++)
    Console.WriteLine($"{paths[i] ?? "-"} {calls[i]}");
