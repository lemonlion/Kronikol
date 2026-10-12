// Probe for #135 research (not repo code): the SHIPPED KafkaBuildInterceptor 4.9.0, end to end.
using Confluent.Kafka;
using Kronikol.Extensions.Kafka;
using Kronikol.Extensions.Kafka.BuildInterception;
using Kronikol.Tracking;

Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
static string Name(object o) => $"{o.GetType().Name}<{string.Join(",", o.GetType().GetGenericArguments().Select(a => a.Name))}>";
var cfg = new ProducerConfig { BootstrapServers = "localhost:9092", MessageTimeoutMs = 1000 };
KafkaBuildInterceptor.EnableProducerTracking<string, string>(o => { o.CurrentTestInfoFetcher = () => ("probe", "probe-id"); });
using var ss = new ProducerBuilder<string, string>(cfg).Build();
Console.WriteLine($"enabled <string,string>; ProducerBuilder<string,string>         -> {Name(ss)}");
using var dep = new DependentProducerBuilder<string, string>(ss.Handle).Build();
Console.WriteLine($"                         DependentProducerBuilder<string,string> -> {Name(dep)}  (claim 1 of #135)");
using var ns = new ProducerBuilder<Null, string>(cfg).Build();
Console.WriteLine($"                         ProducerBuilder<Null,string>            -> {Name(ns)}  is IProducer<Null,string>: {((object)ns) is IProducer<Null, string>}");
try { ns.Produce("t135", new Message<Null, string> { Value = "v" }); Console.WriteLine("  Produce: ok"); }
catch (Exception ex) { Console.WriteLine($"  Produce threw {ex.GetType().Name}: {ex.Message}"); }
KafkaBuildInterceptor.Reset();
using var ns2 = new ProducerBuilder<Null, string>(cfg).Build();
Console.WriteLine($"after Reset();           ProducerBuilder<Null,string>            -> {Name(ns2)}  is IProducer<Null,string>: {((object)ns2) is IProducer<Null, string>}");
// Claim 2 of #135: no W3C ids on a produce entry, even under an ambient Activity.
var tracker = new KafkaTracker(new KafkaTrackingOptions { CurrentTestInfoFetcher = () => ("probe", "probe-id-2") });
using (var act = new System.Diagnostics.Activity("probe").Start())
{
    tracker.LogProduce(new KafkaOperationInfo(KafkaOperation.ProduceAsync, "orders", 0, 1), "Key: k, Value: {\"a\":1}");
    var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == "probe-id-2").ToArray();
    Console.WriteLine($"ambient Activity trace {act.TraceId}; entries: {logs.Length}; ActivityTraceId: [{string.Join(", ", logs.Select(l => l.ActivityTraceId ?? "null"))}]; Content: {logs[0].Content}");
}
