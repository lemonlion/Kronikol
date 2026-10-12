// Probe 5 for #135 research (not repo code): can a Harmony PREFIX that skips the original body and builds the
// producer for the instance's runtime type avoid the shared-generic-code type confusion probes 1 to 4 show?
using System.Reflection;
using Confluent.Kafka;
using HarmonyLib;
using Kronikol.Extensions.Kafka;

Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
static string Name(object o) => $"{o.GetType().Name}<{string.Join(",", o.GetType().GetGenericArguments().Select(a => a.Name))}>";
KafkaTrackingInterceptor.EnableProducerTracking<string, string>(o => { o.CurrentTestInfoFetcher = () => ("probe", "probe-id"); });
var harmony = new Harmony("probe.kafka135.e");
harmony.Patch(typeof(ProducerBuilder<string, string>).GetMethod("Build")!,
    prefix: new HarmonyMethod(typeof(Patches).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!));
harmony.Patch(typeof(DependentProducerBuilder<string, string>).GetMethod("Build")!,
    prefix: new HarmonyMethod(typeof(Patches).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!));
var cfg = new ProducerConfig { BootstrapServers = "localhost:9092", MessageTimeoutMs = 1000 };
using var a = new ProducerBuilder<string, string>(cfg).Build();
Console.WriteLine($"ProducerBuilder<string,string>          -> {Name(a)}  ok={((object)a) is IProducer<string, string>}");
using var n = new ProducerBuilder<Null, string>(cfg).Build();
Console.WriteLine($"ProducerBuilder<Null,string>            -> {Name(n)}  ok={((object)n) is IProducer<Null, string>}");
using var b = new ProducerBuilder<string, byte[]>(cfg).Build();
Console.WriteLine($"ProducerBuilder<string,byte[]>          -> {Name(b)}  ok={((object)b) is IProducer<string, byte[]>}");
using var d = new DependentProducerBuilder<string, string>(a.Handle).Build();
Console.WriteLine($"DependentProducerBuilder<string,string> -> {Name(d)}  ok={((object)d) is IProducer<string, string>}");
using var dn = new DependentProducerBuilder<Null, string>(a.Handle).Build();
Console.WriteLine($"DependentProducerBuilder<Null,string>   -> {Name(dn)}  ok={((object)dn) is IProducer<Null, string>}");
try { n.Produce("t135", new Message<Null, string> { Value = "v" }); Console.WriteLine("  Produce on <Null,string>: no exception (queued; no broker)"); }
catch (Exception ex) { Console.WriteLine($"  Produce on <Null,string> threw {ex.GetType().Name}"); }

static class Patches
{
    static readonly Type ProducerOpen = typeof(IProducer<,>).Assembly.GetType("Confluent.Kafka.Producer`2", throwOnError: true)!;
    static readonly MethodInfo WrapOpen = typeof(KafkaTrackingInterceptor).GetMethod(nameof(KafkaTrackingInterceptor.WrapProducer))!;
    // __instance is the real builder (its runtime type carries the real type arguments); skip the shared body.
    static bool Prefix(object __instance, ref object __result)
    {
        var args = __instance.GetType().GetGenericArguments();
        var producer = Activator.CreateInstance(ProducerOpen.MakeGenericType(args),
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, [__instance], null)!;
        __result = WrapOpen.MakeGenericMethod(args).Invoke(null, [producer])!;
        return false;
    }
}
