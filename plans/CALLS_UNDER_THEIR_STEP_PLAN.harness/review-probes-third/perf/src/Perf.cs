using System.Diagnostics;
using Kronikol.Tracking;
using Kronikol.xUnit3;

namespace Probe;

public class Perf : DiagrammedComponentTest
{
    readonly ITestOutputHelper _o;
    public Perf(ITestOutputHelper o) { _o = o; }
    static void Say(string s) => File.AppendAllText("perf.log", s + Environment.NewLine);

    [Fact] public async Task P01_Sequential_steps_each_with_a_call()
    {
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 4000; i++) await Seq(i);
        Say($"P01 4000 sequential steps+calls: {sw.ElapsedMilliseconds} ms");
    }
    [Step] async Task Seq(int i) { await Api.CallNoDelay("seq-" + i); }

    [Fact] public async Task P02_Wide_parallel_kids_foreign_calls()
    {
        var sw = Stopwatch.StartNew();
        await Parent2();
        Say($"P02 800 open kids, one foreign call each: {sw.ElapsedMilliseconds} ms");
    }
    [Step] async Task Parent2()
    {
        var me = Api.Me;
        var gate = new TaskCompletionSource();
        var kids = Enumerable.Range(0, 800).Select(i => Kid2(i, gate.Task)).ToArray();
        var sw = Stopwatch.StartNew();
        Thread t;
        using (ExecutionContext.SuppressFlow())
        {
            t = new Thread(() => { for (var i = 0; i < 800; i++) Api.FromElsewhere(me.Name, me.Id, "foreign-" + i); });
            t.Start();
        }
        t.Join();
        Say($"   800 foreign calls with 800 kids open: {sw.ElapsedMilliseconds} ms");
        gate.SetResult();
        await Task.WhenAll(kids);
    }
    [Step] async Task Kid2(int i, Task gate) { await gate; }

    [Fact] public async Task P03_Deep_chain_calls()
    {
        var sw = Stopwatch.StartNew();
        await Deep(0);
        Say($"P03 depth: {sw.ElapsedMilliseconds} ms");
    }
    static readonly int Depth = int.Parse(Environment.GetEnvironmentVariable("PROBE_DEPTH") ?? "20");
    [Step] async Task Deep(int d) { await Api.CallNoDelay("deep-" + d); if (d < Depth) await Deep(d + 1); }
}
