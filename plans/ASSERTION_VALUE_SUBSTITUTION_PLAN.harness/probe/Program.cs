using System.Reflection;
using Kronikol;
using Kronikol.Tracking;

[assembly: TrackAssertions]

// Each case runs as its own "test": the resolver names it, and the notes it recorded are read back by that id.
var current = "";
Track.TestIdResolver = () => current;

var woven = typeof(WovenShouldly).Assembly.GetTypes().Any(t => t.Name == "__AssertionTrackingWeaved__");
Console.WriteLine($"Kronikol {typeof(Track).Assembly.GetName().Version} · woven: {woven}");

foreach (var type in new[] { typeof(WovenShouldly), typeof(WovenFluent), typeof(ThatCases), typeof(DirectCases), typeof(SuppressedMethods), typeof(SuppressedType) })
foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Static).OrderBy(m => m.MetadataToken))
{
    current = $"probe-{type.Name}-{m.Name}";
    string outcome;
    try
    {
        var r = m.Invoke(null, null);
        if (r is Task t) await t;
        outcome = "passed";
    }
    catch (Exception e)
    {
        var inner = e is TargetInvocationException { InnerException: { } i } ? i : e;
        outcome = $"threw {inner.GetType().Name}";
    }

    var notes = RequestResponseLogger.RequestAndResponseLogs
        .Where(l => l.TestId == current && l.IsOverrideStart && l.MarkerKind == DiagramMarkerKind.Assertion)
        .Select(l => (l.PlantUml ?? "").Replace("\r", "").Split('\n'))
        .Select(lines => lines.SkipWhile(x => !x.StartsWith("hnote across")).Skip(1).TakeWhile(x => x != "end note").ToArray())
        // The label, and for a failure the library's whole message.
        .Select(body => string.Join("⏎", body.Take(body.Length > 0 && body[0].StartsWith("✗") ? body.Length : 1)))
        .ToArray();
    Console.WriteLine($"== {type.Name}.{m.Name}: {outcome}");
    foreach (var n in notes)
        Console.WriteLine($"   {n}");
    if (notes.Length == 0)
        Console.WriteLine("   (no note)");
}

public enum Colour { Red, Blue }

public sealed record Order(string Id, Colour Colour, List<string> Lines)
{
    public Inner? Inner { get; init; }
    public Box Box { get; init; } = new();
    public Plain Plain { get; init; } = new();
    public int Code { get; init; } = 7;
    public string? Missing { get; init; }
    public string Field = "f";
    public T Get<T>() => default!;
}

public sealed class Inner
{
    public string Value { get; init; } = "v";
    public override string ToString() => $"Inner({Value})";
}

public sealed class Box { public Inner Inner { get; init; } = new(); }

public sealed class Plain { public string Name { get; init; } = "plain"; }

public sealed class Faulty
{
    public string Throws => throw new InvalidOperationException("boom");
    public string Fine => "fine";
}

public static class Data
{
    public static Order Expected() => new("A1", Colour.Blue, ["x", "y"]) { Inner = new Inner() };
    public static Order Actual() => new("A1", Colour.Blue, ["x", "y"]) { Inner = new Inner() };
}
