// Every method shape the probe tries, and what each should do once the weaver handles it.
//
// A shape names what SHOULD happen, so a run on main reads as "what is wrong today", and the same table is the
// prototype's acceptance. `Refused` marks a method the weaver cannot bracket truthfully: it should be left as the
// compiler wrote it, run normally and draw no step, and the build should say so.

namespace StepShapes;

public static partial class Shapes
{
    public const string Attributes = """
        using System;
        namespace Kronikol.Tracking
        {
            [AttributeUsage(AttributeTargets.Assembly)] internal sealed class TrackStepsAttribute : Attribute { }
            [AttributeUsage(AttributeTargets.Assembly)] internal sealed class TrackAssertionsAttribute : Attribute { }
            [AttributeUsage(AttributeTargets.Method)] internal sealed class SuppressAssertionTrackingAttribute : Attribute { }
            [AttributeUsage(AttributeTargets.Method)] internal sealed class GivenStepAttribute : Attribute { public string Description { get; set; } public string SkipIf { get; set; } public string SkipReason { get; set; } }
            [AttributeUsage(AttributeTargets.Method)] internal sealed class WhenStepAttribute : Attribute { public string Description { get; set; } public string SkipIf { get; set; } public string SkipReason { get; set; } }
            [AttributeUsage(AttributeTargets.Method)] internal sealed class ThenStepAttribute : Attribute { public string Description { get; set; } public string SkipIf { get; set; } public string SkipReason { get; set; } }
            [AttributeUsage(AttributeTargets.Method)] internal sealed class ButStepAttribute : Attribute { public string Description { get; set; } public string SkipIf { get; set; } public string SkipReason { get; set; } }
            [AttributeUsage(AttributeTargets.Method)] internal sealed class ButWhenStepAttribute : Attribute { public string Description { get; set; } public string SkipIf { get; set; } public string SkipReason { get; set; } }
            [AttributeUsage(AttributeTargets.Method)] internal sealed class StepAttribute : Attribute { public string Description { get; set; } public string SkipIf { get; set; } public string SkipReason { get; set; } }
        }
        """;

    private const string Header = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using System.Threading.Tasks.Sources;
        using System.Runtime.CompilerServices;
        using Kronikol.Tracking;
        #pragma warning disable CS1998, CS0162, CS8321, CS0649, CS0169, CS0414

        [assembly: TrackSteps]
        //ASSEMBLY

        public record Customer(string Name);
        public struct Big { public int A, B, C, D, E; public override string ToString() => $"Big({A})"; }
        public enum Colour { Red, Blue }

        """;

    // M's body in each shape does some work and calls Inner(), so a bracket that closes early is seen twice: M's time
    // is short, and Inner is M's sibling, not its child.
    private const string T = "M:Passed[Inner:Passed]";
    private static readonly string[] ReturnsByRef = ["ReturnPtrToStack"];
    private static readonly string[] IsPointer = ["UnmanagedPointer"];
    private const string Inner = "\n    [Step] public void Inner() { }\n";

    private static Shape S(string id, string family, string members, string driver,
        string? value = null, string? tree = T, int minMs = 50, string? throws = null, bool refused = false,
        string? extra = null, bool sync = false, string? warning = null, bool noInner = false, int settleMs = 0,
        bool assertions = false, bool assertionsFirst = false, bool tailCall = false, string[]? allowIl = null, bool branchToRet = false, bool noWarning = false)
    {
        var signature = sync ? "public static object Run()" : "public static async Task<object> Run()";
        var source = (assertions ? "using Shouldly;\n" : "") + Header.Replace("//ASSEMBLY", assertions ? "[assembly: TrackAssertions]" : "")
            + (extra ?? "") + "\n"
            + (members.Contains("class Steps") || members.Contains("struct Steps") ? members : "public class Steps\n{\n    " + members + (noInner ? "" : Inner) + "}\n")
            + "public static class Driver\n{\n    public static TaskCompletionSource Done = new();\n    " + signature + "\n    {\n        " + driver + "\n    }\n}\n";
        return new Shape
        {
            Id = id, Family = family, Source = source, Value = value, Throws = throws,
            Tree = tree, MinMs = minMs, Refused = refused, ExpectWarning = warning,
            SettleMs = settleMs, Assertions = assertions, AssertionsFirst = assertionsFirst, TailCall = tailCall, BranchToRet = branchToRet, NoWarning = noWarning, AllowIl = allowIl ?? [],
        };
    }

    // type, a value of it in C#, and how that value prints.
    private static readonly (string Name, string Type, string Literal, string Printed, string Default)[] ValueTypes =
    [
        ("byte", "byte", "(byte)7", "7", "0"),
        ("sbyte", "sbyte", "(sbyte)7", "7", "0"),
        ("short", "short", "(short)7", "7", "0"),
        ("ushort", "ushort", "(ushort)7", "7", "0"),
        ("int", "int", "7", "7", "0"),
        ("uint", "uint", "7u", "7", "0"),
        ("long", "long", "7L", "7", "0"),
        ("ulong", "ulong", "7UL", "7", "0"),
        ("float", "float", "7f", "7", "0"),
        ("double", "double", "7d", "7", "0"),
        ("decimal", "decimal", "7m", "7", "0"),
        ("char", "char", "'7'", "7", "\0"),
        ("bool", "bool", "true", "True", "False"),
        ("nint", "nint", "(nint)7", "7", "0"),
        ("nuint", "nuint", "(nuint)7", "7", "0"),
        ("string", "string", "\"7\"", "7", "(null)"),
        ("object", "object", "\"7\"", "7", "(null)"),
        ("array", "int[]", "new[] { 7 }", "System.Int32[]", "(null)"),
        ("class", "Customer", "new Customer(\"7\")", "Customer { Name = 7 }", "(null)"),
        ("guid", "Guid", "new Guid(\"00000000-0000-0000-0000-000000000007\")", "00000000-0000-0000-0000-000000000007", "00000000-0000-0000-0000-000000000000"),
        ("datetime", "DateTime", "new DateTime(7, 7, 7, 0, 0, 0, DateTimeKind.Utc).ToUniversalTime()", "07/07/0007 00:00:00", "01/01/0001 00:00:00"),
        ("timespan", "TimeSpan", "TimeSpan.FromSeconds(7)", "00:00:07", "00:00:00"),
        ("enum", "Colour", "Colour.Blue", "Blue", "Red"),
        ("nullable", "int?", "(int?)7", "7", "(null)"),
        ("tuple", "(int, string)", "(7, \"7\")", "(7, 7)", "(0, )"),
        ("bigstruct", "Big", "new Big { A = 7 }", "Big(7)", "Big(0)"),
    ];

    // Boxed null prints as nothing through ToString in the probe; "(null)" here means the driver's own Print.
    private const string PrintHelper = """
        public static class P { public static string Of(object v) => v is null ? "(null)" : v.ToString(); }

