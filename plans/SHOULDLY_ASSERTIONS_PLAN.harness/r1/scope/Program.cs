using System.Reflection;
using Kronikol;
using Kronikol.Tracking;
#if AWESOME9
using AwesomeAssertions;
using AwesomeAssertions.Execution;
#else
using FluentAssertions;
using FluentAssertions.Execution;
#endif

[assembly: TrackAssertions]

// Each case runs as its own "test": the resolver names it, and the notes it recorded are read back by that id.
var current = "";
Track.TestIdResolver = () => current;

var library = typeof(AssertionScope).Assembly.GetName();
var woven = typeof(Cases).Assembly.GetTypes().Any(t => t.Name == "__AssertionTrackingWeaved__");
Console.WriteLine($"{library.Name} {library.Version} · Kronikol {typeof(Track).Assembly.GetName().Version} · woven: {woven}");

foreach (var m in typeof(Cases).GetMethods(BindingFlags.Public | BindingFlags.Static).OrderBy(m => m.MetadataToken))
{
    current = "probe-" + m.Name;
    string outcome;
    try
    {
        m.Invoke(null, null);
        outcome = "passed";
    }
    catch (Exception e)
    {
        var inner = e is TargetInvocationException { InnerException: { } i } ? i : e;
        outcome = $"threw {inner.GetType().Name}: {inner.Message.Replace("\r", "").Replace("\n", "⏎")}";
    }

    var notes = RequestResponseLogger.RequestAndResponseLogs
        .Where(l => l.TestId == current && l.IsOverrideStart && l.MarkerKind == DiagramMarkerKind.Assertion)
        .Select(l => (l.PlantUml ?? "").Trim().Replace("\r", "").Replace("\n", "⏎"))
        .ToArray();
    Console.WriteLine($"== {m.Name}: {outcome}");
    Console.WriteLine($"   notes: {notes.Length}");
    foreach (var n in notes)
        Console.WriteLine($"   | {n}");
}

public static class Cases
{
    // On 4.11.0 both notes are drawn as passes; the first should be drawn failed.
    public static void Scope_first_fails()
    {
        int result = 3;
        using (new AssertionScope())
        {
            result.Should().Be(5);
            result.Should().Be(3);
        }
    }

    // A nested scope hands its failure to the outer one when it ends: claimed once, by the assertion that made it.
    public static void Nested_scope_fails_once()
    {
        int result = 3;
        using (new AssertionScope())
        {
            using (new AssertionScope())
            {
                result.Should().Be(4);
            }
            result.Should().Be(3);
        }
    }

    // The library runs each inspector in a scope of its own: the second inspector's note should be drawn failed.
    public static void Inspector_fails()
    {
        var items = new List<int> { 1, 2 };
        items.Should().SatisfyRespectively(
            first => first.Should().Be(1),
            second => second.Should().Be(3));
    }

    // Outside any scope, as before.
    public static void Plain_pass()
    {
        int result = 3;
        result.Should().Be(3);
    }
}
