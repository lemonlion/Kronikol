using System.Reflection;
using Kronikol;
using Kronikol.Tracking;

[assembly: TrackAssertions]

// Each case runs as its own "test": the resolver names it, and the notes it recorded are read back by that id.
var current = "";
Track.TestIdResolver = () => current;

var woven = typeof(Woven210).Assembly.GetTypes().Any(t => t.Name == "__AssertionTrackingWeaved__");
Console.WriteLine($"Kronikol {typeof(Track).Assembly.GetName().Version} · woven: {woven}");

foreach (var type in new[] { typeof(Woven210), typeof(Fluent210), typeof(That210), typeof(Audit210) })
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
        // The label, and for a failure the next line of the library's message.
        .Select(body => string.Join(" | ", body.Take(body.Length > 1 && body[0].StartsWith("✗") ? 3 : 1)))
        .ToArray();
    Console.WriteLine($"== {type.Name}.{m.Name}: {outcome}");
    foreach (var n in notes)
        Console.WriteLine($"   {n}");
    if (notes.Length == 0)
        Console.WriteLine("   (no note)");
}

public enum CurrencyCode { GBP, USD }

public enum ApplicationStage { Open, Cancelled }

public static class Currencies
{
    public const string Gbp = "GBP";
    public const string Usd = "USD";
    public static readonly string GbpReadonly = "GBP";
}

public static class Limits
{
    public const int MaxRetries = 3;
}

public static class Keys
{
    public const string Primary = "primary";
}

public sealed record Item(string Name, bool IsActive);

public sealed class Request { public string Email { get; init; } = ""; }

public sealed class Result
{
    public decimal SplitPercentage { get; init; } = 12.5m;
    public string CurrencyIsoAlpha3 { get; init; } = "GBP";
    public CurrencyCode Code { get; init; } = CurrencyCode.GBP;
    public CurrencyCode? MaybeCode { get; init; } = CurrencyCode.GBP;
    public System.Net.HttpStatusCode StatusCode { get; init; } = System.Net.HttpStatusCode.OK;
    public string Stage { get; init; } = "Cancelled";
    public int Count { get; init; } = 3;
    public int Big { get; init; } = int.MaxValue;
    public DateTime Date { get; init; } = new(2026, 10, 27);
    public List<Item> Items { get; init; } = [new("a", true), new("b", true), new("c", false)];
    public string Link { get; init; } = "https://example.com/x";
    public Dictionary<string, int> Map { get; init; } = new() { ["primary"] = 1 };
    public Dictionary<string, string> Errors { get; init; } = new() { ["Email"] = "required" };
    public T Get<T>() where T : class => (T)(object)Items[0];
}

public static class Data
{
    public static Result Result() => new();
    public static void Fail() => throw new InvalidOperationException("boom");
}