        """;

    public static IReadOnlyList<Shape> All { get; } = Build();

    private static List<Shape> Build()
    {
        var all = new List<Shape>();
        all.AddRange(SyncValues());
        all.AddRange(ControlFlow());
        all.AddRange(SpecialReturns());
        all.AddRange(TaskShapes());
        all.AddRange(ValueTaskShapes());
        all.AddRange(Deferred());
        all.AddRange(Parameters());
        all.AddRange(Kinds());
        all.AddRange(SkipIfValues());
        all.AddRange(SkipIfMembers());
        all.AddRange(WithAssertions());
        all.AddRange(Candidates());

        // The differential stays out of the catalogue's counts: DIFFERENTIAL=1 adds it, and the prefix D- runs it alone.
        if (Environment.GetEnvironmentVariable("DIFFERENTIAL") == "1")
            all.AddRange(DiffRows());
        return all;
    }

    // ---- A: a sync method's value ----

    private static IEnumerable<Shape> SyncValues()
    {
        yield return S("A00-void", "A sync value", "[Step] public void M() { Thread.Sleep(60); Inner(); }", "new Steps().M(); return null;");
        yield return S("A00-issue-int", "A sync value", "[Step] public int M() => 42;", "return new Steps().M();", value: "42", tree: "M:Passed", minMs: 0);
        foreach (var (name, type, literal, printed, _) in ValueTypes)
            yield return S($"A01-{name}", "A sync value",
                $"[Step] public {type} M() {{ Thread.Sleep(60); Inner(); return {literal}; }}",
                "return P.Of(new Steps().M());", value: printed, extra: PrintHelper);
        yield return S("A02-dynamic", "A sync value", "[Step] public dynamic M() { Thread.Sleep(60); Inner(); return 7; }", "return new Steps().M().ToString();", value: "7");
        yield return S("A02-delegate", "A sync value", "[Step] public Func<int> M() { Thread.Sleep(60); Inner(); return () => 7; }", "return new Steps().M()();", value: "7");
        yield return S("A02-disposable", "A sync value", "[Step] public IDisposable M() { Thread.Sleep(60); Inner(); return new CancellationTokenSource(); }", "using var d = new Steps().M(); return \"ok\";", value: "ok");
        yield return S("A02-customer", "A sync value", "[Step] public Customer A_customer_is_registered(string name) { return new Customer(name); }\n[Step] public void M() { Thread.Sleep(60); Inner(); var c = A_customer_is_registered(\"Alice\"); C = c.Name; }\npublic string C;",
            "var s = new Steps(); s.M(); return s.C;", value: "Alice", tree: "M:Passed[Inner:Passed,A customer is registered(name=Alice):Passed]");
    }

    private static IEnumerable<Shape> ControlFlow()
    {
        const string f = "A sync value, control flow";
        yield return S("A10-branch-to-ret", f, "[Step] public int M(bool b) { Thread.Sleep(60); Inner(); if (b) return 1; return 2; }", "return new Steps().M(true);", value: "1", tree: "M(b=True):Passed[Inner:Passed]", branchToRet: true);
        yield return S("A10-two-returns", f, "[Step] public int M(bool b) { Thread.Sleep(60); Inner(); if (b) return 1; return 2; }", "return new Steps().M(false);", value: "2", tree: "M(b=False):Passed[Inner:Passed]");
        yield return S("A11-ternary", f, "[Step] public int M(bool b) { Thread.Sleep(60); Inner(); return b ? 1 : 2; }", "return new Steps().M(true);", value: "1", tree: "M(b=True):Passed[Inner:Passed]");
        yield return S("A12-switch-expr", f, "[Step] public string M(int k) { Thread.Sleep(60); Inner(); return k switch { 1 => \"a\", 2 => \"b\", 3 => \"c\", 4 => \"d\", _ => \"z\" }; }", "return new Steps().M(3);", value: "c", tree: "M(k=3):Passed[Inner:Passed]");
        yield return S("A12-switch-jump", f, "[Step] public int M(int k) { Thread.Sleep(60); Inner(); switch (k) { case 0: return 10; case 1: return 11; case 2: return 12; case 3: return 13; default: return 14; } }", "return new Steps().M(2);", value: "12", tree: "M(k=2):Passed[Inner:Passed]");
        yield return S("A13-try-finally", f, "[Step] public int M() { Thread.Sleep(60); try { return 1; } finally { Inner(); } }", "return new Steps().M();", value: "1");
        yield return S("A13-starts-in-try", f, "[Step] public int M() { try { Thread.Sleep(60); return 1; } finally { Inner(); } }", "return new Steps().M();", value: "1");
        yield return S("A14-try-catch", f, "[Step] public int M() { Thread.Sleep(60); Inner(); try { throw new InvalidOperationException(); } catch (InvalidOperationException) { return 3; } }", "return new Steps().M();", value: "3");
        yield return S("A14-catch-rethrow", f, "[Step] public int M() { Thread.Sleep(60); Inner(); try { throw new InvalidOperationException(\"x\"); } catch (InvalidOperationException) { throw; } }", "return new Steps().M();", tree: "M:Failed[Inner:Passed]", throws: "InvalidOperationException");
        yield return S("A14-void-ends-in-catch", f, "[Step] public void M() { Thread.Sleep(60); Inner(); try { throw new InvalidOperationException(\"x\"); } catch (InvalidOperationException) { throw; } }", "new Steps().M(); return null;", tree: "M:Failed[Inner:Passed]", throws: "InvalidOperationException");
        yield return S("A14-void-ends-in-finally", f, "[Step] public void M() { Thread.Sleep(60); try { throw new InvalidOperationException(\"x\"); } finally { Inner(); } }", "new Steps().M(); return null;", tree: "M:Failed[Inner:Passed]", throws: "InvalidOperationException");
        yield return S("A14-plain-task-ends-in-catch", f, "[Step] public Task M() { Thread.Sleep(60); Inner(); try { throw new InvalidOperationException(\"x\"); } catch (InvalidOperationException) { throw; } }", "await new Steps().M(); return null;", tree: "M:Failed[Inner:Passed]", throws: "InvalidOperationException");
        yield return S("A14-filter", f, "[Step] public int M(int k) { Thread.Sleep(60); Inner(); try { throw new InvalidOperationException(\"x\"); } catch (InvalidOperationException) when (k > 0) { return 7; } }", "return new Steps().M(1);", value: "7", tree: "M(k=1):Passed[Inner:Passed]");
        yield return S("A20-tail-value", f, "private int Other() { Thread.Sleep(60); Inner(); return 4; } [Step] public int M() { return Other(); }", "return new Steps().M();", value: "4", tailCall: true);
        yield return S("A20-tail-void", f, "private void Other() { Thread.Sleep(60); Inner(); } [Step] public void M() { Other(); }", "new Steps().M(); return \"ok\";", value: "ok", tailCall: true);
        yield return S("A21-full-opt-value", f, "[Step] [MethodImpl(MethodImplOptions.AggressiveOptimization)] public int M() { Thread.Sleep(60); Inner(); return 42; }", "return new Steps().M();", value: "42");
        yield return S("A21-full-opt-value-task", f, "[Step] [MethodImpl(MethodImplOptions.AggressiveOptimization)] public async ValueTask<int> M() { await Task.Delay(60); Inner(); return 42; }", "return await new Steps().M();", value: "42");
        yield return S("A15-using", f, "[Step] public int M() { Thread.Sleep(60); Inner(); using var c = new CancellationTokenSource(); return 4; }", "return new Steps().M();", value: "4");
        yield return S("A15-lock", f, "private readonly object _l = new(); [Step] public int M() { Thread.Sleep(60); Inner(); lock (_l) { return 5; } }", "return new Steps().M();", value: "5");
        yield return S("A16-loop", f, "[Step] public int M() { Thread.Sleep(60); Inner(); for (var i = 0; i < 10; i++) if (i == 6) return i; return -1; }", "return new Steps().M();", value: "6");
        yield return S("A16-starts-with-loop", f, "private int _n; [Step] public int M() { while (true) { Thread.Sleep(30); if (_n++ == 1) { Inner(); return _n; } } }", "return new Steps().M();", value: "2");
        yield return S("A17-only-throws", f, "[Step] public int M() => throw new InvalidOperationException(\"x\");", "return new Steps().M();", tree: "M:Failed", minMs: 0, throws: "InvalidOperationException");
        yield return S("A17-throws-after-work", f, "[Step] public Customer M() { Thread.Sleep(60); Inner(); throw new InvalidOperationException(\"x\"); }", "return new Steps().M();", tree: "M:Failed[Inner:Passed]", throws: "InvalidOperationException");
        yield return S("A18-recursion", f, "[Step] public int M(int k) { if (k == 0) return 0; return 1 + M(k - 1); }", "return new Steps().M(1);", value: "1", tree: "M(k=1):Passed[M(k=0):Passed]", minMs: 0);
        yield return S("A19-void-branches", f, "[Step] public void M(bool b) { Thread.Sleep(60); Inner(); if (b) return; Thread.Sleep(1); }", "new Steps().M(true); return null;", tree: "M(b=True):Passed[Inner:Passed]");
        yield return S("A19-void-try", f, "[Step] public void M() { Thread.Sleep(60); try { Inner(); return; } finally { Thread.Sleep(1); } }", "new Steps().M(); return null;");
        yield return S("A19-stackalloc", f, "[Step] public int M() { Thread.Sleep(60); Inner(); Span<int> s = stackalloc int[4]; s[0] = 8; return s[0]; }", "return new Steps().M();", value: "8");
    }

    // ---- B: returns that are not ordinary values ----

    private static IEnumerable<Shape> SpecialReturns()
    {
        const string f = "B generic, ref, span, pointer returns";
        yield return S("B01-generic-method-int", f, "[Step] public T M<T>(T v) { Thread.Sleep(60); Inner(); return v; }", "return new Steps().M(7);", value: "7", tree: "M(v=7):Passed[Inner:Passed]");
        yield return S("B01-generic-method-string", f, "[Step] public T M<T>(T v) { Thread.Sleep(60); Inner(); return v; }", "return new Steps().M(\"7\");", value: "7", tree: "M(v=7):Passed[Inner:Passed]");
        yield return S("B02-generic-class", f, "public class Steps<T> { [Step] public T M(T v) { Thread.Sleep(60); Inner(); return v; } [Step] public void Inner() { } }", "return new Steps<int>().M(7);", value: "7", tree: "M(v=7):Passed[Inner:Passed]");
        yield return S("B03-ref", f, "private int _x = 1; public int X => _x; [Step] public ref int M() { Thread.Sleep(60); Inner(); return ref _x; }", "var s = new Steps(); ref int r = ref s.M(); r = 5; return s.X;", value: "5", sync: true, allowIl: ReturnsByRef);
        yield return S("B04-ref-readonly-virtual", f, "private int _x = 6; [Step] public virtual ref readonly int M() { Thread.Sleep(60); Inner(); return ref _x; }", "var s = new Steps(); return s.M();", value: "6", sync: true, allowIl: ReturnsByRef);
        yield return S("B04-ref-readonly", f, "private int _x = 6; [Step] public ref readonly int M() { Thread.Sleep(60); Inner(); return ref _x; }", "var s = new Steps(); return s.M();", value: "6", sync: true, allowIl: ReturnsByRef);
        yield return S("B05-ref-generic", f, "[Step] public ref T M<T>(T[] a) { Thread.Sleep(60); Inner(); return ref a[0]; }", "var s = new Steps(); var a = new[] { 1 }; s.M(a) = 5; return a[0];", value: "5", sync: true, tree: "M(a=System.Int32[]):Passed[Inner:Passed]", allowIl: ReturnsByRef);
        yield return S("B06-span", f, "private int[] _a = { 1, 2 }; [Step] public Span<int> M() { Thread.Sleep(60); Inner(); return _a; }", "return new Steps().M().Length;", value: "2", sync: true);
        yield return S("B06-readonly-span", f, "[Step] public ReadOnlySpan<char> M() { Thread.Sleep(60); Inner(); return \"abc\"; }", "return new Steps().M().Length;", value: "3", sync: true);
        yield return S("B07-ref-struct", f, "[Step] public Rs M() { Thread.Sleep(60); Inner(); return new Rs { K = 4 }; }", "return new Steps().M().K;", value: "4", sync: true, extra: "public ref struct Rs { public int K; }");
        yield return S("B08-pointer", f, "[Step] public unsafe int* M() { Thread.Sleep(60); Inner(); return (int*)16; }", "unsafe { return (long)new Steps().M(); }", value: "16", sync: true, allowIl: IsPointer);
        yield return S("B09-function-pointer", f, "public static int F() => 9; [Step] public unsafe delegate*<int> M() { Thread.Sleep(60); Inner(); return &F; }", "unsafe { return new Steps().M()(); }", value: "9", sync: true);
    }

    // ---- C: Task ----

    private static IEnumerable<Shape> TaskShapes()
    {
        const string f = "C Task, Task<T>";
        yield return S("C01-async-task", f, "[Step] public async Task M() { await Task.Delay(60); Inner(); }", "await new Steps().M(); return null;");
        yield return S("C02-async-task-int", f, "[Step] public async Task<int> M() { await Task.Delay(60); Inner(); return 42; }", "return await new Steps().M();", value: "42");
        yield return S("C02-async-task-string", f, "[Step] public async Task<string> M() { await Task.Delay(60); Inner(); return \"s\"; }", "return await new Steps().M();", value: "s");
        yield return S("C02-async-task-guid", f, "[Step] public async Task<Guid> M() { await Task.Delay(60); Inner(); return Guid.Empty; }", "return await new Steps().M();", value: "00000000-0000-0000-0000-000000000000");
        yield return S("C03-plain-task", f, "[Step] public Task M() { Inner(); return Task.Delay(60); }", "await new Steps().M(); return null;");
        yield return S("C03-plain-task-int", f, "private async Task<int> W() { await Task.Delay(60); return 3; } [Step] public Task<int> M() { Inner(); return W(); }", "return await new Steps().M();", value: "3");
        yield return S("C04-plain-task-completed", f, "[Step] public Task M() { Inner(); return Task.CompletedTask; }", "await new Steps().M(); return null;", minMs: 0);
        yield return S("C05-plain-task-sync-throw", f, "[Step] public Task M() { Inner(); throw new InvalidOperationException(\"x\"); }", "await new Steps().M(); return null;", tree: "M:Failed[Inner:Passed]", minMs: 0, throws: "InvalidOperationException");
        yield return S("C05-plain-task-int-sync-throw", f, "[Step] public Task<int> M() => throw new InvalidOperationException(\"x\");", "return await new Steps().M();", tree: "M:Failed", minMs: 0, throws: "InvalidOperationException");
        yield return S("C05-sync-throw-then-next-step", f, "[Step] public Task M() => throw new InvalidOperationException(\"x\");\n[Step] public void Next() { }", "var s = new Steps(); try { await s.M(); } catch (InvalidOperationException) { } s.Next(); return null;", tree: "M:Failed,Next:Passed", minMs: 0);
        yield return S("C05-sync-guard-clause", f, "[Step] public Task M(string name) { if (name is null) throw new ArgumentNullException(nameof(name)); return Task.Delay(1); }", "await new Steps().M(null); return null;", tree: "M(name=null):Failed", minMs: 0, throws: "ArgumentNullException");
        yield return S("C06-async-throw-before-await", f, "[Step] public async Task M() { Inner(); throw new InvalidOperationException(\"x\"); await Task.Delay(1); }", "await new Steps().M(); return null;", tree: "M:Failed[Inner:Passed]", minMs: 0, throws: "InvalidOperationException");
        yield return S("C06-async-throw-after-await", f, "[Step] public async Task M() { await Task.Delay(60); Inner(); throw new InvalidOperationException(\"x\"); }", "await new Steps().M(); return null;", tree: "M:Failed[Inner:Passed]", throws: "InvalidOperationException");
        yield return S("C07-faulted", f, "[Step] public Task M() => Task.FromException(new InvalidOperationException(\"x\"));", "await new Steps().M(); return null;", tree: "M:Failed", minMs: 0, throws: "InvalidOperationException");
        yield return S("C08-cancelled", f, "[Step] public Task M() => Task.FromCanceled(new CancellationToken(true));", "await new Steps().M(); return null;", tree: "M:Failed", minMs: 0, throws: "TaskCanceledException");
        yield return S("C09-multiple-returns", f, "[Step] public Task M(bool b) { Inner(); if (b) return Task.Delay(60); return Task.Delay(61); }", "await new Steps().M(false); return null;", tree: "M(b=False):Passed[Inner:Passed]");
        yield return S("C10-null-task", f, "[Step] public Task M() => null;", "await new Steps().M(); return null;", tree: "M:Failed", minMs: 0, throws: "NullReferenceException");
        yield return S("C11-generic-method", f, "[Step] public async Task<T> M<T>(T v) { await Task.Delay(60); Inner(); return v; }", "return await new Steps().M(7);", value: "7", tree: "M(v=7):Passed[Inner:Passed]");
        yield return S("C11-generic-class", f, "public class Steps<T> { [Step] public async Task<T> M(T v) { await Task.Delay(60); Inner(); return v; } [Step] public void Inner() { } }", "return await new Steps<string>().M(\"7\");", value: "7", tree: "M(v=7):Passed[Inner:Passed]");
        yield return S("C12-task-subclass", f, "[Step] public MyTask M() { Inner(); var t = new MyTask(() => Thread.Sleep(60)); t.Start(); return t; }", "await new Steps().M(); return \"ok\";", value: "ok", tree: "Inner:Passed", refused: true, extra: "public class MyTask : Task { public MyTask(Action a) : base(a) { } }");
        yield return S("C13-whenall-keeps-both-faults", f, "[Step] public Task M() => Task.WhenAll(Task.FromException(new InvalidOperationException(\"a\")), Task.FromException(new ArgumentException(\"b\")));", "var t = new Steps().M(); try { await t; } catch { } return t.Exception.InnerExceptions.Count;", value: "1", tree: "M:Failed", minMs: 0);
    }

    // ---- D: ValueTask ----

    private static IEnumerable<Shape> ValueTaskShapes()
    {
        const string f = "D ValueTask, ValueTask<T>";
        yield return S("D00-issue-value-task", f, "[Step] public async ValueTask M() { await Task.Delay(100); Inner(); }", "await new Steps().M(); return null;", minMs: 90);
        yield return S("D01-async-value-task", f, "[Step] public async ValueTask M() { await Task.Delay(60); Inner(); await Task.Delay(1); }", "await new Steps().M(); return null;");
        yield return S("D02-async-int", f, "[Step] public async ValueTask<int> M() { await Task.Delay(60); Inner(); return 42; }", "return await new Steps().M();", value: "42");
        yield return S("D02-async-string", f, "[Step] public async ValueTask<string> M() { await Task.Delay(60); Inner(); return \"s\"; }", "return await new Steps().M();", value: "s");
        yield return S("D02-async-guid", f, "[Step] public async ValueTask<Guid> M() { await Task.Delay(60); Inner(); return Guid.Empty; }", "return await new Steps().M();", value: "00000000-0000-0000-0000-000000000000");
        yield return S("D02-async-big", f, "[Step] public async ValueTask<Big> M() { await Task.Delay(60); Inner(); return new Big { A = 4 }; }", "return await new Steps().M();", value: "Big(4)");
        yield return S("D03-default", f, "[Step] public ValueTask M() { Inner(); return default; }", "await new Steps().M(); return null;", minMs: 0);
        yield return S("D03-completed", f, "[Step] public ValueTask M() { Inner(); return ValueTask.CompletedTask; }", "await new Steps().M(); return null;", minMs: 0);
        yield return S("D04-from-result", f, "[Step] public ValueTask<int> M() { Inner(); return new ValueTask<int>(42); }", "return await new Steps().M();", value: "42", minMs: 0);
        yield return S("D04-from-result-api", f, "[Step] public ValueTask<int> M() { Inner(); return ValueTask.FromResult(42); }", "return await new Steps().M();", value: "42", minMs: 0);
        yield return S("D05-wraps-task", f, "[Step] public ValueTask M() { Inner(); return new ValueTask(Task.Delay(60)); }", "await new Steps().M(); return null;");
        yield return S("D05-wraps-task-int", f, "private async Task<int> W() { await Task.Delay(60); return 3; } [Step] public ValueTask<int> M() { Inner(); return new ValueTask<int>(W()); }", "return await new Steps().M();", value: "3");
        yield return S("D06-source-backed", f, Source + "[Step] public ValueTask M() { Inner(); var s = new Src(); s.Finish(60); return new ValueTask(s, s.Version); }", "await new Steps().M(); return null;");
        yield return S("D06-source-backed-int", f, Source + "[Step] public ValueTask<int> M() { Inner(); var s = new Src(); s.Finish(60); return new ValueTask<int>(s, s.Version); }", "return await new Steps().M();", value: "5");
        yield return S("D07-pooling-builder", f, "[Step] [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))] public async ValueTask M() { await Task.Delay(60); Inner(); }", "await new Steps().M(); return null;");
        yield return S("D07-pooling-builder-int", f, "[Step] [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))] public async ValueTask<int> M() { await Task.Delay(60); Inner(); return 3; }", "return await new Steps().M();", value: "3");
        yield return S("D08-sync-throw", f, "[Step] public ValueTask M() { Inner(); throw new InvalidOperationException(\"x\"); }", "await new Steps().M(); return null;", tree: "M:Failed[Inner:Passed]", minMs: 0, throws: "InvalidOperationException");
        yield return S("D08-sync-throw-int", f, "[Step] public ValueTask<int> M() => throw new InvalidOperationException(\"x\");", "return await new Steps().M();", tree: "M:Failed", minMs: 0, throws: "InvalidOperationException");
        yield return S("D09-faulted", f, "[Step] public ValueTask M() => ValueTask.FromException(new InvalidOperationException(\"x\"));", "await new Steps().M(); return null;", tree: "M:Failed", minMs: 0, throws: "InvalidOperationException");
        yield return S("D09-throw-after-await", f, "[Step] public async ValueTask M() { await Task.Delay(60); Inner(); throw new InvalidOperationException(\"x\"); }", "await new Steps().M(); return null;", tree: "M:Failed[Inner:Passed]", throws: "InvalidOperationException");
        yield return S("D10-cancelled", f, "[Step] public ValueTask M() => ValueTask.FromCanceled(new CancellationToken(true));", "await new Steps().M(); return null;", tree: "M:Failed", minMs: 0, throws: "TaskCanceledException");
        yield return S("D11-generic-method", f, "[Step] public async ValueTask<T> M<T>(T v) { await Task.Delay(60); Inner(); return v; }", "return await new Steps().M(7);", value: "7", tree: "M(v=7):Passed[Inner:Passed]");
        yield return S("D11-generic-class", f, "public class Steps<T> { [Step] public async ValueTask<T> M(T v) { await Task.Delay(60); Inner(); return v; } [Step] public void Inner() { } }", "return await new Steps<string>().M(\"7\");", value: "7", tree: "M(v=7):Passed[Inner:Passed]");
        yield return S("D12-as-task-by-caller", f, "[Step] public async ValueTask<int> M() { await Task.Delay(60); Inner(); return 2; }", "return await new Steps().M().AsTask();", value: "2");
        yield return S("D13-multiple-returns", f, "[Step] public ValueTask<int> M(bool b) { Inner(); if (b) return new ValueTask<int>(1); return new ValueTask<int>(2); }", "return await new Steps().M(false);", value: "2", tree: "M(b=False):Passed[Inner:Passed]", minMs: 0);
    }

    private const string Source = """
        public sealed class Src : IValueTaskSource, IValueTaskSource<int>
        {
            private ManualResetValueTaskSourceCore<int> _core;
            public short Version => _core.Version;
            public void Finish(int ms) { Task.Delay(ms).ContinueWith(_ => _core.SetResult(5)); }
            int IValueTaskSource<int>.GetResult(short token) => _core.GetResult(token);
            void IValueTaskSource.GetResult(short token) => _core.GetResult(token);
            public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
            public void OnCompleted(Action<object> c, object s, short t, ValueTaskSourceOnCompletedFlags f) => _core.OnCompleted(c, s, t, f);
        }

