using System.Runtime.CompilerServices;
using HarmonyLib;
using Xunit;

public class QuickJitTest
{
    [Fact]
    public async Task Measures_whether_the_test_host_obeys_the_package_props()
    {
        var target = typeof(Gen<string>).GetMethod(nameof(Gen<string>.Make))!;
        new Harmony("audit.b.e5.test").Patch(target, prefix: new HarmonyMethod(typeof(Patches).GetMethod(nameof(Patches.Prefix))));
        int intercepted = 0, firstBypass = -1;
        for (var i = 0; i < 2000; i++)
        {
            var hit = Caller.CallStr() == "patched";
            if (hit) intercepted++; else if (firstBypass < 0) firstBypass = i;
            if (i % 50 == 0) await Task.Delay(50);
        }
        var line = $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} process={Environment.ProcessPath} " +
                   $"AppContext QuickJit={AppContext.GetData("System.Runtime.TieredCompilation.QuickJit") ?? "null"} intercepted={intercepted}/2000 firstBypass={firstBypass}";
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "e5-result.txt"), line);
    }
}

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
    public static bool Prefix(object __instance, ref string __result)
    {
        if (__instance.GetType().GetGenericArguments()[0] != typeof(string)) return true;
        __result = "patched";
        return false;
    }
}
