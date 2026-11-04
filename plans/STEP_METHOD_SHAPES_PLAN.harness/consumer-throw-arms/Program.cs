using Kronikol.Tracking;
using Shouldly;
[assembly: TrackSteps]
[assembly: TrackAssertions]

var d = new Shop();
foreach (var (name, act) in new (string, Func<Task>)[]
{
    ("T1 switch discard", async () => d.T1(1, "a")),
    ("T2 ternary then throw", async () => d.T2(1, "a")),
    ("T3 ternary throw first", async () => d.T3(1, "a")),
    ("T4 coalesce", async () => d.T4(1, "a")),
    ("T5 plain switch", async () => d.T5(1, "a")),
    ("T6 plain ternary then throw", async () => d.T6(1, "a")),
    ("T7 plain ternary throw first", async () => d.T7(1, "a")),
    ("T8 plain coalesce", async () => d.T8(1, "a")),
    ("T9 throw mid method", async () => d.T9(1, "a")),
    ("T10 switch statement", async () => d.T10(1, "a")),
    ("T11 try return or throw", async () => d.T11(1, "a")),
    ("T12 lambda", async () => d.T12(1, "a")),
    ("T13 async switch", async () => await d.T13(1, "a")),
    ("T14 switch in a using", async () => d.T14(1, "a")),
    ("T15 expression switch, throw arm first", async () => d.T15(1, "a")),
})
{
    var id = Guid.NewGuid().ToString("N");
    using var scope = TestIdentityScope.Begin(id, id);
    try { await act(); } catch (Exception e) { Console.WriteLine($"{name}: threw {e.GetType().Name}"); }
    foreach (var step in StepCollector.GetSteps(id))
        Console.WriteLine($"{name}: {step.Status}, {(step.SubSteps ?? []).Count(s => s.IsAssertion)} note(s)");
}

public class Shop
{
    [Step] public void T1(int x, string s) { _ = x switch { 1 => s.ShouldNotBeNull(), _ => throw new ArithmeticException() }; }
    [Step] public void T2(int x, string s) { var v = x == 1 ? s.ShouldNotBeNull() : throw new ArithmeticException(); GC.KeepAlive(v); }
    [Step] public void T3(int x, string s) { var v = x != 1 ? throw new ArithmeticException() : s.ShouldNotBeNull(); GC.KeepAlive(v); }
    [Step] public void T4(int x, string s) { var v = s.ShouldNotBeNull() ?? throw new ArithmeticException(); GC.KeepAlive(v); }
    public string G5(int x, string s) => x switch { 1 => s.ShouldNotBeNull(), _ => throw new ArithmeticException() };
    [Step] public void T5(int x, string s) { G5(x, s); }
    public string G6(int x, string s) => x == 1 ? s.ShouldNotBeNull() : throw new ArithmeticException();
    [Step] public void T6(int x, string s) { G6(x, s); }
    public string G7(int x, string s) => x != 1 ? throw new ArithmeticException() : s.ShouldNotBeNull();
    [Step] public void T7(int x, string s) { G7(x, s); }
    public string G8(string s) => s.ShouldNotBeNull() ?? throw new ArithmeticException();
    [Step] public void T8(int x, string s) { G8(s); }
    public void G9(int x, string s) { if (x != 1) throw new ArithmeticException(s.ShouldNotBeNull()); s.ShouldBe("a"); }
    [Step] public void T9(int x, string s) { G9(x, s); }
    public void G10(int x, string s) { switch (x) { case 1: s.ShouldBe("a"); break; default: throw new ArithmeticException(s.ShouldNotBeNull()); } }
    [Step] public void T10(int x, string s) { G10(x, s); }
    public void G11(int x, string s) { try { if (x == 1) { s.ShouldBe("a"); return; } throw new ArithmeticException(); } finally { GC.KeepAlive(s); } }
    [Step] public void T11(int x, string s) { G11(x, s); }
    private readonly Func<int, string, string> _l = (x, s) => x == 1 ? s.ShouldNotBeNull() : throw new ArithmeticException();
    [Step] public void T12(int x, string s) { _l(x, s); }
    [Step] public async Task T13(int x, string s) { await Task.Yield(); _ = x switch { 1 => s.ShouldNotBeNull(), _ => throw new ArithmeticException() }; }
    [Step] public void T14(int x, string s) { using (new MemoryStream()) { _ = x switch { 1 => s.ShouldNotBeNull(), _ => throw new ArithmeticException() }; } }
    public string G15(int x, string s) => x switch { 2 => throw new ArithmeticException(), _ => s.ShouldNotBeNull() };
    [Step] public void T15(int x, string s) { G15(x, s); }
}
