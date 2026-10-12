// audit B E5 (not repo code): what the plan's tiering check would read, against what the runtime obeyed.
// The observable is tiermit's M0: a Harmony prefix on shared generic code (Gen<string>.Make), 2,000 calls with pauses.
using System.Runtime.CompilerServices;
using HarmonyLib;

static string Show(object? o) => o is null ? "null" : $"{o.GetType().Name} \"{o}\"";
static string? Env(string n) => Environment.GetEnvironmentVariable(n);
Console.WriteLine($"== {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}  build={args.FirstOrDefault() ?? "?"}  Lib.Harmony={typeof(Harmony).Assembly.GetName().Version}");
foreach (var k in new[] { "System.Runtime.TieredCompilation", "System.Runtime.TieredCompilation.QuickJit", "System.Runtime.TieredCompilation.QuickJitForLoops", "System.Runtime.TieredPGO" })
    Console.WriteLine($"  AppContext.GetData(\"{k}\") = {Show(AppContext.GetData(k))}");
foreach (var k in new[] { "DOTNET_TieredCompilation", "COMPlus_TieredCompilation", "DOTNET_TC_QuickJit", "COMPlus_TC_QuickJit" })
    Console.WriteLine($"  env {k} = {Env(k) ?? "unset"}");

// Two ways a settings reader could combine the sources; the runtime itself (CLRConfig) lets an environment
// variable win over runtimeconfig.json and parses DWORD values as hexadecimal.
static bool? ParseAppContext(string key) => AppContext.GetData(key) switch
{
    null => null,
    bool b => b,
    string s when bool.TryParse(s, out var b) => b,
    _ => null,
};
static bool? ParseEnvLikeClrConfig(string name)
{
    var v = Environment.GetEnvironmentVariable("DOTNET_" + name) ?? Environment.GetEnvironmentVariable("COMPlus_" + name);
    if (v is null) return null;
    var s = v.Trim();
    if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
    return uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var n) ? n != 0 : null; // not a hex DWORD: the runtime ignores it
}
static bool? ParseEnvNaive(string name)
{
    var v = Environment.GetEnvironmentVariable("DOTNET_" + name) ?? Environment.GetEnvironmentVariable("COMPlus_" + name);
    return v is null ? null : !(v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase));
}
bool tcAppFirst = ParseAppContext("System.Runtime.TieredCompilation") ?? ParseEnvNaive("TieredCompilation") ?? true;
bool qjAppFirst = ParseAppContext("System.Runtime.TieredCompilation.QuickJit") ?? ParseEnvNaive("TC_QuickJit") ?? true;
bool tcEnvFirst = ParseEnvLikeClrConfig("TieredCompilation") ?? ParseAppContext("System.Runtime.TieredCompilation") ?? true;
bool qjEnvFirst = ParseEnvLikeClrConfig("TC_QuickJit") ?? ParseAppContext("System.Runtime.TieredCompilation.QuickJit") ?? true;
Console.WriteLine($"  reader A (runtimeconfig first, then env, 'false'/'0' = off): tiering can re-JIT = {tcAppFirst && qjAppFirst}");
Console.WriteLine($"  reader B (env first as CLRConfig does, hex DWORD, then runtimeconfig): tiering can re-JIT = {tcEnvFirst && qjEnvFirst}");

var target = typeof(Gen<string>).GetMethod(nameof(Gen<string>.Make))!;
new Harmony("audit.b.e5").Patch(target, prefix: new HarmonyMethod(typeof(Patches).GetMethod(nameof(Patches.Prefix))));
int intercepted = 0, firstBypass = -1;
for (var i = 0; i < 2000; i++)
{
    var before = Patches.Calls;
    var hit = Caller.CallStr() == "patched" && Patches.Calls > before;
    if (hit) intercepted++; else if (firstBypass < 0) firstBypass = i;
    if (i % 50 == 0) await Task.Delay(50);
}
Console.WriteLine($"  MEASURED: shared-generic patch intercepted {intercepted} of 2000 calls (first bypass {firstBypass}) -> runtime {(firstBypass < 0 ? "did NOT re-JIT (quick JIT off or tiering off)" : "re-JITted (tiering with quick JIT on)")}");

public class Gen<T> where T : class
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public string Make(T value) => "original:" + typeof(T).Name;
}
public static class Caller
{
    [MethodImpl(MethodImplOptions.NoInlining)] public static string CallStr() => new Gen<string>().Make("x");
}
public static class Patches
{
    public static int Calls;
    public static bool Prefix(object __instance, ref string __result)
    {
        Calls++;
        if (__instance.GetType().GetGenericArguments()[0] != typeof(string)) return true;
        __result = "patched";
        return false;
    }
}