        """;

    // ---- E: a method whose work is not over when it returns, and awaitables the weaver cannot rebuild ----

    private static IEnumerable<Shape> Deferred()
    {
        const string f = "E deferred work, other awaitables";
        yield return S("E01-iterator", f, "[Step] public IEnumerable<int> M() { Inner(); yield return 1; Thread.Sleep(60); yield return 2; }", "return new Steps().M().Sum();", value: "3", tree: "Inner:Passed", refused: true);
        yield return S("E02-iterator-enumerator", f, "[Step] public IEnumerator<int> M() { Inner(); yield return 1; }", "var e = new Steps().M(); e.MoveNext(); return e.Current;", value: "1", tree: "Inner:Passed", refused: true);
        yield return S("E03-async-iterator", f, "[Step] public async IAsyncEnumerable<int> M() { await Task.Delay(60); Inner(); yield return 1; yield return 2; }", "var t = 0; await foreach (var x in new Steps().M()) t += x; return t;", value: "3", tree: "Inner:Passed", refused: true);
        yield return S("E04-async-enumerator", f, "[Step] public async IAsyncEnumerator<int> M() { await Task.Delay(60); Inner(); yield return 1; }", "var e = new Steps().M(); await e.MoveNextAsync(); return e.Current;", value: "1", tree: "Inner:Passed", refused: true);
        yield return S("E05-async-void", f, "[Step] public async void M() { await Task.Delay(60); Inner(); Driver.Done.SetResult(); }", "new Steps().M(); await Driver.Done.Task; return \"ok\";", value: "ok", tree: "Inner:Passed", refused: true);
        yield return S("E06-task-like", f, "[Step] public async MyLike M() { await Task.Delay(60); Inner(); }", "await new Steps().M(); return \"ok\";", value: "ok", tree: "Inner:Passed", refused: true, extra: TaskLike);
        yield return S("E07-awaitable-struct", f, "[Step] public Aw M() { Inner(); return new Aw(); }", "await new Steps().M(); return \"ok\";", value: "ok", tree: "Inner:Passed", refused: true, extra: "public struct Aw { public TaskAwaiter GetAwaiter() => Task.Delay(60).GetAwaiter(); }");
        yield return S("E08-configured-task", f, "[Step] public ConfiguredTaskAwaitable M() { Inner(); return Task.Delay(60).ConfigureAwait(false); }", "await new Steps().M(); return \"ok\";", value: "ok", tree: "Inner:Passed", refused: true);
        yield return S("E09-configured-value-task", f, "[Step] public ConfiguredValueTaskAwaitable M() { Inner(); return new ValueTask(Task.Delay(60)).ConfigureAwait(false); }", "await new Steps().M(); return \"ok\";", value: "ok", tree: "Inner:Passed", refused: true);
        yield return S("E10-yield-awaitable", f, "[Step] public YieldAwaitable M() { Inner(); return Task.Yield(); }", "await new Steps().M(); return \"ok\";", value: "ok", tree: "Inner:Passed", refused: true);
        yield return S("E11-awaitable-interface", f, "[Step] public IAw M() { Inner(); return new Aw(); }", "await new Steps().M(); return \"ok\";", value: "ok", tree: "Inner:Passed", refused: true, extra: "public interface IAw { TaskAwaiter GetAwaiter(); } public class Aw : IAw { public TaskAwaiter GetAwaiter() => Task.Delay(60).GetAwaiter(); }");
        yield return S("E12-awaitable-base-interface", f, "[Step] public IAw2 M() { Inner(); return new Aw(); }", "await new Steps().M(); return \"ok\";", value: "ok", tree: "Inner:Passed", refused: true, extra: "public interface IAw { TaskAwaiter GetAwaiter(); } public interface IAw2 : IAw { } public class Aw : IAw2 { public TaskAwaiter GetAwaiter() => Task.Delay(60).GetAwaiter(); }");
        // Values: the weaver cannot tell these are lazy, and they are not its to follow. The step is the call.
        yield return S("E20-lazy-linq", f, "[Step] public IEnumerable<int> M() { Thread.Sleep(60); Inner(); return new[] { 1, 2 }.Select(x => x + 1); }", "return new Steps().M().Sum();", value: "5");
        yield return S("E21-func-task", f, "[Step] public Func<Task> M() { Thread.Sleep(60); Inner(); return () => Task.CompletedTask; }", "await new Steps().M()(); return \"ok\";", value: "ok");
        yield return S("E22-object-holding-task", f, "[Step] public object M() { Thread.Sleep(60); Inner(); return Task.CompletedTask; }", "return new Steps().M() is Task;", value: "True");
        yield return S("E24-own-type-named-task", f, "[Step] public My.Task M() { Thread.Sleep(60); Inner(); return new My.Task(); }", "return new Steps().M() is My.Task;", value: "True", extra: "namespace My { public class Task { } }");
        yield return S("E25-array-of-tasks", f, "[Step] public Task[] M() { Thread.Sleep(60); Inner(); return new[] { Task.CompletedTask }; }", "return new Steps().M().Length;", value: "1");
        yield return S("E26-generic-task-constraint", f, "[Step] public T M<T>(T t) where T : Task { Thread.Sleep(60); Inner(); return t; }", "await new Steps().M(Task.CompletedTask); return \"ok\";", value: "ok", tree: "Inner:Passed", refused: true);
        yield return S("E27-nullable-value-task", f, "[Step] public ValueTask? M() { Thread.Sleep(60); Inner(); return null; }", "return new Steps().M().HasValue;", value: "False");
        yield return S("E23-list", f, "[Step] public List<int> M() { Thread.Sleep(60); Inner(); return new List<int> { 1 }; }", "return new Steps().M().Count;", value: "1");
    }

    private const string TaskLike = """
        [AsyncMethodBuilder(typeof(MyLikeBuilder))]
        public readonly struct MyLike
        {
            internal readonly Task Inner;
            internal MyLike(Task inner) => Inner = inner;
            public TaskAwaiter GetAwaiter() => Inner.GetAwaiter();
        }

