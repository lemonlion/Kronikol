using System.Runtime.CompilerServices;
using Kronikol.Tracking;
using Shouldly;

[assembly: TrackAssertions]

// No step anywhere: only the assertion weaver has touched these.
static class Plain
{
    public static void Catch() { try { throw new InvalidOperationException("x"); } catch (InvalidOperationException e) { e.Message.ShouldBe("x"); } }
    public static void Finally() { try { GC.KeepAlive(typeof(Plain)); } finally { 1.ShouldBe(1); } }
}

static class Program
{
    static int Main()
    {
        var failures = 0;
        foreach (var (name, act) in new (string, Action)[] { ("catch", Plain.Catch), ("finally", Plain.Finally) })
        {
            try { act(); Console.WriteLine($"{name}: ran"); }
            catch (InvalidProgramException) { Console.WriteLine($"{name}: InvalidProgramException"); failures++; }
        }
        return failures;
    }
}
