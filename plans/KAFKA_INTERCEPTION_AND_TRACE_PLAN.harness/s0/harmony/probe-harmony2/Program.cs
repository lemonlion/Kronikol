// Probe 2 for #135 research (not repo code): what a ProducerBuilder<Null,string> / ConsumerBuilder<Null,string>
// gives back once ProducerBuilder<string,string>.Build / ConsumerBuilder<string,string>.Build carry a Harmony
// postfix, (a) with a postfix that does nothing, (b) with Kronikol's wrap, and what using the result does.
using System.Reflection;
using Confluent.Kafka;
using HarmonyLib;
using Kronikol.Extensions.Kafka;

var mode = args.Length > 0 ? args[0] : "noop";
Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}  mode={mode}");
KafkaTrackingInterceptor.EnableProducerTracking<string, string>(o => { o.CurrentTestInfoFetcher = () => ("probe", "probe-id"); });
KafkaTrackingInterceptor.EnableConsumerTracking<string, string>(o => { o.CurrentTestInfoFetcher = () => ("probe", "probe-id"); });
var harmony = new Harmony("probe.kafka135.b");
var pName = mode == "wrap" ? "WrapP" : "NoopP";
var cName = mode == "wrap" ? "WrapC" : "NoopC";
harmony.Patch(typeof(ProducerBuilder<string, string>).GetMethod("Build")!,
    postfix: new HarmonyMethod(typeof(Patches).GetMethod(pName, BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(typeof(string), typeof(string))));
harmony.Patch(typeof(ConsumerBuilder<string, string>).GetMethod("Build")!,
    postfix: new HarmonyMethod(typeof(Patches).GetMethod(cName, BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(typeof(string), typeof(string))));

static string Name(object o) => $"{o.GetType().Name}<{string.Join(",", o.GetType().GetGenericArguments().Select(a => a.Name))}>";

var pcfg = new ProducerConfig { BootstrapServers = "localhost:9092", MessageTimeoutMs = 1500 };
using (var p = new ProducerBuilder<Null, string>(pcfg).Build())
{
    Console.WriteLine($"ProducerBuilder<Null,string>.Build() -> {Name(p)}; is IProducer<Null,string>: {((object)p) is IProducer<Null, string>}");
    try
    {
        p.Produce("t135", new Message<Null, string> { Value = "v" }, r => Console.WriteLine($"  delivery report: {r.Error.Code}"));
        Console.WriteLine("  Produce(...) returned without throwing");
        p.Flush(TimeSpan.FromSeconds(3));
    }
    catch (Exception ex) { Console.WriteLine($"  Produce(...) threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }
}
var ccfg = new ConsumerConfig { BootstrapServers = "localhost:9092", GroupId = "g135" };
using (var c = new ConsumerBuilder<Null, string>(ccfg).Build())
{
    Console.WriteLine($"ConsumerBuilder<Null,string>.Build() -> {Name(c)}; is IConsumer<Null,string>: {((object)c) is IConsumer<Null, string>}");
    try { c.Subscribe("t135"); Console.WriteLine("  Subscribe returned without throwing"); }
    catch (Exception ex) { Console.WriteLine($"  Subscribe threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }
    try { var r = c.Consume(TimeSpan.FromMilliseconds(300)); Console.WriteLine($"  Consume returned {(r is null ? "null" : "a result")}"); }
    catch (Exception ex) { Console.WriteLine($"  Consume threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }
}
harmony.UnpatchAll("probe.kafka135.b");
using (var p2 = new ProducerBuilder<Null, string>(pcfg).Build())
    Console.WriteLine($"after UnpatchAll: ProducerBuilder<Null,string>.Build() -> {Name(p2)}");

static class Patches
{
    static void NoopP<TKey, TValue>(ref IProducer<TKey, TValue> __result) { }
    static void NoopC<TKey, TValue>(ref IConsumer<TKey, TValue> __result) { }
    static void WrapP<TKey, TValue>(ref IProducer<TKey, TValue> __result) => __result = KafkaTrackingInterceptor.WrapProducer(__result);
    static void WrapC<TKey, TValue>(ref IConsumer<TKey, TValue> __result) => __result = KafkaTrackingInterceptor.WrapConsumer(__result);
}
