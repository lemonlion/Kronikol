// Probe 4 for #135 research (not repo code): KafkaBuildInterceptor's mechanism applied to
// DependentProducerBuilder<string,string>.Build(), and what it does to DependentProducerBuilder<Null,string>.
using System.Reflection;
using Confluent.Kafka;
using HarmonyLib;
using Kronikol.Extensions.Kafka;

Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
static string Name(object o) => $"{o.GetType().Name}<{string.Join(",", o.GetType().GetGenericArguments().Select(a => a.Name))}>";
KafkaTrackingInterceptor.EnableProducerTracking<string, string>(o => { o.CurrentTestInfoFetcher = () => ("probe", "probe-id"); });
var cfg = new ProducerConfig { BootstrapServers = "localhost:9092" };
using var owner = new ProducerBuilder<string, string>(cfg).Build();   // the handle's owner, unpatched
var harmony = new Harmony("probe.kafka135.d");
harmony.Patch(typeof(DependentProducerBuilder<string, string>).GetMethod("Build")!,
    postfix: new HarmonyMethod(typeof(Patches).GetMethod("WrapP", BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(typeof(string), typeof(string))));
using var d1 = new DependentProducerBuilder<string, string>(owner.Handle).Build();
Console.WriteLine($"DependentProducerBuilder<string,string> -> {Name(d1)}  ok={((object)d1) is IProducer<string, string>}");
using var d2 = new DependentProducerBuilder<Null, string>(owner.Handle).Build();
Console.WriteLine($"DependentProducerBuilder<Null,string>   -> {Name(d2)}  ok={((object)d2) is IProducer<Null, string>}");
using var d3 = new DependentProducerBuilder<int, string>(owner.Handle).Build();
Console.WriteLine($"DependentProducerBuilder<int,string>    -> {Name(d3)}  ok={((object)d3) is IProducer<int, string>}");
var tracked = KafkaTrackingInterceptor.WrapProducer(owner);
using var d4 = new DependentProducerBuilder<string, string>(tracked.Handle).Build();
Console.WriteLine($"from a TrackingKafkaProducer's Handle   -> {Name(d4)}  (Handle delegates to the inner producer)");
static class Patches
{
    static void WrapP<TKey, TValue>(ref IProducer<TKey, TValue> __result) => __result = KafkaTrackingInterceptor.WrapProducer(__result);
}
