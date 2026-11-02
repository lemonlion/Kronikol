// Real routes, published 5.27.13, that write a verb-cased label the plan's reflection over database classifiers did not read, plus
// hand-logged shapes for Q6. Writes one mergeable shard to args[0].
#:package Kronikol@5.27.13
#:package Kronikol.Extensions.Redis@5.27.13
#:package Kronikol.Extensions.Grpc@5.27.13
#:package Kronikol.Extensions.StorageQueues@5.27.13
#:property PublishAot=false
#:property NoWarn=CS0618

using System.Net;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Kronikol;
using Kronikol.Constants;
using Kronikol.Extensions.Grpc;
using Kronikol.Extensions.Redis;
using Kronikol.Extensions.StorageQueues;
using Kronikol.Reports;
using Kronikol.Tracking;

var folder = args[0];
var ledger = args.Length > 1 ? args[1] : null;
var runId = args.Length > 2 ? args[2] : null;
const string id = "s1";
const string name = "Labels";
(string, string) Current() => (name, id);

// Redis: the tracker's own two calls, at each verbosity, through a Hit and through the commands with no cache result.
foreach (var verbosity in new[] { RedisTrackingVerbosity.Detailed, RedisTrackingVerbosity.Summarised, RedisTrackingVerbosity.Raw })
{
    var tracker = new RedisTracker(new RedisTrackingDatabaseOptions { ServiceName = $"Redis {verbosity}", Verbosity = verbosity, CurrentTestInfoFetcher = Current });
    foreach (var command in new[] { "GET", "DEL" })
    {
        var (req, trace) = tracker.LogRedisRequest(command, "k", 0, "c");
        tracker.LogRedisResponse(command, "k", 0, true, req, trace, "v");
    }
}

// gRPC: the interceptor, a unary call on a method a consumer named.
var marshaller = Marshallers.Create<string>(s => System.Text.Encoding.UTF8.GetBytes(s), b => System.Text.Encoding.UTF8.GetString(b));
foreach (var verbosity in new[] { GrpcTrackingVerbosity.Detailed, GrpcTrackingVerbosity.Summarised, GrpcTrackingVerbosity.Raw })
{
    var interceptor = new GrpcTrackingInterceptor(new GrpcTrackingOptions { ServiceName = $"gRPC {verbosity}", Verbosity = verbosity, CurrentTestInfoFetcher = Current });
    foreach (var rpc in new[] { "Get", "Delete", "Put" })
    {
        var method = new Method<string, string>(MethodType.Unary, "orders.Orders", rpc, marshaller, marshaller);
        var context = new ClientInterceptorContext<string, string>(method, "localhost", new CallOptions());
        interceptor.BlockingUnaryCall("{}", context, (request, c) => "{}");
    }
}

// DispatchProxy: an interface method's own name, and MediatR's route is this one with Send/Publish/CreateStream.
var proxied = TrackingProxy<IOrders>.Create(new Orders(), new TrackingProxyOptions { ServiceName = "Proxy", CurrentTestInfoFetcher = Current });
proxied.Get(1); proxied.Delete(1); proxied.Put(1); proxied.Post(1); proxied.Patch(1); proxied.Head(1); proxied.Options(1); proxied.Trace(1); proxied.Connect(1);
proxied.DeleteAsync(1).GetAwaiter().GetResult();

// Storage Queues: its real handler at each verbosity.
foreach (var verbosity in new[] { StorageQueueTrackingVerbosity.Detailed, StorageQueueTrackingVerbosity.Summarised, StorageQueueTrackingVerbosity.Raw })
{
    using var client = new HttpClient(new StorageQueueTrackingMessageHandler(
        new StorageQueueTrackingMessageHandlerOptions { ServiceName = $"Queue {verbosity}", Verbosity = verbosity, CurrentTestInfoFetcher = Current }, new Answer()));
    client.DeleteAsync("https://acct.queue.core.windows.net/orders/messages/m1?popreceipt=x").GetAwaiter().GetResult();
}

void Hand(string service, OneOf<HttpMethod, string> method, RequestResponseMetaType kind)
{
    var request = Guid.NewGuid();
    var trace = Guid.NewGuid();
    var uri = new Uri($"probe:///{service.Replace(' ', '-')}");
    RequestResponseLogger.Log(new RequestResponseLog(name, id, method, "body", uri, [], service, "Caller",
        RequestResponseType.Request, trace, request, false, MetaType: kind));
    RequestResponseLogger.Log(new RequestResponseLog(name, id, method, "answer", uri, [], service, "Caller",
        RequestResponseType.Response, trace, request, false));
}

// Q6: a real HttpMethod with a lower-case name, by hand, and strings.
Hand("Hand new HttpMethod(patch)", new HttpMethod("patch"), RequestResponseMetaType.Default);
Hand("Hand new HttpMethod(get)", new HttpMethod("get"), RequestResponseMetaType.Default);
Hand("Hand new HttpMethod(Get)", new HttpMethod("Get"), RequestResponseMetaType.Default);
Hand("Hand HttpMethod.Parse(patch)", HttpMethod.Parse("patch"), RequestResponseMetaType.Default);
Hand("Hand HttpMethod.Get", HttpMethod.Get, RequestResponseMetaType.Default);
Hand("Hand string get", "get", RequestResponseMetaType.Default);
Hand("Hand string Get", "Get", RequestResponseMetaType.Default);
Hand("Hand string Get Event", "Get", RequestResponseMetaType.Event);
Hand("Hand string DELETE", "DELETE", RequestResponseMetaType.Default);
Hand("Hand string DELETE Event", "DELETE", RequestResponseMetaType.Event);
Hand("Hand new HttpMethod(FETCH)", new HttpMethod("FETCH"), RequestResponseMetaType.Default);
Hand("Hand new HttpMethod(propfind)", new HttpMethod("propfind"), RequestResponseMetaType.Default);

var features = new[] { new Feature { DisplayName = "Probe", Scenarios = [new Scenario { Id = id, DisplayName = name, Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1) }] } };
ReportGenerator.CreateStandardReportsWithDiagrams(features, DateTime.UtcNow.AddSeconds(-5), DateTime.UtcNow, new ReportConfigurationOptions
{
    ReportsFolderPath = folder, PlantUmlRendering = PlantUmlRendering.BrowserJs, GenerateMergeableData = true,
    GenerateComponentDiagram = false, WriteRunSummaryToConsole = false,
    HistoryFilePath = ledger, HistoryRunId = runId,
});

public interface IOrders { int Get(int x); int Delete(int x); int Put(int x); int Post(int x); int Patch(int x); int Head(int x); int Options(int x); int Trace(int x); int Connect(int x); Task<int> DeleteAsync(int x); }
public class Orders : IOrders { public int Get(int x) => x; public int Delete(int x) => x; public int Put(int x) => x; public int Post(int x) => x; public int Patch(int x) => x; public int Head(int x) => x; public int Options(int x) => x; public int Trace(int x) => x; public int Connect(int x) => x; public Task<int> DeleteAsync(int x) => Task.FromResult(x); }
public class Answer : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)); }
