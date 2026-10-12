// Probe 6 for #135 research (not repo code): does UnpatchAll (what KafkaBuildInterceptor.Reset() calls) give the
// other reference-type instantiations their own Build() back? Mode "before": <Null,string> built while patched,
// then after UnpatchAll. Mode "after": <Null,string> first built only after UnpatchAll. Mode "never": patch, unpatch,
// with no <string,string> build in between.
using System.Reflection;
using Confluent.Kafka;
using HarmonyLib;

var mode = args.Length > 0 ? args[0] : "before";
Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}  mode={mode}");
static string Name(object o) => $"{o.GetType().Name}<{string.Join(",", o.GetType().GetGenericArguments().Select(a => a.Name))}>";
var cfg = new ProducerConfig { BootstrapServers = "localhost:9092" };
var harmony = new Harmony("probe.kafka135.f");
harmony.Patch(typeof(ProducerBuilder<string, string>).GetMethod("Build")!,
    postfix: new HarmonyMethod(typeof(Patches).GetMethod("Noop", BindingFlags.NonPublic | BindingFlags.Static)!));
if (mode == "before") { using var n0 = new ProducerBuilder<Null, string>(cfg).Build(); Console.WriteLine($"patched:        <Null,string> -> {Name(n0)}"); }
if (mode != "never") { using var s0 = new ProducerBuilder<string, string>(cfg).Build(); Console.WriteLine($"patched:        <string,string> -> {Name(s0)}"); }
harmony.UnpatchAll("probe.kafka135.f");
Console.WriteLine($"Harmony.HasAnyPatches: {Harmony.HasAnyPatches("probe.kafka135.f")}");
using var n1 = new ProducerBuilder<Null, string>(cfg).Build();
Console.WriteLine($"after Unpatch:  <Null,string> -> {Name(n1)}  ok={((object)n1) is IProducer<Null, string>}");
using var s1 = new ProducerBuilder<string, string>(cfg).Build();
Console.WriteLine($"after Unpatch:  <string,string> -> {Name(s1)}");
static class Patches { static void Noop(object __result) { } }
