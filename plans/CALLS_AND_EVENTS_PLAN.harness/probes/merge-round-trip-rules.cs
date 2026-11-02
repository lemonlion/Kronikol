// One run of a suite whose package moves across a change of InteractionShape.Version: the marks a tracker sets when a rule
// changes what it records (RecordedBefore11, NamedByRemoteHostFallback) are set here as the tracker sets them, since a data
// file never carries them. The package is chosen by the directive below, which merge-round-trip-rules.py replaces per build.
// args: <folder> <ledger or -> <run number> <last run number>
#:package Kronikol@5.27.13
#:property PublishAot=false

using System.Net;
using System.Reflection;
using Kronikol;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Tracking;

var (folder, ledger, number, last) = (args[0], args[1], int.Parse(args[2]), int.Parse(args[3]));
var rule = InteractionShape.Version;
var at = DateTimeOffset.UtcNow;
const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

// Sets an internal mark where this package has it.
RequestResponseLog Mark(RequestResponseLog log, string member, object? value)
{
    typeof(RequestResponseLog).GetProperty(member, Any)?.SetValue(log, value);
    return log;
}

void Call(string id, string name, OneOf<HttpMethod, string> method, string uri, string service, Func<RequestResponseLog, RequestResponseLog>? mark = null)
{
    var request = Guid.NewGuid();
    var trace = Guid.NewGuid();
    var parsed = new Uri(uri);
    at = at.AddMilliseconds(3);
    var req = new RequestResponseLog(name, id, method, null, parsed, [], service, "Caller", RequestResponseType.Request, trace, request, false) { Timestamp = at };
    var res = new RequestResponseLog(name, id, method, null, parsed, [], service, "Caller", RequestResponseType.Response, trace, request, false, HttpStatusCode.OK) { Timestamp = at.AddMilliseconds(1) };
    RequestResponseLogger.Log(mark is null ? req : mark(req));
    RequestResponseLogger.Log(res);
}

Scenario Sc(string id, string name, double seconds) => new() { Id = id, DisplayName = name, Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(seconds) };

Call("a", "stays", HttpMethod.Get, "http://h/stays", "Svc");
Call("b", "a label", "Delete", "http://h/label", "Svc");
Call("c", "a real change in the last run", HttpMethod.Get, "http://h/change" + (number == last ? "/new" : ""), "Svc");
// Named by its remote host from rule 7 on, where 6 called it localhost:{port}.
if (rule >= 7)
    Call("d", "named by its remote host", HttpMethod.Get, "http://orders-host:5000/x", "orders-host:5000", log => Mark(log, "NamedByRemoteHostFallback", true));
else
    Call("d", "named by its remote host", HttpMethod.Get, "http://orders-host:5000/x", "localhost:5000");
// What a tracker recorded otherwise before rule 11: the Blob Storage handler against Azurite, Other from the account then.
if (rule >= 11)
{
    var before = Type.GetType("Kronikol.Tracking.RecordedBefore, Kronikol");
    Call("e", "recorded otherwise before rule 11", "Upload", "http://az/container/blob", "Blob",
        log => Mark(log, "RecordedBefore11", Activator.CreateInstance(before!, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [(OneOf<HttpMethod, string>)"Other", new Uri("http://az/account"), false], null)));
}
else
    Call("e", "recorded otherwise before rule 11", "Other", "http://az/account", "Blob");

var end = DateTime.UtcNow;
ReportGenerator.CreateStandardReportsWithDiagrams(
    [new Feature { DisplayName = "Rules", Scenarios = [Sc("a", "stays", 1.1), Sc("b", "a label", 1.2), Sc("c", "a real change in the last run", 1.3), Sc("d", "named by its remote host", 1.4), Sc("e", "recorded otherwise before rule 11", 1.5)] }],
    end.AddSeconds(-5), end,
    new ReportConfigurationOptions
    {
        ReportsFolderPath = folder,
        PlantUmlRendering = PlantUmlRendering.BrowserJs,
        GenerateMergeableData = true,
        GenerateComponentDiagram = false,
        GenerateSpecificationsReport = false,
        GenerateSpecificationsData = false,
        GenerateTestRunReport = false,
        WriteRunSummaryToConsole = false,
        // No ledger at run time, so each run's fragment is undecided on Partial and carries every kept rule's bridge.
        HistoryFilePath = null,
        WriteHistoryLedger = false,
        SuiteName = "fixture",
    });
Console.WriteLine($"rule {rule}");
return 0;
