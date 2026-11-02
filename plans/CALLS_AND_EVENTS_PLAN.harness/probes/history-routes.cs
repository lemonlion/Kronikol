// Whether the routes the plan's list misses raise F1. One run per process, published 5.27.13. args = <reports folder>. The ledger comes from KRONIKOL_HISTORY.
// Every run makes the same calls, so a changed verdict is false. One scenario per route, so the verdict names it.
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
using Kronikol.Reports;
using Kronikol.Extensions.Grpc;
using Kronikol.Extensions.Redis;
using Kronikol.Extensions.StorageQueues;
using Kronikol.Tracking;

var folder = args[0];
var id = "";
var name = "";
(string, string) Current() => (name, id);
var scenarios = new List<Scenario>();
void Run(string sid, string display, Action calls)
{
    id = sid;
    name = display;
    calls();
    scenarios.Add(new Scenario { Id = sid, DisplayName = display, Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1) });
}

var redis = new RedisTracker(new RedisTrackingDatabaseOptions { ServiceName = "Redis", CurrentTestInfoFetcher = Current });
var redisRaw = new RedisTracker(new RedisTrackingDatabaseOptions { ServiceName = "Redis Raw", Verbosity = RedisTrackingVerbosity.Raw, CurrentTestInfoFetcher = Current });
void Redis(RedisTracker tracker, string command)
{
    var (req, trace) = tracker.LogRedisRequest(command, "k", 0, "c");
    tracker.LogRedisResponse(command, "k", 0, true, req, trace, "v");
}
Run("r1", "Redis GET, the real tracker at its default", () => Redis(redis, "GET"));
Run("r2", "Redis DEL, the real tracker at its default", () => Redis(redis, "DEL"));
Run("r3", "Redis SET, control", () => Redis(redis, "SET"));
Run("r4", "Redis GET at Raw, control", () => Redis(redisRaw, "GET"));

var marshaller = Marshallers.Create<string>(s => System.Text.Encoding.UTF8.GetBytes(s), b => System.Text.Encoding.UTF8.GetString(b));
var grpc = new GrpcTrackingInterceptor(new GrpcTrackingOptions { ServiceName = "Orders gRPC", CurrentTestInfoFetcher = Current });
void Rpc(string rpc)
{
    var method = new Method<string, string>(MethodType.Unary, "orders.Orders", rpc, marshaller, marshaller);
    grpc.BlockingUnaryCall("{}", new ClientInterceptorContext<string, string>(method, "localhost", new CallOptions()), (r, c) => "{}");
}
Run("g1", "gRPC method named Get, default verbosity", () => Rpc("Get"));
Run("g2", "gRPC method named GetOrder, control", () => Rpc("GetOrder"));

var proxied = TrackingProxy<IOrders>.Create(new Orders(), new TrackingProxyOptions { ServiceName = "Proxy", CurrentTestInfoFetcher = Current });
Run("p1", "DispatchProxy method named Delete", () => proxied.Delete(1));
Run("p2", "DispatchProxy method named Find, control", () => proxied.Find(1));

using var queue = new HttpClient(new StorageQueueTrackingMessageHandler(
    new StorageQueueTrackingMessageHandlerOptions { ServiceName = "Queue", CurrentTestInfoFetcher = Current }, new Answer()));
Run("q1", "Storage Queues delete, default verbosity", () => queue.DeleteAsync("https://acct.queue.core.windows.net/orders/messages/m1?popreceipt=x").GetAwaiter().GetResult());

void Hand(string service, OneOf<HttpMethod, string> method)
{
    var request = Guid.NewGuid();
    var trace = Guid.NewGuid();
    var uri = new Uri($"probe:///{service.Replace(' ', '-')}");
    RequestResponseLogger.Log(new RequestResponseLog(name, id, method, "body", uri, [], service, "Caller", RequestResponseType.Request, trace, request, false));
    RequestResponseLogger.Log(new RequestResponseLog(name, id, method, "answer", uri, [], service, "Caller", RequestResponseType.Response, trace, request, false));
}
Run("h1", "Hand new HttpMethod(patch)", () => Hand("Hand A", new HttpMethod("patch")));
Run("h2", "Hand HttpMethod.Parse(patch)", () => Hand("Hand B", HttpMethod.Parse("patch")));
Run("h3", "Hand HttpMethod.Patch, control", () => Hand("Hand C", HttpMethod.Patch));
Run("h4", "Hand string get", () => Hand("Hand D", "get"));
Run("h5", "Hand string DELETE, control", () => Hand("Hand E", "DELETE"));
Run("h6", "Hand new HttpMethod(propfind)", () => Hand("Hand F", new HttpMethod("propfind")));

ReportGenerator.CreateStandardReportsWithDiagrams(
    [new Feature { DisplayName = "Probe", Scenarios = scenarios.ToArray() }], DateTime.UtcNow.AddSeconds(-5), DateTime.UtcNow,
    new ReportConfigurationOptions
    {
        ReportsFolderPath = folder, PlantUmlRendering = PlantUmlRendering.BrowserJs, GenerateMergeableData = true,
        GenerateComponentDiagram = false, WriteRunSummaryToConsole = false,
    });

public interface IOrders { int Delete(int x); int Find(int x); }
public class Orders : IOrders { public int Delete(int x) => x; public int Find(int x) => x; }
public class Answer : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)); }
