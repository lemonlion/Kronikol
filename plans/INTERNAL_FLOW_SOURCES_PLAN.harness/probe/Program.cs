// Probe for issue #138: what does an ActivityListener shaped like Kronikol's InternalFlowActivityListener
// capture from HttpClient (and Grpc.Net.Client), with and without System.Net.Http in ShouldListenTo,
// and does listening to System.Net.Http change Application Insights 2.x dependency telemetry?
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.DependencyCollector;
using Microsoft.ApplicationInsights.Extensibility;

var mode = args[0];          // none | exclude | include | include-parented
var useAi = args[1] == "ai"; // ai | noai
var parent = args[2] == "parent";
var order = args.Length > 3 ? args[3] : "ai-first"; // ai-first | listener-first
var grpc = args.Contains("grpc");

var captured = new ConcurrentQueue<Activity>();
ActivityListener? listener = null;
void StartListener()
{
    if (mode == "none") return;
    listener = new ActivityListener
    {
        ShouldListenTo = s => mode != "exclude" || s.Name != "System.Net.Http",
        Sample = (ref ActivityCreationOptions<ActivityContext> o) =>
            mode == "include-parented" && o.Source.Name == "System.Net.Http" && o.Parent == default
                ? ActivitySamplingResult.None
                : ActivitySamplingResult.AllData,
        SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
        ActivityStopped = a => captured.Enqueue(a),
    };
    ActivitySource.AddActivityListener(listener);
}

var channel = new CapturingChannel();
DependencyTrackingTelemetryModule? module = null;
void StartAi()
{
    if (!useAi) return;
    var config = new TelemetryConfiguration
    {
        ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000001;IngestionEndpoint=http://127.0.0.1:1/",
        TelemetryChannel = channel,
    };
    config.TelemetryInitializers.Add(new OperationCorrelationTelemetryInitializer());
    module = new DependencyTrackingTelemetryModule();
    module.Initialize(config);
}

if (order == "ai-first") { StartAi(); StartListener(); } else { StartListener(); StartAi(); }

// A tiny HTTP/1.1 server that records the traceparent each request carried.
var tcp = new TcpListener(IPAddress.Loopback, 0);
tcp.Start();
var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
var seenTraceparents = new ConcurrentQueue<string>();
_ = Task.Run(async () =>
{
    while (true)
    {
        var client = await tcp.AcceptTcpClientAsync();
        _ = Task.Run(async () =>
        {
            using var c = client;
            var stream = c.GetStream();
            var reader = new StreamReader(stream);
            while (true)
            {
                string? line;
                var tp = "(none)";
                var any = false;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                {
                    any = true;
                    if (line.StartsWith("traceparent:", StringComparison.OrdinalIgnoreCase)) tp = line[12..].Trim();
                }
                if (!any) return;
                seenTraceparents.Enqueue(tp);
                var body = "ok";
                var resp = $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nContent-Type: text/plain\r\n\r\n{body}";
                var bytes = System.Text.Encoding.ASCII.GetBytes(resp);
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
            }
        });
    }
});

Activity? parentActivity = null;
if (parent)
{
    parentActivity = new Activity("Probe.Parent").SetIdFormat(ActivityIdFormat.W3C).Start();
}

using var http = new HttpClient();
for (var i = 0; i < 3; i++)
{
    var r = await http.GetAsync($"http://127.0.0.1:{port}/item/{i}");
    await r.Content.ReadAsStringAsync();
}

if (grpc)
{
    // A unary call to a closed port: the call fails, but the spans it makes are what we want to see.
    var closed = new TcpListener(IPAddress.Loopback, 0);
    closed.Start();
    var closedPort = ((IPEndPoint)closed.LocalEndpoint).Port;
    closed.Stop();
    var target = Environment.GetEnvironmentVariable("PROBE_GRPC_LIVE") == "1" ? port : closedPort;
    using var ch = GrpcChannel.ForAddress($"http://127.0.0.1:{target}");
    var m = new Method<byte[], byte[]>(MethodType.Unary, "probe.Svc", "Do",
        Marshallers.Create(b => b, b => b), Marshallers.Create(b => b, b => b));
    try { await ch.CreateCallInvoker().AsyncUnaryCall(m, null, new CallOptions(), new byte[] { 1 }); }
    catch (RpcException) { }
}

parentActivity?.Stop();
await Task.Delay(300);
module?.Dispose();

var spans = captured.Where(a => a.OperationName != "Probe.Parent").ToList();
Console.WriteLine($"runtime={RuntimeInformation.FrameworkDescription} mode={mode} ai={useAi} parent={parent} order={order}");
Console.WriteLine($"  captured spans: {spans.Count}");
foreach (var g in spans.GroupBy(a => (Src: a.Source.Name, a.OperationName, a.DisplayName, a.Kind, a.Recorded, a.IsAllDataRequested,
             UnderParent: parentActivity != null && a.ParentSpanId == parentActivity.SpanId,
             Server: a.GetTagItem("server.address")?.ToString() ?? "-", Method: a.GetTagItem("grpc.method")?.ToString() ?? "-")))
    Console.WriteLine($"    x{g.Count()} source='{g.Key.Src}' op='{g.Key.OperationName}' display='{g.Key.DisplayName}' kind={g.Key.Kind} recorded={g.Key.Recorded} allData={g.Key.IsAllDataRequested} underParent={g.Key.UnderParent} server.address={g.Key.Server} grpc.method={g.Key.Method}");
foreach (var a in spans.Where(a => !a.Source.Name.StartsWith("Experimental")))
    Console.WriteLine($"    span op={a.OperationName} display={a.DisplayName} parentOp={a.Parent?.OperationName ?? "(none)"} status={a.Status} err={a.GetTagItem("error.type")}");
var httpSpanIds = spans.Where(a => a.OperationName == "System.Net.Http.HttpRequestOut").Select(a => a.Id).ToHashSet();
string Describe(string t) =>
    t == "(none)" ? "none"
    : httpSpanIds.Contains(t) ? "=captured http span"
    : parentActivity != null && t.Contains(parentActivity.SpanId.ToString()) ? "=parent"
    : "uncaptured span";
Console.WriteLine($"  traceparents at server: {string.Join(", ", seenTraceparents.Select(Describe))}");
if (useAi)
{
    var deps = channel.Items.OfType<DependencyTelemetry>().Where(d => d.Type == "Http").ToList();
    Console.WriteLine($"  AI http dependencies: {deps.Count}");
    foreach (var d in deps)
    {
        var id = spans.Any(a => a.SpanId.ToHexString() == d.Id) ? "=captured span" : d.Id;
        var opId = parentActivity != null && d.Context.Operation.Id == parentActivity.TraceId.ToHexString() ? "=parent trace" : d.Context.Operation.Id;
        var opParent = parentActivity != null && d.Context.Operation.ParentId == parentActivity.SpanId.ToHexString() ? "=parent span" : d.Context.Operation.ParentId;
        Console.WriteLine($"    '{d.Name}' id={id} op.id={opId} op.parentId={opParent} success={d.Success}");
    }
}

sealed class CapturingChannel : ITelemetryChannel
{
    public ConcurrentQueue<ITelemetry> Items { get; } = new();
    public bool? DeveloperMode { get; set; }
    public string? EndpointAddress { get; set; }
    public void Dispose() { }
    public void Flush() { }
    public void Send(ITelemetry item) => Items.Enqueue(item);
}
