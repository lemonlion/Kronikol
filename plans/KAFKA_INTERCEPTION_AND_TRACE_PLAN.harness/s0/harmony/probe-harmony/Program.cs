// Probe for #135 research (not repo code). Replicates KafkaBuildInterceptor's patch (4.9.0,
// src/Kronikol.Extensions.Kafka.BuildInterception/KafkaBuildInterceptor.cs:134-153) with the published
// Kronikol.Extensions.Kafka 4.9.0 wrapper, and asks: (1) is DependentProducerBuilder.Build() intercepted?
// (2) does patching ProducerBuilder<string,string>.Build() also run the <string,string> postfix for other
// reference-type instantiations (shared generic code)?
using System.Reflection;
using Confluent.Kafka;
using HarmonyLib;
using Kronikol.Extensions.Kafka;

Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
KafkaTrackingInterceptor.EnableProducerTracking<string, string>(new KafkaTrackingOptions
{
    ServiceName = "Kafka", CallerName = "Probe", CurrentTestInfoFetcher = () => ("probe", "probe-id"),
});
var harmony = new Harmony("probe.kafka135");
var original = typeof(ProducerBuilder<string, string>).GetMethod(nameof(ProducerBuilder<string, string>.Build))!;
var postfix = typeof(Patches).GetMethod("ProducerBuildPostfix", BindingFlags.NonPublic | BindingFlags.Static)!
    .MakeGenericMethod(typeof(string), typeof(string));
harmony.Patch(original, postfix: new HarmonyMethod(postfix));

var cfg = new ProducerConfig { BootstrapServers = "localhost:9092" };

void Report(string label, object p, bool isExpectedInterface)
    => Console.WriteLine($"{label,-38} -> {p.GetType().Name}<{string.Join(",", p.GetType().GetGenericArguments().Select(a => a.Name))}>  implements its own IProducer: {isExpectedInterface}  postfix calls so far: {Patches.Calls}");

using var ss = new ProducerBuilder<string, string>(cfg).Build();
Report("ProducerBuilder<string,string>", ss, ss is IProducer<string, string>);

using var dep = new DependentProducerBuilder<string, string>(ss.Handle).Build();
Report("DependentProducerBuilder<string,string>", dep, dep is IProducer<string, string>);

using var ns = new ProducerBuilder<Null, string>(cfg).Build();
Report("ProducerBuilder<Null,string>", ns, ((object)ns) is IProducer<Null, string>);

using var sb = new ProducerBuilder<string, byte[]>(cfg).Build();
Report("ProducerBuilder<string,byte[]>", sb, ((object)sb) is IProducer<string, byte[]>);

using var isP = new ProducerBuilder<int, string>(cfg).SetKeySerializer(Serializers.Int32).Build();
Report("ProducerBuilder<int,string>", isP, ((object)isP) is IProducer<int, string>);

foreach (var s in Patches.Seen) Console.WriteLine("  postfix saw: " + s);
harmony.UnpatchAll("probe.kafka135");

static class Patches
{
    public static int Calls;
    public static readonly List<string> Seen = new();
    static void ProducerBuildPostfix<TKey, TValue>(ref IProducer<TKey, TValue> __result)
    {
        Calls++;
        Seen.Add($"postfix<{typeof(TKey).Name},{typeof(TValue).Name}> given {__result.GetType().Name}<{string.Join(",", __result.GetType().GetGenericArguments().Select(a => a.Name))}>");
        __result = KafkaTrackingInterceptor.WrapProducer(__result);
    }
}
