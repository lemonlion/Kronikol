// Whether a merge's DELETE changes the service card for a Storage Queues delete. args[0] = reports folder. Published 5.27.13.
#:package Kronikol@5.27.13
#:package Kronikol.Extensions.StorageQueues@5.27.13
#:property PublishAot=false
#:property NoWarn=CS0618

using System.Net;
using Kronikol;
using Kronikol.Extensions.StorageQueues;
using Kronikol.Reports;
using Kronikol.Tracking;

const string id = "s1";
const string name = "Deleting a message";
(string, string) Current() => (name, id);
foreach (var verbosity in new[] { StorageQueueTrackingVerbosity.Detailed })
{
    using var client = new HttpClient(new StorageQueueTrackingMessageHandler(
        new StorageQueueTrackingMessageHandlerOptions { ServiceName = "Queue", CallerName = "Orders Api", Verbosity = verbosity, CurrentTestInfoFetcher = Current }, new Answer()));
    client.DeleteAsync("https://acct.queue.core.windows.net/orders/messages/m1?popreceipt=x").GetAwaiter().GetResult();
}
var features = new[] { new Feature { DisplayName = "Probe", Scenarios = [new Scenario { Id = id, DisplayName = name, Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1) }] } };
ReportGenerator.CreateStandardReportsWithDiagrams(features, DateTime.UtcNow.AddSeconds(-5), DateTime.UtcNow, new ReportConfigurationOptions
{
    ReportsFolderPath = args[0], PlantUmlRendering = PlantUmlRendering.BrowserJs, GenerateMergeableData = true,
    GenerateComponentDiagram = false, WriteRunSummaryToConsole = false, ServiceId = "orders-api", Parties = [new("Orders Api") { Id = "orders-api", Kind = Kronikol.Parties.PartyKind.UnderTest }],
});
public class Answer : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)); }
