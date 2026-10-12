// Probe 3 for #135 research (not repo code). Control (no patch), and two reference-type pairs enabled at once,
// using KafkaBuildInterceptor's mechanism (a closed <TKey,TValue> postfix per enabled pair, 4.9.0
// KafkaBuildInterceptor.cs:134-153) with the published Kronikol.Extensions.Kafka 4.9.0 wrapper.
using System.Reflection;
using Confluent.Kafka;
using HarmonyLib;
using Kronikol.Extensions.Kafka;

var mode = args.Length > 0 ? args[0] : "control";
Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}  mode={mode}");
static string Name(object o) => $"{o.GetType().Name}<{string.Join(",", o.GetType().GetGenericArguments().Select(a => a.Name))}>";
var harmony = new Harmony("probe.kafka135.c");
void Enable<TKey, TValue>()
{
    KafkaTrackingInterceptor.EnableProducerTracking<TKey, TValue>(o => { o.CurrentTestInfoFetcher = () => ("probe", "probe-id"); });
    harmony.Patch(typeof(ProducerBuilder<TKey, TValue>).GetMethod("Build")!,
        postfix: new HarmonyMethod(typeof(Patches).GetMethod("WrapP", BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(typeof(TKey), typeof(TValue))));
}
if (mode == "two") { Enable<string, string>(); Enable<string, byte[]>(); }
if (mode == "nullfirst") { Enable<Null, string>(); }
var cfg = new ProducerConfig { BootstrapServers = "localhost:9092" };
using var a = new ProducerBuilder<string, string>(cfg).Build();
Console.WriteLine($"ProducerBuilder<string,string>  -> {Name(a)}  ok={((object)a) is IProducer<string, string>}");
using var b = new ProducerBuilder<string, byte[]>(cfg).Build();
Console.WriteLine($"ProducerBuilder<string,byte[]>  -> {Name(b)}  ok={((object)b) is IProducer<string, byte[]>}");
using var n = new ProducerBuilder<Null, string>(cfg).Build();
Console.WriteLine($"ProducerBuilder<Null,string>    -> {Name(n)}  ok={((object)n) is IProducer<Null, string>}");
Console.WriteLine($"postfix calls: {Patches.Calls}");

static class Patches
{
    public static int Calls;
    static void WrapP<TKey, TValue>(ref IProducer<TKey, TValue> __result) { Calls++; __result = KafkaTrackingInterceptor.WrapProducer(__result); }
}