        public struct MyLikeBuilder
        {
            private AsyncTaskMethodBuilder _b;
            public static MyLikeBuilder Create() => new() { _b = AsyncTaskMethodBuilder.Create() };
            public void Start<TSm>(ref TSm sm) where TSm : IAsyncStateMachine => _b.Start(ref sm);
            public void SetStateMachine(IAsyncStateMachine sm) => _b.SetStateMachine(sm);
            public void SetResult() => _b.SetResult();
            public void SetException(Exception e) => _b.SetException(e);
            public MyLike Task => new(_b.Task);
            public void AwaitOnCompleted<TA, TSm>(ref TA a, ref TSm sm) where TA : INotifyCompletion where TSm : IAsyncStateMachine => _b.AwaitOnCompleted(ref a, ref sm);
            public void AwaitUnsafeOnCompleted<TA, TSm>(ref TA a, ref TSm sm) where TA : ICriticalNotifyCompletion where TSm : IAsyncStateMachine => _b.AwaitUnsafeOnCompleted(ref a, ref sm);
        }

        """;

    // ---- F: parameters: the preamble boxes each one into an object[] ----

    private static IEnumerable<Shape> Parameters()
    {
        const string f = "F parameters";
        yield return S("F00-by-value", f, "[Step] public void M(int x, string s) { Thread.Sleep(60); Inner(); }", "new Steps().M(1, \"a\"); return null;", tree: "M(x=1,s=a):Passed[Inner:Passed]");
        yield return S("F01-ref-int", f, "[Step] public void M(ref int x) { Thread.Sleep(60); Inner(); x++; }", "var v = 1; new Steps().M(ref v); return v;", value: "2", tree: "M(x=1):Passed[Inner:Passed]", sync: true);
        yield return S("F02-out-int", f, "[Step] public void M(out int x) { Thread.Sleep(60); Inner(); x = 7; }", "new Steps().M(out var v); return v;", value: "7", sync: true);
        yield return S("F03-in-int", f, "[Step] public void M(in int x) { Thread.Sleep(60); Inner(); }", "var v = 5; new Steps().M(in v); return null;", tree: "M(x=5):Passed[Inner:Passed]", sync: true);
        yield return S("F03-ref-readonly-int", f, "[Step] public void M(ref readonly int x) { Thread.Sleep(60); Inner(); }", "var v = 5; new Steps().M(in v); return null;", tree: "M(x=5):Passed[Inner:Passed]", sync: true);
        yield return S("F03-in-virtual", f, "[Step] public virtual void M(in int x) { Thread.Sleep(60); Inner(); }", "var v = 5; new Steps().M(in v); return null;", tree: "M(x=5):Passed[Inner:Passed]", sync: true);
        yield return S("F03-in-interface", f, "public interface IS { void M(in int x); } public class Steps : IS { [Step] public void M(in int x) { Thread.Sleep(60); Inner(); } [Step] public void Inner() { } }", "var v = 5; ((IS)new Steps()).M(in v); return null;", tree: "M(x=5):Passed[Inner:Passed]", sync: true);
        yield return S("F04-in-big", f, "[Step] public void M(in Big b) { Thread.Sleep(60); Inner(); }", "var v = new Big { A = 3 }; new Steps().M(in v); return null;", tree: "M(b=Big(3)):Passed[Inner:Passed]", sync: true);
        yield return S("F05-ref-string", f, "[Step] public void M(ref string s) { Thread.Sleep(60); Inner(); s = \"b\"; }", "var v = \"a\"; new Steps().M(ref v); return v;", value: "b", tree: "M(s=a):Passed[Inner:Passed]", sync: true);
        yield return S("F06-out-string", f, "[Step] public void M(out string s) { Thread.Sleep(60); Inner(); s = \"b\"; }", "new Steps().M(out var v); return v;", value: "b", sync: true);
        yield return S("F07-ref-generic-int", f, "[Step] public void M<T>(ref T v) { Thread.Sleep(60); Inner(); }", "var v = 4; new Steps().M(ref v); return null;", tree: "M(v=4):Passed[Inner:Passed]", sync: true);
        yield return S("F07-ref-generic-string", f, "[Step] public void M<T>(ref T v) { Thread.Sleep(60); Inner(); }", "var v = \"a\"; new Steps().M(ref v); return null;", tree: "M(v=a):Passed[Inner:Passed]", sync: true);
        yield return S("F08-out-and-value", f, "[Step] public int M(int a, out int b) { Thread.Sleep(60); Inner(); b = a + 1; return a; }", "var r = new Steps().M(1, out var v); return r + v;", value: "3", tree: "M(a=1):Passed[Inner:Passed]", sync: true);
        yield return S("F10-span", f, "[Step] public void M(Span<int> s) { Thread.Sleep(60); Inner(); }", "new Steps().M(new int[2]); return null;", tree: "M(s=[Span<Int32>]):Passed[Inner:Passed]", sync: true);
        yield return S("F11-readonly-span", f, "[Step] public int M(ReadOnlySpan<char> s) { Thread.Sleep(60); Inner(); return s.Length; }", "return new Steps().M(\"abc\");", value: "3", tree: "M(s=[ReadOnlySpan<Char>]):Passed[Inner:Passed]", sync: true);
        yield return S("F12-ref-struct", f, "[Step] public void M(Rs r) { Thread.Sleep(60); Inner(); }", "new Steps().M(new Rs()); return null;", tree: "M(r=[Rs]):Passed[Inner:Passed]", sync: true, extra: "public ref struct Rs { public int K; }");
        yield return S("F13-pointer", f, "[Step] public unsafe void M(int* p) { Thread.Sleep(60); Inner(); }", "unsafe { new Steps().M((int*)16); } return null;", tree: "M(p=[Int32*]):Passed[Inner:Passed]", sync: true);
        yield return S("F14-function-pointer", f, "public static int F() => 9; [Step] public unsafe void M(delegate*<int> p) { Thread.Sleep(60); Inner(); }", "unsafe { new Steps().M(&Steps.F); } return null;", tree: "M(p=[delegate*]):Passed[Inner:Passed]", sync: true);
        yield return S("F15-params-span", f, "[Step] public void M(params ReadOnlySpan<int> xs) { Thread.Sleep(60); Inner(); }", "new Steps().M(1, 2); return null;", tree: "M(xs=[ReadOnlySpan<Int32>]):Passed[Inner:Passed]", sync: true);
        yield return S("F15-allows-ref-struct", f, "[Step] public void M<T>(T v) where T : allows ref struct { Thread.Sleep(60); Inner(); }", "new Steps().M(new Span<int>(new int[2])); return null;", tree: "M(v=[T]):Passed[Inner:Passed]", sync: true);
        yield return S("F15-allows-ref-struct-int", f, "[Step] public void M<T>(T v) where T : allows ref struct { Thread.Sleep(60); Inner(); }", "new Steps().M(5); return null;", tree: "M(v=[T]):Passed[Inner:Passed]", sync: true);
        yield return S("F16-ref-allows-ref-struct", f, "[Step] public void M<T>(ref T v) where T : allows ref struct { Thread.Sleep(60); Inner(); }", "var v = new Span<int>(new int[2]); new Steps().M(ref v); return null;", tree: "M(v=[T]):Passed[Inner:Passed]", sync: true);
        yield return S("F16-typed-reference", f, "[Step] public void M(TypedReference r) { Thread.Sleep(60); Inner(); }", "int v = 1; new Steps().M(__makeref(v)); return null;", tree: "M(r=[TypedReference]):Passed[Inner:Passed]", sync: true);
        yield return S("F16-arglist", f, "[Step] public void M(__arglist) { Thread.Sleep(60); Inner(); }", "new Steps().M(__arglist(1, 2)); return null;", sync: true);
        yield return S("F16-ref-span", f, "[Step] public void M(ref Span<int> s) { Thread.Sleep(60); Inner(); }", "Span<int> v = new int[2]; new Steps().M(ref v); return null;", tree: "M(s=[Span<Int32>]):Passed[Inner:Passed]", sync: true);
        yield return S("F16-ref-array", f, "[Step] public void M(ref int[] a) { Thread.Sleep(60); Inner(); }", "var v = new[] { 1 }; new Steps().M(ref v); return null;", tree: "M(a=System.Int32[]):Passed[Inner:Passed]", sync: true);
        yield return S("F16-params-array", f, "[Step] public void M(params int[] xs) { Thread.Sleep(60); Inner(); }", "new Steps().M(1, 2); return null;", tree: "M(xs=System.Int32[]):Passed[Inner:Passed]");
        yield return S("F17-generic-by-value", f, "[Step] public void M<T>(T v) { Thread.Sleep(60); Inner(); }", "new Steps().M(1); return null;", tree: "M(v=1):Passed[Inner:Passed]");
        yield return S("F18-nullable-null", f, "[Step] public void M(int? v, string s) { Thread.Sleep(60); Inner(); }", "new Steps().M(null, null); return null;", tree: "M(v=null,s=null):Passed[Inner:Passed]");
        yield return S("F19-plain-task-ref", f, "[Step] public Task M(ref int x) { Inner(); x++; return Task.Delay(60); }", "var v = 1; var t = new Steps().M(ref v); t.GetAwaiter().GetResult(); return v;", value: "2", tree: "M(x=1):Passed[Inner:Passed]", sync: true);
        yield return S("F20-value-task-in", f, "[Step] public ValueTask M(in int x) { Inner(); return new ValueTask(Task.Delay(60)); }", "var v = 1; new Steps().M(in v).AsTask().GetAwaiter().GetResult(); return \"ok\";", value: "ok", tree: "M(x=1):Passed[Inner:Passed]", sync: true);
        yield return S("F21-tostring-throws", f, "[Step] public int M(Bad b) { Thread.Sleep(60); Inner(); return 1; }", "return new Steps().M(new Bad());", value: "1", tree: "M(b=[Bad]):Passed[Inner:Passed]", extra: "public class Bad { public override string ToString() => throw new InvalidOperationException(\"no\"); }");
        yield return S("F22-async-tostring-throws", f, "[Step] public async Task<int> M(Bad b) { await Task.Delay(60); Inner(); return 1; }", "return await new Steps().M(new Bad());", value: "1", tree: "M(b=[Bad]):Passed[Inner:Passed]", extra: "public class Bad { public override string ToString() => throw new InvalidOperationException(\"no\"); }");
    }

    // ---- G: kinds of method ----

    private static IEnumerable<Shape> Kinds()
    {
        const string f = "G kinds of method";
        yield return S("G01-static", f, "public class Steps { [Step] public static int M() { Thread.Sleep(60); Inner(); return 1; } [Step] public static void Inner() { } }", "return Steps.M();", value: "1");
        yield return S("G02-override", f, "public class B { [Step] public virtual int M() { Thread.Sleep(60); return 1; } } public class Steps : B { [Step] public override int M() { return base.M() + 1; } }", "return new Steps().M();", value: "2", tree: "M:Passed[M:Passed]");
        yield return S("G03-interface-default", f, "public interface IS { [Step] int M() { Thread.Sleep(60); return 1; } } public class Steps : IS { }", "return ((IS)new Steps()).M();", value: "1", tree: "M:Passed");
        yield return S("G04-explicit-interface", f, "public interface IS { int M(); } public class Steps : IS { [Step(Description = \"M\")] int IS.M() { Thread.Sleep(60); return 1; } }", "return ((IS)new Steps()).M();", value: "1", tree: "M:Passed");
        yield return S("G05-property-get", f, "public int P { [Step(Description = \"M\")] get { Thread.Sleep(60); Inner(); return 1; } }", "return new Steps().P;", value: "1");
        yield return S("G05-property-set", f, "public int V; public int P { [Step(Description = \"M\")] set { Thread.Sleep(60); Inner(); V = value; } }", "var s = new Steps(); s.P = 2; return s.V;", value: "2", tree: "M(value=2):Passed[Inner:Passed]");
        yield return S("G05-property-init", f, "public int V; public int P { [Step(Description = \"M\")] init { Thread.Sleep(60); Inner(); V = value; } }", "return new Steps { P = 3 }.V;", value: "3", tree: "M(value=3):Passed[Inner:Passed]");
        yield return S("G06-indexer", f, "public int this[int i] { [Step(Description = \"M\")] get { Thread.Sleep(60); Inner(); return i; } }", "return new Steps()[3];", value: "3", tree: "M(i=3):Passed[Inner:Passed]");
        yield return S("G07-operator", f, "public class Steps { public int K; [Step(Description = \"M\")] public static Steps operator +(Steps a, Steps b) { Thread.Sleep(60); return new Steps { K = 1 }; } }", "return (new Steps() + new Steps()).K;", value: "1", tree: "M(a=Steps,b=Steps):Passed");
        yield return S("G08-extension", f, "public class Steps { } public static class Ext { [Step(Description = \"M\")] public static int Plus(this int x) { Thread.Sleep(60); return x + 1; } }", "return 1.Plus();", value: "2", tree: "M(x=1):Passed");
        yield return S("G10-local-function", f, "public int Go() { [Step(Description = \"M\")] int L() { Thread.Sleep(60); Inner(); return 1; } return L(); }", "return new Steps().Go();", value: "1");
        yield return S("G11-local-capturing-struct", f, "public int Go() { var k = 1; [Step(Description = \"M\")] int L() { Thread.Sleep(60); Inner(); return k + 1; } return L(); }", "return new Steps().Go();", value: "2");
        yield return S("G11-local-void-capturing", f, "public int R; public void Go() { var k = 1; [Step(Description = \"M\")] void L() { Thread.Sleep(60); Inner(); R = k + 1; } L(); }", "var s = new Steps(); s.Go(); return s.R;", value: "2");
        yield return S("G10-local-function-name", f, "public int Go() { [Step] int The_local_step() { return 1; } return The_local_step(); }", "return new Steps().Go();", value: "1", tree: "The local step:Passed", minMs: 0);
        yield return S("G10-local-function-void-name", f, "public void Go() { [Step] void The_local_step() { } The_local_step(); }", "new Steps().Go(); return \"ok\";", value: "ok", tree: "The local step:Passed", minMs: 0);
        yield return S("G12-lambda-name", f, "public int The_method() { Func<int> f = [Step] () => 1; return f(); }", "return new Steps().The_method();", value: "1", tree: "The method:Passed", minMs: 0);
        yield return S("G12-lambda-void-name", f, "public void The_method() { Action f = [Step] () => { }; f(); }", "new Steps().The_method(); return \"ok\";", value: "ok", tree: "The method:Passed", minMs: 0);
        yield return S("G12-lambda-capturing", f, "public int Go() { var k = 1; Func<int> f = [Step(Description = \"M\")] () => { Thread.Sleep(60); return k + 1; }; return f(); }", "return new Steps().Go();", value: "2", tree: "M:Passed");
        yield return S("G12-lambda", f, "public int Go() { Func<int> f = [Step(Description = \"M\")] () => { Thread.Sleep(60); return 1; }; return f(); }", "return new Steps().Go();", value: "1", tree: "M:Passed");
        yield return S("G12-async-lambda", f, "public Task<int> Go() { Func<Task<int>> f = [Step(Description = \"M\")] async () => { await Task.Delay(60); return 1; }; return f(); }", "return await new Steps().Go();", value: "1", tree: "M:Passed");
        yield return S("G13-struct-method", f, "public struct Steps { public int K; [Step] public int M() { Thread.Sleep(60); return K + 1; } }", "return new Steps { K = 1 }.M();", value: "2", tree: "M:Passed");
        yield return S("G13-struct-void", f, "public struct Steps { public int K; [Step] public void M() { Thread.Sleep(60); K++; } }", "var s = new Steps(); s.M(); return s.K;", value: "1", tree: "M:Passed");
        yield return S("G14-readonly-struct", f, "public readonly struct Steps { [Step] public int M() { Thread.Sleep(60); return 1; } }", "return new Steps().M();", value: "1", tree: "M:Passed");
        yield return S("G15-ref-struct-method", f, "public ref struct Steps { public int K; [Step] public int M() { Thread.Sleep(60); return K + 1; } }", "return new Steps { K = 1 }.M();", value: "2", tree: "M:Passed", sync: true);
        yield return S("G16-partial", f, "public partial class Steps { [Step] public partial int M(); } public partial class Steps { public partial int M() { Thread.Sleep(60); Inner(); return 1; } [Step] public void Inner() { } }", "return new Steps().M();", value: "1");
        yield return S("G17-static-abstract", f, "public interface IS { static abstract int M(); } public class Steps : IS { [Step] public static int M() { Thread.Sleep(60); return 1; } } public static class C { public static int Call<T>() where T : IS => T.M(); }", "return C.Call<Steps>();", value: "1", tree: "M:Passed");
        yield return S("G18-nested", f, "public class Outer { public class Steps { [Step] public int M() { Thread.Sleep(60); return 1; } } }", "return new Outer.Steps().M();", value: "1", tree: "M:Passed");
        yield return S("G19-abstract-declared", f, "public abstract class B { [Step] public abstract int M(); } public class Steps : B { public override int M() { return 1; } }", "return new Steps().M();", value: "1", tree: "", minMs: 0);
        yield return S("G21-finalizer", f, "[Step] ~Steps() { GC.KeepAlive(this); }", "return \"ok\";", value: "ok", tree: "", minMs: 0);
        yield return S("G22-returns-allows-ref-struct", f, "[Step] public T M<T>(T v) where T : allows ref struct { Thread.Sleep(60); Inner(); return v; }", "return new Steps().M(new Span<int>(new int[3])).Length;", value: "3", tree: "M(v=[T]):Passed[Inner:Passed]", sync: true);
        yield return S("G20-keyword-attributes", f, "[GivenStep(Description = \"M\")] public int G() { Thread.Sleep(60); return 1; } [WhenStep] public int W() => 2; [ThenStep] public int Th() => 3; [ButStep] public int B() => 4; [ButWhenStep] public int Bw() => 5;", "var s = new Steps(); return s.G() + s.W() + s.Th() + s.B() + s.Bw();", value: "15", tree: null);
    }

    // ---- H: SkipIf with every return: the bypass path returns as well ----

    private static IEnumerable<Shape> SkipIfValues()
    {
        const string sync = "H SkipIf, sync value";
        const string task = "H SkipIf, Task<T>";
        const string valueTask = "H SkipIf, ValueTask<T>";

        yield return S("H00-void", sync, "public bool Off => true; [Step(SkipIf = nameof(Off), SkipReason = \"r\")] public void M() { throw new Exception(\"ran\"); }", "new Steps().M(); return \"ok\";", value: "ok", tree: "M:Bypassed", minMs: 0);
        yield return S("H00-task", task, "public bool Off => true; [Step(SkipIf = nameof(Off), SkipReason = \"r\")] public async Task M() { throw new Exception(\"ran\"); }", "await new Steps().M(); return \"ok\";", value: "ok", tree: "M:Bypassed", minMs: 0);
        yield return S("H00-value-task", valueTask, "public bool Off => true; [Step(SkipIf = nameof(Off), SkipReason = \"r\")] public async ValueTask M() { throw new Exception(\"ran\"); }", "await new Steps().M(); return \"ok\";", value: "ok", tree: "M:Bypassed", minMs: 0);

        foreach (var (name, type, literal, _, dflt) in ValueTypes)
        {
            yield return S($"H01-{name}", sync, $"public bool Off => true; [Step(SkipIf = nameof(Off))] public {type} M() {{ throw new Exception(\"ran\"); }}",
                "return P.Of(new Steps().M());", value: dflt, tree: "M:Bypassed", minMs: 0, extra: PrintHelper);
            yield return S($"H02-{name}", task, $"public bool Off => true; [Step(SkipIf = nameof(Off))] public async Task<{type}> M() {{ throw new Exception(\"ran\"); }}",
                "return P.Of(await new Steps().M());", value: dflt, tree: "M:Bypassed", minMs: 0, extra: PrintHelper);
            yield return S($"H03-{name}", valueTask, $"public bool Off => true; [Step(SkipIf = nameof(Off))] public async ValueTask<{type}> M() {{ throw new Exception(\"ran\"); }}",
                "return P.Of(await new Steps().M());", value: dflt, tree: "M:Bypassed", minMs: 0, extra: PrintHelper);
        }

        yield return S("H04-generic-method", sync, "public bool Off => true; [Step(SkipIf = nameof(Off))] public T M<T>(T v) { throw new Exception(\"ran\"); }", "return new Steps().M(7);", value: "0", tree: "M(v=7):Bypassed", minMs: 0);
        yield return S("H04-generic-method-ref", sync, "public bool Off => true; [Step(SkipIf = nameof(Off))] public T M<T>(T v) { throw new Exception(\"ran\"); }", "return P.Of(new Steps().M(\"x\"));", value: "(null)", tree: "M(v=x):Bypassed", minMs: 0, extra: PrintHelper);
        yield return S("H05-task-generic-int", task, "public bool Off => true; [Step(SkipIf = nameof(Off))] public async Task<T> M<T>(T v) { throw new Exception(\"ran\"); }", "return await new Steps().M(7);", value: "0", tree: "M(v=7):Bypassed", minMs: 0);
        yield return S("H05-task-generic-string", task, "public bool Off => true; [Step(SkipIf = nameof(Off))] public async Task<T> M<T>(T v) { throw new Exception(\"ran\"); }", "return P.Of(await new Steps().M(\"x\"));", value: "(null)", tree: "M(v=x):Bypassed", minMs: 0, extra: PrintHelper);
        yield return S("H06-span", sync, "public bool Off => true; [Step(SkipIf = nameof(Off))] public Span<int> M() { throw new Exception(\"ran\"); }", "return new Steps().M().Length;", value: "0", tree: "M:Bypassed", minMs: 0, sync: true, allowIl: ReturnsByRef);
        yield return S("H07-pointer", sync, "public bool Off => true; [Step(SkipIf = nameof(Off))] public unsafe int* M() { throw new Exception(\"ran\"); }", "unsafe { return (long)new Steps().M(); }", value: "0", tree: "M:Bypassed", minMs: 0, sync: true, allowIl: IsPointer);
        // A ref return has no default to hand back: the step runs, with a warning, as SkipIf does for a member it cannot use.
        yield return S("H08-ref-off", sync, "private int _x = 3; public bool Off => true; [Step(SkipIf = nameof(Off))] public ref int M() { return ref _x; }", "return new Steps().M();", value: "3", tree: "M:Passed", minMs: 0, sync: true, warning: "SkipIf", allowIl: ReturnsByRef);
        yield return S("H09-out-param", sync, "public bool Off => true; [Step(SkipIf = nameof(Off))] public void M(out string s) { throw new Exception(\"ran\"); }", "var v = \"before\"; new Steps().M(out v); return P.Of(v);", value: "(null)", tree: "M:Bypassed", minMs: 0, sync: true, extra: PrintHelper);
        yield return S("H11-nested-bypass-void", sync, "public bool Off => true; [Step(SkipIf = nameof(Off))] public void S() { throw new Exception(\"ran\"); } [Step] public void M() { S(); Thread.Sleep(60); Inner(); }", "new Steps().M(); return \"ok\";", value: "ok", tree: "M:Passed[S:Bypassed,Inner:Passed]");
        yield return S("H11-nested-bypass-task", task, "public bool Off => true; [Step(SkipIf = nameof(Off))] public async Task S() { throw new Exception(\"ran\"); } [Step] public async Task M() { await S(); await Task.Delay(60); Inner(); }", "await new Steps().M(); return \"ok\";", value: "ok", tree: "M:Passed[S:Bypassed,Inner:Passed]");
        yield return S("H11-nested-bypass-task-int", task, "public bool Off => true; [Step(SkipIf = nameof(Off))] public async Task<int> S() { throw new Exception(\"ran\"); } [Step] public async Task M() { await S(); await Task.Delay(60); Inner(); }", "await new Steps().M(); return \"ok\";", value: "ok", tree: "M:Passed[S:Bypassed,Inner:Passed]");
        yield return S("H11-nested-bypass-value", sync, "public bool Off => true; [Step(SkipIf = nameof(Off))] public int S() { throw new Exception(\"ran\"); } [Step] public void M() { S(); Thread.Sleep(60); Inner(); }", "new Steps().M(); return \"ok\";", value: "ok", tree: "M:Passed[S:Bypassed,Inner:Passed]");
        yield return S("H12-bypassed-with-reason-then-next", sync, "public bool Off => true; [Step(SkipIf = nameof(Off), SkipReason = \"r\")] public void S() { throw new Exception(\"ran\"); } [Step] public void Next() { }", "var s = new Steps(); s.S(); s.Next(); return \"ok\";", value: "ok", tree: "S:Bypassed,Next:Passed", minMs: 0);
        yield return S("H13-skip-locals-init", sync, "public bool Off => true; [System.Runtime.CompilerServices.SkipLocalsInit] [Step(SkipIf = nameof(Off))] public long M() { return 9; }", "Dirty(); return new Steps().M(); static void Dirty() { Span<long> s = stackalloc long[256]; s.Fill(-1); GC.KeepAlive(s[3]); }", value: "0", tree: "M:Bypassed", minMs: 0, sync: true);
        yield return S("H09-out-span", sync, "public bool Off => true; [Step(SkipIf = nameof(Off))] public void M(out Span<int> s) { throw new Exception(\"ran\"); }", "new Steps().M(out var v); return v.Length;", value: "0", tree: "M:Bypassed", minMs: 0, sync: true);
        yield return S("H10-false-runs-value", sync, "public bool Off => false; [Step(SkipIf = nameof(Off))] public int M() { Thread.Sleep(60); Inner(); return 42; }", "return new Steps().M();", value: "42");
        yield return S("H10-false-runs-task", task, "public bool Off => false; [Step(SkipIf = nameof(Off))] public async Task<Guid> M() { await Task.Delay(60); Inner(); return Guid.Empty; }", "return await new Steps().M();", value: "00000000-0000-0000-0000-000000000000");
        yield return S("H10-false-runs-value-task", valueTask, "public bool Off => false; [Step(SkipIf = nameof(Off))] public async ValueTask<int> M() { await Task.Delay(60); Inner(); return 42; }", "return await new Steps().M();", value: "42");
    }

    private static IEnumerable<Shape> SkipIfMembers()
    {
        const string f = "H SkipIf, the member it names";
        yield return S("H20-property", f, "public bool Off => true; [Step(SkipIf = nameof(Off))] public void M() { throw new Exception(\"ran\"); }", "new Steps().M(); return \"ok\";", value: "ok", tree: "M:Bypassed", minMs: 0);
        yield return S("H21-field", f, "public bool Off = true; [Step(SkipIf = nameof(Off))] public void M() { throw new Exception(\"ran\"); }", "new Steps().M(); return \"ok\";", value: "ok", tree: "M:Bypassed", minMs: 0);
        yield return S("H22-static-on-static", f, "public static bool Off => true; [Step(SkipIf = nameof(Off))] public static void M() { throw new Exception(\"ran\"); }", "Steps.M(); return \"ok\";", value: "ok", tree: "M:Bypassed", minMs: 0);
        yield return S("H23-static-on-instance", f, "public static bool Off = true; [Step(SkipIf = nameof(Off))] public void M() { throw new Exception(\"ran\"); }", "new Steps().M(); return \"ok\";", value: "ok", tree: "M:Bypassed", minMs: 0);
        // An instance member on a static step has no instance to read: warned, and the step runs.
        yield return S("H24-instance-on-static", f, "public bool Off => true; [Step(SkipIf = nameof(Off))] public static void M() { Thread.Sleep(60); }", "Steps.M(); return \"ok\";", value: "ok", tree: "M:Passed", warning: "SkipIf");
        yield return S("H24-instance-on-static-arg", f, "public bool Off => true; [Step(SkipIf = nameof(Off))] public static void M(string s) { Thread.Sleep(60); }", "Steps.M(\"a\"); return \"ok\";", value: "ok", tree: "M(s=a):Passed", warning: "SkipIf");
        yield return S("H25-generic-class-property", f, "public class Steps<T> { public bool Off => true; [Step(SkipIf = nameof(Off))] public void M() { throw new Exception(\"ran\"); } }", "new Steps<int>().M(); return \"ok\";", value: "ok", tree: "M:Bypassed", minMs: 0);
        yield return S("H25-generic-class-field", f, "public class Steps<T> { public bool Off = true; [Step(SkipIf = nameof(Off))] public void M() { throw new Exception(\"ran\"); } }", "new Steps<string>().M(); return \"ok\";", value: "ok", tree: "M:Bypassed", minMs: 0);
        yield return S("H25-generic-class-static", f, "public class Steps<T> { public static bool Off = true; [Step(SkipIf = nameof(Off))] public void M() { throw new Exception(\"ran\"); } }", "new Steps<string>().M(); return \"ok\";", value: "ok", tree: "M:Bypassed", minMs: 0);
        yield return S("H26-generic-base-property", f, "public class B<T> { public bool Off => true; } public class Steps : B<int> { [Step(SkipIf = nameof(Off))] public void M() { throw new Exception(\"ran\"); } }", "new Steps().M(); return \"ok\";", value: "ok", tree: "M:Bypassed", minMs: 0);
        yield return S("H26-generic-base-field", f, "public class B<T> { public bool Off = true; } public class Steps : B<string> { [Step(SkipIf = nameof(Off))] public void M() { throw new Exception(\"ran\"); } }", "new Steps().M(); return \"ok\";", value: "ok", tree: "M:Bypassed", minMs: 0);
        yield return S("H27-private-base-property", f, "public class B { private bool Off => true; } public class Steps : B { [Step(SkipIf = \"Off\")] public void M() { Thread.Sleep(60); } }", "new Steps().M(); return \"ok\";", value: "ok", tree: "M:Passed", warning: "SkipIf");
        yield return S("H27-private-base-field", f, "public class B { private bool Off = true; } public class Steps : B { [Step(SkipIf = \"Off\")] public void M() { Thread.Sleep(60); } }", "new Steps().M(); return \"ok\";", value: "ok", tree: "M:Passed", warning: "SkipIf");
        yield return S("H28-protected-base", f, "public class B { protected bool Off => true; } public class Steps : B { [Step(SkipIf = nameof(Off))] public void M() { throw new Exception(\"ran\"); } }", "new Steps().M(); return \"ok\";", value: "ok", tree: "M:Bypassed", minMs: 0);
        yield return S("H29-struct-property", f, "public struct Steps { public bool Off => true; [Step(SkipIf = nameof(Off))] public void M() { throw new Exception(\"ran\"); } }", "new Steps().M(); return \"ok\";", value: "ok", tree: "M:Bypassed", minMs: 0);
        yield return S("H29-struct-field", f, "public struct Steps { public bool Off; [Step(SkipIf = nameof(Off))] public void M() { throw new Exception(\"ran\"); } }", "new Steps { Off = true }.M(); return \"ok\";", value: "ok", tree: "M:Bypassed", minMs: 0);
        yield return S("H29-struct-generic", f, "public struct Steps<T> { public bool Off => true; [Step(SkipIf = nameof(Off))] public void M() { throw new Exception(\"ran\"); } }", "new Steps<int>().M(); return \"ok\";", value: "ok", tree: "M:Bypassed", minMs: 0);
        yield return S("H28-private-protected", f, "public class B { private protected bool Off => true; } public class Steps : B { [Step(SkipIf = nameof(Off))] public void M() { throw new Exception(\"ran\"); } }", "new Steps().M(); return \"ok\";", value: "ok", tree: "M:Bypassed", minMs: 0);
        yield return S("H30-virtual-property", f, "public virtual bool Off => true; [Step(SkipIf = nameof(Off))] public void M() { throw new Exception(\"ran\"); }", "new Steps().M(); return \"ok\";", value: "ok", tree: "M:Bypassed", minMs: 0);
        yield return S("H31-not-bool", f, "public int Off => 1; [Step(SkipIf = nameof(Off))] public void M() { Thread.Sleep(60); }", "new Steps().M(); return \"ok\";", value: "ok", tree: "M:Passed", warning: "not a bool");
        yield return S("H32-not-found", f, "[Step(SkipIf = \"Nope\")] public void M() { Thread.Sleep(60); }", "new Steps().M(); return \"ok\";", value: "ok", tree: "M:Passed", warning: "not found");
        yield return S("H33-write-only-property", f, "public bool Off { set { } } [Step(SkipIf = nameof(Off))] public void M() { Thread.Sleep(60); }", "new Steps().M(); return \"ok\";", value: "ok", tree: "M:Passed", warning: "not found");
        yield return S("H34-method-not-member", f, "public bool Off() => true; [Step(SkipIf = nameof(Off))] public void M() { Thread.Sleep(60); }", "new Steps().M(); return \"ok\";", value: "ok", tree: "M:Passed", warning: "not found");
        yield return S("H36-indexer-named", f, "public bool this[int i] => true; [Step(SkipIf = \"Item\")] public void M() { Thread.Sleep(60); }", "new Steps().M(); return \"ok\";", value: "ok", tree: "M:Passed", warning: "not found");
        yield return S("H37-generic-chain", f, "public class A<T> { public bool Off => true; } public class B<U> : A<U[]> { } public class Steps : B<int> { [Step(SkipIf = nameof(Off))] public void M() { throw new Exception(\"ran\"); } }", "new Steps().M(); return \"ok\";", value: "ok", tree: "M:Bypassed", minMs: 0);
        yield return S("H38-generic-self-through-base", f, "public class A<T> { public bool Off = true; } public class Steps<V> : A<V> { [Step(SkipIf = nameof(Off))] public void M() { throw new Exception(\"ran\"); } }", "new Steps<string>().M(); return \"ok\";", value: "ok", tree: "M:Bypassed", minMs: 0);
        yield return S("H35-on-iterator", f, "public bool Off => true; [Step(SkipIf = nameof(Off))] public IEnumerable<int> M() { yield return 1; }", "return new Steps().M().Sum();", value: "1", tree: "", minMs: 0, refused: true, warning: "its SkipIf is not read");
    }

    // ---- I: with the assertion weaver on the same method, in both orders ----

    private static IEnumerable<Shape> WithAssertions()
    {
        foreach (var (order, first) in new[] { ("assertions-first", true), ("steps-first", false) })
        {
            var f = "I with assertion weaving, " + order;
            const string A = "M:Passed[~assert:Passed]";
            yield return S($"I01-value-{order}", f, "[Step] public int M() { var k = 1; k.ShouldBe(1); return 42; }", "return new Steps().M();", value: "42", tree: A, minMs: 0, assertions: true, assertionsFirst: first, noInner: true);
            yield return S($"I02-value-task-{order}", f, "[Step] public async ValueTask M() { await Task.Delay(60); var k = 1; k.ShouldBe(1); }", "await new Steps().M(); return \"ok\";", value: "ok", tree: A, assertions: true, assertionsFirst: first, noInner: true);
            yield return S($"I03-value-task-int-{order}", f, "[Step] public async ValueTask<int> M() { await Task.Delay(60); var k = 1; k.ShouldBe(1); return 3; }", "return await new Steps().M();", value: "3", tree: A, assertions: true, assertionsFirst: first, noInner: true);
            yield return S($"I04-plain-task-{order}", f, "[Step] public Task<int> M() { var k = 1; k.ShouldBe(1); return Task.FromResult(1); }", "return await new Steps().M();", value: "1", tree: A, minMs: 0, assertions: true, assertionsFirst: first, noInner: true);
            yield return S($"I05-branches-{order}", f, "[Step] public int M(bool b) { if (b) { var k = 1; k.ShouldBe(1); return 1; } return 2; }", "return new Steps().M(true);", value: "1", tree: "M(b=True):Passed[~assert:Passed]", minMs: 0, assertions: true, assertionsFirst: first, noInner: true);
            yield return S($"I06-failing-{order}", f, "[Step] public int M() { var k = 1; k.ShouldBe(2); return 1; }", "return new Steps().M();", tree: "M:Failed[~assert:Failed]", minMs: 0, throws: "ShouldAssertException", assertions: true, assertionsFirst: first, noInner: true);
            yield return S($"I07-returned-assertion-{order}", f, "[Step] public Task<InvalidOperationException> M() { return Should.ThrowAsync<InvalidOperationException>(() => Task.FromException(new InvalidOperationException(\"x\"))); }", "return (await new Steps().M()).Message;", value: "x", tree: null, minMs: 0, assertions: true, assertionsFirst: first, noInner: true);
            yield return S($"I10-bypass-{order}", f, "public bool Off => true; [Step(SkipIf = nameof(Off))] public int M() { var k = 1; k.ShouldBe(1); return 1; }", "return new Steps().M();", value: "0", tree: "M:Bypassed", minMs: 0, assertions: true, assertionsFirst: first, noInner: true);
            yield return S($"Z83-runtime-async-assertion-{order}", f, "[Step] public async Task M() { await Task.Yield(); 1.ShouldBe(1); }", "await new Steps().M(); return \"ok\";", value: "ok", tree: A, minMs: 0, assertions: true, assertionsFirst: first, noInner: true, extra: "//RUNTIME-ASYNC");
            yield return S($"Z120-assertion-first-in-a-catch-{order}", f, "[Step] public void M() { try { throw new InvalidOperationException(\"x\"); } catch (InvalidOperationException e) { e.Message.ShouldBe(\"x\"); } }", "new Steps().M(); return \"ok\";", value: "ok", tree: A, minMs: 0, assertions: true, assertionsFirst: first, noInner: true);
            yield return S($"Z120-assertion-first-in-a-finally-{order}", f, "[Step] public void M() { try { GC.KeepAlive(this); } finally { 1.ShouldBe(1); } }", "new Steps().M(); return \"ok\";", value: "ok", tree: A, minMs: 0, assertions: true, assertionsFirst: first, noInner: true);
            yield return S($"Z120-assertion-first-in-a-finally-and-more-{order}", f, "[Step] public void M() { try { GC.KeepAlive(this); } finally { 1.ShouldBe(1); GC.KeepAlive(this); } }", "new Steps().M(); return \"ok\";", value: "ok", tree: A, minMs: 0, assertions: true, assertionsFirst: first, noInner: true);
            // A real build's order only: F12.
            if (!first) yield return S($"Z124-assertion-in-a-short-circuit-{order}", f, "[Step] public bool M(string s) => s.ShouldNotBeNull() != null && s.Length > 0;", "return new Steps().M(\"a\");", value: "True", tree: null, minMs: 0, assertions: true, assertionsFirst: first, noInner: true);
            // A known limit of the assertion weaver, with no step in it: section 12 of the plan.
            yield return S($"Z125-assertion-in-a-short-circuit-of-a-plain-method-{order}", f, "public bool Go(string s) => s.ShouldNotBeNull() != null && s.Length > 0; [Step] public bool M() => Go(\"a\");", "return new Steps().M();", value: "True", tree: "M:Passed", minMs: 0, assertions: true, assertionsFirst: first, noInner: true);
            // A DebugType changed with the old PDB still in obj: no symbols to read, so the step is drawn and no statement can be found.
            yield return S($"Z127-a-stale-pdb-{order}", f, "//STALE-PDB\n[Step] public void M() { 1.ShouldBe(1); }", "new Steps().M(); return \"ok\";", value: "ok", tree: "M:Passed", minMs: 0, assertions: true, assertionsFirst: first, noInner: true);
            yield return S($"I11-expression-void-{order}", f, "[Step] public void M() => 1.ShouldBe(1);", "new Steps().M(); return \"ok\";", value: "ok", tree: A, minMs: 0, assertions: true, assertionsFirst: first, noInner: true);
            yield return S($"I12-expression-value-{order}", f, "[Step] public string M() => \"a\".ShouldNotBeNull();", "return new Steps().M();", value: "a", tree: A, minMs: 0, assertions: true, assertionsFirst: first, noInner: true);
            yield return S($"I13-expression-task-{order}", f, "[Step] public Task<InvalidOperationException> M() => Should.ThrowAsync<InvalidOperationException>(() => Task.FromException(new InvalidOperationException(\"x\")));", "return (await new Steps().M()).Message;", value: "x", tree: A, minMs: 0, assertions: true, assertionsFirst: first, noInner: true);
            yield return S($"I14-expression-in-parameter-{order}", f, "[Step] public void M(in int x) => x.ShouldBe(2);", "var v = 2; new Steps().M(in v); return \"ok\";", value: "ok", tree: "M(x=2):Passed[~assert:Passed]", minMs: 0, assertions: true, assertionsFirst: first, noInner: true, sync: true);
            yield return S($"I15-returned-value-{order}", f, "[Step] public string M() { return \"a\".ShouldNotBeNull(); }", "return new Steps().M();", value: "a", tree: A, minMs: 0, assertions: true, assertionsFirst: first, noInner: true);
            yield return S($"I16-expression-failing-{order}", f, "[Step] public void M() => 1.ShouldBe(2);", "new Steps().M(); return \"ok\";", tree: "M:Failed[~assert:Failed]", minMs: 0, throws: "ShouldAssertException", assertions: true, assertionsFirst: first, noInner: true);
            yield return S($"I08-void-{order}", f, "[Step] public void M() { var k = 1; k.ShouldBe(1); }", "new Steps().M(); return \"ok\";", value: "ok", tree: A, minMs: 0, assertions: true, assertionsFirst: first, noInner: true);
            yield return S($"I09-async-task-{order}", f, "[Step] public async Task M() { await Task.Delay(60); var k = 1; k.ShouldBe(1); }", "await new Steps().M(); return \"ok\";", value: "ok", tree: A, assertions: true, assertionsFirst: first, noInner: true);
        }
    }
}
