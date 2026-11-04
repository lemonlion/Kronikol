using Kronikol.Tracking;
using Shouldly;

[assembly: TrackAssertions]

// No step anywhere: only the assertion weaver has touched these. Each is called so that its condition is false, and each should read as it does with no weaver at all.
static class Plain
{
    public static string IfElse(string s) { if (s.ShouldNotBeNull().Length > 5) return "then"; else return "else"; }
    public static int While(List<int> l) { var n = 0; while (l.ShouldNotBeNull().Count > n) n++; return n; }
    public static int For(List<int> l) { var n = 0; for (var i = 0; l.ShouldNotBeNull().Count > i; i++) n++; return n; }
    public static int DoWhile(List<int> l) { var n = 0; do { n++; } while (l.ShouldNotBeNull().Count > n); return n; }
    public static string EarlyReturn(string s) { if (s.ShouldNotBeNull().Length > 5) return "returned early"; return "carried on"; }
    public static string Guard(string s) { if (s.ShouldNotBeNull().Length > 5) throw new ArithmeticException("too long"); return "no throw"; }
    public static string Throws() { if (Should.Throw<ArithmeticException>(() => throw new ArithmeticException("x")).Message == "y") return "then"; else return "else"; }
    public static async Task<string> Awaited() { if ((await Should.ThrowAsync<ArithmeticException>(() => Task.FromException(new ArithmeticException("x")))).Message == "y") return "then"; else return "else"; }
    public static int WhileTrue(List<int> l) { var n = 0; while (true) { if (l.ShouldNotBeNull().Count <= n) break; n++; } return n; }
}

static class Program
{
    private static void Show(string name, string expected, Func<object> run)
    {
        string actual;
        try { actual = run().ToString()!; }
        catch (Exception e) { actual = "threw " + e.GetType().Name; }
        Console.WriteLine($"{name}: expected {expected}, read {actual}{(actual == expected ? "" : "  WRONG")}");
    }

    private static int Main()
    {
        var three = new List<int> { 1, 2, 3 };
        Show("if and else", "else", () => Plain.IfElse("a"));
        Show("while over 3", "3", () => Plain.While(three));
        Show("for over 3", "3", () => Plain.For(three));
        Show("do while over 3", "3", () => Plain.DoWhile(three));
        Show("if and a return", "carried on", () => Plain.EarlyReturn("a"));
        Show("a guard", "no throw", () => Plain.Guard("a"));
        Show("an exception's message", "else", () => Plain.Throws());
        Show("an awaited exception's message", "else", () => Plain.Awaited().GetAwaiter().GetResult());

        // Last, as it can spin for ever: on its own thread, so that the process can say so and end.
        string? read = null;
        var thread = new Thread(() => read = Plain.WhileTrue(three).ToString()) { IsBackground = true };
        thread.Start();
        Console.WriteLine(thread.Join(TimeSpan.FromSeconds(10)) ? $"while true with a break: expected 3, read {read}{(read == "3" ? "" : "  WRONG")}" : "while true with a break: expected 3, read nothing in 10 seconds  WRONG");
        return 0;
    }
}
