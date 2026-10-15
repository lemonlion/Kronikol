// Every Shouldly 4.3.0 check's failure text, with its source (the default) and without it
// (ShouldlyConfiguration.DisableSourceInErrors(), the form Shouldly writes when it cannot read the caller's file),
// and what Kronikol's ErrorDiffParser and FailureText.ClusterKey (copied below: it is internal) read from each.
using System.Text.Json;
using Kronikol.Reports;
using Shouldly;

var cases = new (string Name, Action Act)[]
{
    ("int ShouldBe", () => 4.ShouldBe(5)),
    ("string ShouldBe", () => "a".ShouldBe("b")),
    ("ShouldBe null expected", () => "x".ShouldBe(null)),
    ("enum ShouldBe", () => DayOfWeek.Monday.ShouldBe(DayOfWeek.Friday)),
    ("ShouldBe with message", () => 4.ShouldBe(5, "custom message")),
    ("double ShouldBe tolerance", () => 1.0.ShouldBe(2.0, 0.1)),
    ("collection ShouldBe", () => new[] { 1, 2 }.ShouldBe(new[] { 1, 3 })),
    ("ShouldNotBe", () => 5.ShouldNotBe(5)),
    ("ShouldBeNull", () => "x".ShouldBeNull()),
    ("ShouldNotBeNull", () => ((string?)null).ShouldNotBeNull()),
    ("ShouldBeTrue", () => false.ShouldBeTrue()),
    ("ShouldBeFalse", () => true.ShouldBeFalse()),
    ("string ShouldContain", () => "abc".ShouldContain("z")),
    ("collection ShouldContain", () => new[] { 1, 2 }.ShouldContain(3)),
    ("ShouldNotContain", () => new[] { 1, 2 }.ShouldNotContain(2)),
    ("ShouldBeEmpty", () => new[] { 1 }.ShouldBeEmpty()),
    ("ShouldNotBeEmpty", () => Array.Empty<int>().ShouldNotBeEmpty()),
    ("ShouldBeGreaterThan", () => 1.ShouldBeGreaterThan(2)),
    ("ShouldBeLessThan", () => 3.ShouldBeLessThan(2)),
    ("ShouldBeInRange", () => 10.ShouldBeInRange(1, 5)),
    ("ShouldBeOneOf", () => 1.ShouldBeOneOf(2, 3)),
    ("ShouldStartWith", () => "abc".ShouldStartWith("z")),
    ("ShouldEndWith", () => "abc".ShouldEndWith("z")),
    ("ShouldMatch", () => "abc".ShouldMatch("^z")),
    ("ShouldBeOfType", () => ((object)1).ShouldBeOfType<string>()),
    ("ShouldBeAssignableTo", () => ((object)1).ShouldBeAssignableTo<string>()),
    ("ShouldBeSameAs", () => new object().ShouldBeSameAs(new object())),
    ("ShouldBeEquivalentTo", () => new { A = 1 }.ShouldBeEquivalentTo(new { A = 2 })),
    ("ShouldContainKey", () => new Dictionary<string, int> { ["a"] = 1 }.ShouldContainKey("b")),
    ("ShouldHaveSingleItem", () => new[] { 1, 2 }.ShouldHaveSingleItem()),
    ("ShouldAllBe", () => new[] { 1, 2 }.ShouldAllBe(x => x > 1)),
    ("Should.Throw", () => Should.Throw<InvalidOperationException>(() => { })),
    ("ShouldSatisfyAllConditions", () => 4.ShouldSatisfyAllConditions(x => x.ShouldBe(5), x => x.ShouldBeLessThan(3))),
};

foreach (var sourceless in new[] { false, true })
{
    Console.WriteLine(sourceless ? "== WITHOUT SOURCE (ShouldlyConfiguration.DisableSourceInErrors())" : "== WITH SOURCE (the default)");
    using var _ = sourceless ? ShouldlyConfiguration.DisableSourceInErrors() : null;
    foreach (var (name, act) in cases)
    {
        string message;
        try { act(); message = "<no failure>"; }
        catch (Exception e) { message = e.Message; }
        var diff = ErrorDiffParser.TryParseExpectedActual(message);
        Console.WriteLine($"-- {name}");
        Console.WriteLine($"   message:  {JsonSerializer.Serialize(message)}");
        Console.WriteLine($"   parser:   {(diff is null ? "no expected/actual" : $"expected={JsonSerializer.Serialize(diff.Expected)} actual={JsonSerializer.Serialize(diff.Actual)}")}");
        Console.WriteLine($"   key:      {JsonSerializer.Serialize(ClusterKey(message))}");
    }
}

// FailureText.ClusterKey and FirstLine at 4.18.1 (src/Kronikol/Reports/FailureText.cs L45 to L78), copied.
static string FirstLine(string? message)
{
    if (message is null) return "";
    var end = message.AsSpan().IndexOfAny('\r', '\n');
    var line = end < 0 ? message : message[..end];
    return string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

static string ClusterKey(string? message)
{
    var first = FirstLine(message);
    if (first.Length == 0 || message is null) return first;
    var end = message.AsSpan().IndexOfAny('\r', '\n');
    if (end < 0) return first;
    var rest = message.AsSpan(end + (message[end] == '\r' && end + 1 < message.Length && message[end + 1] == '\n' ? 2 : 1));
    var secondEnd = rest.IndexOfAny('\r', '\n');
    var second = (secondEnd < 0 ? rest : rest[..secondEnd]).ToString();
    return second.StartsWith("    should ", StringComparison.Ordinal)
        ? first + " " + string.Join(' ', second.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        : first;
}
