using System.Runtime.CompilerServices;
using System.Text;
using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using Rev4;
using Shouldly;
using Xunit;

[assembly: TrackSteps]
[assembly: TrackAssertions]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Rev4.Weave;

public class TestRun : DiagrammedTestRun, IDisposable
{
    public void Dispose()
    {
        EndRunTime = DateTime.UtcNow;
        Skeleton.Write("rev4-weave" + (Environment.GetEnvironmentVariable("PROBE_CONFIG") ?? ""));
        Lines.Flush("rev4-weave-lines" + (Environment.GetEnvironmentVariable("PROBE_CONFIG") ?? ""));
    }
}

[CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Collection : ICollectionFixture<TestRun> { }

public static class Lines
{
    private static readonly StringBuilder Out = new();
    public static void Add(string shape, string text) { lock (Out) Out.AppendLine($"## {shape}\n{text}"); }
    public static void Flush(string name)
    {
        var dir = Environment.GetEnvironmentVariable("PHASE_PROBE_OUT") ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name + ".txt"), string.Join("\n", Out.ToString().Split("## ").OrderBy(s => s, StringComparer.Ordinal).Select(s => "## " + s)));
    }
}

[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Shapes : DiagrammedComponentTest
{
    private readonly HttpClient _http = new(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new Answer() });
    public bool Always => true;
    public int Value = 1;

    private async Task Call(string label) => (await _http.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();
    [MethodImpl(MethodImplOptions.NoInlining)] private static void Boom(string m = "boom") => throw new InvalidOperationException(m);
    private static int _depth;

    private static string FramesOf(Exception e)
    {
        var lines = (e.StackTrace ?? "").Replace("\r", "").Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Contains("Rev4.Weave") && !l.Contains("Record("))
            .Select(l =>
            {
                var at = l.IndexOf(" in ");
                var file = at >= 0 ? l[(at + 4)..] : "(no file)";
                var slash = file.LastIndexOfAny(new[] { '/', (char)92 });
                return (at >= 0 ? l[..at] : l).Replace("at Rev4.Weave.", "") + " @ " + (slash >= 0 ? file[(slash + 1)..] : file);
            });
        return string.Join("\n", lines);
    }

    private static async Task Record(string shape, Func<Task> run)
    {
        try { await run(); Lines.Add(shape, "(no exception)"); }
        catch (Exception e) { Lines.Add(shape, $"{e.GetType().Name}: {e.Message.Replace("\r", "").Split('\n')[0]}\n{FramesOf(e)}"); }
    }

    // ---- W01 to W09: Task stubs that throw before they have a task
    [GivenStep] public Task W01_ExprThrow() => throw new InvalidOperationException("w01");

    [GivenStep]
    public Task W02_CatchAtEnd()
    {
        try { Boom("w02-inner"); return Task.CompletedTask; }
        catch (InvalidOperationException e) { throw new ArgumentException("w02-outer", e); }
    }

    [GivenStep]
    public Task W03_FinallyAtEnd()
    {
        try { throw new InvalidOperationException("w03"); }
        finally { _depth = 0; }
    }

    [GivenStep]
    public Task W04_FilterAtEnd()
    {
        try { throw new InvalidOperationException("w04"); }
        catch (InvalidOperationException e) when (e.Message == "w04") { throw new ArgumentException("w04-outer", e); }
    }

    [GivenStep]
    public Task W05_Switch(int n)
    {
        switch (n)
        {
            case 0: return Task.CompletedTask;
            case 1: return Task.Delay(1);
            case 2: Boom("w05"); return Task.CompletedTask;
            default: return Task.FromException(new InvalidOperationException("w05-faulted"));
        }
    }

    [GivenStep]
    public Task<int> W06_Using(int n)
    {
        using var ms = new MemoryStream();
        if (n == 1) Boom("w06");
        return Task.FromResult(n);
    }

    [GivenStep]
    public Task W07_Lock(object gate)
    {
        lock (gate) { return Task.Delay(1); }
    }

    [GivenStep]
    public Task W08_Generic<T>(T value) where T : class => value is null ? throw new InvalidOperationException("w08") : Task.FromResult(value);

    [GivenStep]
    public Task<List<T>> W09_GenericResult<T>(T value) => Task.FromResult(new List<T> { value });

    [GivenStep]
    public Task W10_Stackalloc(int n)
    {
        Span<int> s = stackalloc int[4];
        s[0] = n;
        if (s[0] == 1) Boom("w10");
        return Task.CompletedTask;
    }

    [GivenStep(SkipIf = nameof(Always), SkipReason = "skip")]
    public Task W11_SkipIfTaskStub() { try { return Task.Delay(1); } finally { _depth = 0; } }

    [GivenStep] public Task W12_NullTask() => null!;

    [GivenStep] public Task W13_Ternary(bool a) => a ? Call("w13-a") : Call("w13-b");

    [WhenStep] public Task W14_Outer(int n) => n == 0 ? Task.CompletedTask : W14_Inner(n);
    [GivenStep] public Task W14_Inner(int n) { Boom("w14"); return Task.CompletedTask; }

    [GivenStep]
    public unsafe Task W15_Fixed(byte[] buffer)
    {
        fixed (byte* p = buffer) { *p = 1; return Task.CompletedTask; }
    }

    [GivenStep] public Task W16_AlreadyFaulted() => Task.FromException<int>(new InvalidOperationException("w16"));

    // ---- line numbers: the one statement that throws is on a line of its own
    [ThenStep]
    public Task L01_TaskStubThrowsFromCallee()
    {
        var a = 1;
        Boom($"l01-{a}");                         // LINE-L01
        return Task.CompletedTask;
    }

    [ThenStep]
    public Task L02_TaskStubThrowsHere()
    {
        var a = 1;
        if (a == 1) throw new InvalidOperationException("l02");   // LINE-L02
        return Task.CompletedTask;
    }

    [ThenStep]
    public void L03_SyncStep()
    {
        var a = 1;
        Boom($"l03-{a}");                         // LINE-L03
    }

    [ThenStep]
    public async Task L04_AsyncStep()
    {
        await Task.Yield();
        Boom("l04");                              // LINE-L04
    }

    // ---- with assertion tracking on the same stub
    [ThenStep]
    public Task A01_AssertionInTaskStubFails()
    {
        Value.ShouldBe(2);                        // LINE-A01
        return Task.CompletedTask;
    }

    [ThenStep]
    public Task A02_AssertionInTaskStubPasses()
    {
        Value.ShouldBe(1);
        return Task.CompletedTask;
    }

    [ThenStep] public Task A03_AwaitableAssertionExprBody() => Should.ThrowAsync<InvalidOperationException>(() => Task.CompletedTask);

    [ThenStep]
    public Task A04_TwoAssertionsThenReturnTask()
    {
        Value.ShouldBe(1);
        Value.ShouldBeGreaterThan(5);
        return Task.CompletedTask;
    }

    // ---- sync step, all paths throw (HandlerEnd null at the sync wrapper)
    [GivenStep]
    public void S01_SyncCatchAtEnd()
    {
        try { Boom("s01-inner"); }
        catch (InvalidOperationException e) { throw new ArgumentException("s01-outer", e); }
    }


    // ---- sync step, every path ends in a throw, so no ret follows the handler
    [GivenStep]
    public void S02_SyncAllPathsThrow()
    {
        try { throw new InvalidOperationException("s02-inner"); }
        catch (InvalidOperationException e) { throw new ArgumentException("s02-outer", e); }
    }

    [Fact] public Task T_S02() => Record("S02", () => { S02_SyncAllPathsThrow(); return Task.CompletedTask; });

    [Fact] public Task T_W01() => Record("W01", () => W01_ExprThrow());
    [Fact] public Task T_W02() => Record("W02", () => W02_CatchAtEnd());
    [Fact] public Task T_W03() => Record("W03", () => W03_FinallyAtEnd());
    [Fact] public Task T_W04() => Record("W04", () => W04_FilterAtEnd());
    [Fact] public async Task T_W05() { for (var i = 0; i < 4; i++) { var n = i; await Record($"W05-{n}", () => W05_Switch(n)); } }
    [Fact] public async Task T_W06() { await Record("W06-0", () => W06_Using(0)); await Record("W06-1", () => W06_Using(1)); }
    [Fact] public Task T_W07() => Record("W07", () => W07_Lock(new object()));
    [Fact] public async Task T_W08() { await Record("W08", () => W08_Generic("x")); await Record("W08-null", () => W08_Generic<string>(null!)); }
    [Fact] public Task T_W09() => Record("W09", () => W09_GenericResult(1));
    [Fact] public async Task T_W10() { await Record("W10-0", () => W10_Stackalloc(0)); await Record("W10-1", () => W10_Stackalloc(1)); }
    [Fact] public Task T_W11() => Record("W11", () => W11_SkipIfTaskStub());
    [Fact] public Task T_W12() => Record("W12", () => W12_NullTask());
    [Fact] public async Task T_W13() { await Record("W13-a", () => W13_Ternary(true)); await Record("W13-b", () => W13_Ternary(false)); }
    [Fact] public Task T_W14() => Record("W14", () => W14_Outer(1));
    [Fact] public Task T_W15() => Record("W15", () => W15_Fixed(new byte[1]));
    [Fact] public Task T_W16() => Record("W16", () => W16_AlreadyFaulted());
    [Fact] public Task T_L01() => Record("L01", () => L01_TaskStubThrowsFromCallee());
    [Fact] public Task T_L02() => Record("L02", () => L02_TaskStubThrowsHere());
    [Fact] public Task T_L03() => Record("L03", () => { L03_SyncStep(); return Task.CompletedTask; });
    [Fact] public Task T_L04() => Record("L04", () => L04_AsyncStep());
    [Fact] public Task T_A01() => Record("A01", () => A01_AssertionInTaskStubFails());
    [Fact] public Task T_A02() => Record("A02", () => A02_AssertionInTaskStubPasses());
    [Fact] public Task T_A03() => Record("A03", () => A03_AwaitableAssertionExprBody());
    [Fact] public Task T_A04() => Record("A04", () => A04_TwoAssertionsThenReturnTask());
    [Fact] public Task T_S01() => Record("S01", () => { S01_SyncCatchAtEnd(); return Task.CompletedTask; });
}
