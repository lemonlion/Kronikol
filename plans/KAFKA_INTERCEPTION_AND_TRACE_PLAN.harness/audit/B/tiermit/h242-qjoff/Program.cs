// Probe for #135 plan (kronikol-cc): can a Harmony prefix on SHARED canonical generic code survive tier-up?
// Mitigations tried: M0 none (control), M1 RuntimeHelpers.PrepareMethod before patching, M2 warm the method to
// tier 1 before patching, M3 re-patch after the first bypass, M4 re-patch on a schedule (every 50 calls),
// M5 enable-time warm-up on a sentinel instantiation that re-patches until 600 consecutive calls were intercepted,
// then 20,000 real calls.
using System.Runtime.CompilerServices;
using HarmonyLib;

var mode = args.Length > 0 ? args[0] : "M0";
Console.WriteLine($"== mode={mode} runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} " +
                  $"TieredCompilation={Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default"} QuickJit={Environment.GetEnvironmentVariable("DOTNET_TC_QuickJit") ?? "default"}");

var target = typeof(Gen<string>).GetMethod(nameof(Gen<string>.Make))!;
var prefix = new HarmonyMethod(typeof(Patches).GetMethod(nameof(Patches.Prefix)));
var harmony = new Harmony("probe.tiermit");

if (mode == "M1")
    RuntimeHelpers.PrepareMethod(target.MethodHandle, [typeof(string).TypeHandle]);
if (mode == "M2")
{
    // Warm the canonical code: calls from another reference-type instantiation share its call count.
    for (var i = 0; i < 200; i++) Caller.CallObj();
    await Task.Delay(1500);
}
harmony.Patch(target, prefix: prefix);

if (mode == "M5")
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    int streak = 0, warmCalls = 0, warmRepatches = 0;
    while (streak < 600 && sw.ElapsedMilliseconds < 20000)
    {
        var before = Patches.Calls;
        var r = Caller.CallSentinel();
        warmCalls++;
        if (Patches.Calls > before && r == "sentinel") streak++;
        else
        {
            streak = 0;
            harmony.Unpatch(target, HarmonyPatchType.Prefix, harmony.Id);
            harmony.Patch(target, prefix: prefix);
            warmRepatches++;
        }
        if (warmCalls % 50 == 0) await Task.Delay(20);
    }
    Console.WriteLine($"warm-up: calls={warmCalls} repatches={warmRepatches} elapsedMs={sw.ElapsedMilliseconds}");
}

var total = mode == "M5" ? 20000 : 2000;
int intercepted = 0, bypassed = 0, firstBypass = -1, repatches = 0;
for (var i = 0; i < total; i++)
{
    var before = Patches.Calls;
    var r = Caller.CallStr();
    var hit = Patches.Calls > before && r == "patched";
    if (hit) intercepted++; else { bypassed++; if (firstBypass < 0) firstBypass = i; }
    if (!hit && mode == "M3" && repatches < 5)
    {
        harmony.Unpatch(target, HarmonyPatchType.Prefix, harmony.Id);
        harmony.Patch(target, prefix: prefix);
        repatches++;
    }
    if (mode == "M4" && i % 50 == 49)
    {
        harmony.Unpatch(target, HarmonyPatchType.Prefix, harmony.Id);
        harmony.Patch(target, prefix: prefix);
        repatches++;
    }
    if (i % 50 == 0) await Task.Delay(mode == "M5" ? 5 : 50);
}
// Another instantiation sharing the code: "original:String" means Harmony's replacement ran the closed-over body.
Console.WriteLine($"intercepted={intercepted} bypassed={bypassed} firstBypass={firstBypass} repatches={repatches} objCall={Caller.CallObj()}");

public class Gen<T> where T : class
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public string Make(T value) => "original:" + typeof(T).Name;
}

public sealed class Sentinel { }

public static class Caller
{
    [MethodImpl(MethodImplOptions.NoInlining)] public static string CallStr() => new Gen<string>().Make("x");
    [MethodImpl(MethodImplOptions.NoInlining)] public static string CallObj() => new Gen<object>().Make(new object());
    [MethodImpl(MethodImplOptions.NoInlining)] public static string CallSentinel() => new Gen<Sentinel>().Make(new Sentinel());
}

public static class Patches
{
    public static int Calls;

    public static bool Prefix(object __instance, ref string __result)
    {
        Calls++;
        var t = __instance.GetType().GetGenericArguments()[0];
        if (t == typeof(Sentinel)) { __result = "sentinel"; return false; }
        if (t != typeof(string)) return true;
        __result = "patched";
        return false;
    }
}
