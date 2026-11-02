// One whole report through a record whose address is relative, which is what D4 of the review measured. One run per process, since the
// logger is static: args = <reports folder> <mode>. Lists what the run wrote and any exception or warning it printed.
//
//   event          TrackSendEvent("Publish", "Bus", new Uri("orders", UriKind.Relative)), every option at its default
//   event-no-flow  the same with InternalFlowTracking off
//   event-mergeable        the same with GenerateMergeableData on, the others at their defaults
//   event-data-only        a shard's options, as merge-round-trip-fixture.cs sets them: only the mergeable data file, internal flow at its default
//   event-data-only-no-flow  the same with InternalFlowTracking off
//   call           a Default call on the same relative address, every option at its default
//   control        TrackSendEvent on event://orders, every option at its default
//
// Usage, from the harness directory, on the published 5.27.13: dotnet run --file probes/relative-address-report.cs -- <folder> <mode>
// On a worktree: probes/on-prototype.sh <worktree> probes/relative-address-report.cs <scratch dir> <folder> <mode>
#:package Kronikol@5.27.13
#:property PublishAot=false

using System.Reflection;
using Kronikol;
using Kronikol.Reports;
using Kronikol.Tracking;

var (folder, mode) = (args[0], args[1]);
if (Directory.Exists(folder))
    Directory.Delete(folder, recursive: true);

Console.WriteLine($"Kronikol {typeof(RequestResponseLog).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}, mode {mode}");

const string Test = "relative-address-scenario";
var tracker = new MessageTracker(new MessageTrackerOptions { CallerName = "Test", ServiceName = "Bus", CurrentTestInfoFetcher = () => ("A publish on a relative address", Test) });
switch (mode)
{
    case "event":
    case "event-no-flow":
    case "event-mergeable":
    case "event-data-only":
    case "event-data-only-no-flow":
        tracker.TrackSendEvent("Publish", "Bus", new Uri("orders", UriKind.Relative), new { Id = 1 });
        break;
    case "control":
        tracker.TrackSendEvent("Publish", "Bus", new Uri("event://orders"), new { Id = 1 });
        break;
    case "call":
    {
        var (trace, id, uri) = (Guid.NewGuid(), Guid.NewGuid(), new Uri("orders", UriKind.Relative));
        RequestResponseLogger.Log(new RequestResponseLog("A call on a relative address", Test, HttpMethod.Post, null, uri, [], "Bus", "Test", RequestResponseType.Request, trace, id, false));
        RequestResponseLogger.Log(new RequestResponseLog("A call on a relative address", Test, HttpMethod.Post, null, uri, [], "Bus", "Test", RequestResponseType.Response, trace, id, false, System.Net.HttpStatusCode.OK));
        break;
    }
    default:
        throw new ArgumentException(mode);
}

var options = new ReportConfigurationOptions { ReportsFolderPath = folder };
if (mode is "event-no-flow" or "event-data-only-no-flow")
    options.InternalFlowTracking = false;
if (mode is "event-mergeable" or "event-data-only" or "event-data-only-no-flow")
    options.GenerateMergeableData = true;
if (mode is "event-data-only" or "event-data-only-no-flow")
{
    options.GenerateTestRunReport = false;
    options.GenerateSpecificationsReport = false;
    options.GenerateSpecificationsData = false;
    options.GenerateComponentDiagram = false;
}

var scenario = new Scenario { Id = Test, DisplayName = "A publish on a relative address", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1) };
try
{
    ReportGenerator.CreateStandardReportsWithDiagrams([new Feature { DisplayName = "Probe", Scenarios = [scenario] }], DateTime.UtcNow.AddSeconds(-5), DateTime.UtcNow, options);
    Console.WriteLine("the run: no exception");
}
catch (Exception e)
{
    var frame = e.StackTrace?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("at Kronikol", StringComparison.Ordinal)) ?? "";
    Console.WriteLine($"the run: {e.GetType().Name}: {e.Message}");
    Console.WriteLine($"    {frame.Split(" in ")[0]}");
}

Console.WriteLine("files written:");
if (!Directory.Exists(folder))
{
    Console.WriteLine("  (no folder)");
    return 0;
}
foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/')).Order(StringComparer.Ordinal))
    Console.WriteLine($"  {file}");
return 0;
