using System.Reflection;
using Kronikol;
using Kronikol.Tracking;
using Shouldly;
#if CONTROL
using AwesomeAssertions;
using AwesomeAssertions.Execution;
#endif

[assembly: TrackAssertions]

// Each case runs as its own "test": the resolver names it, and the notes it recorded are read back by that id.
var current = "";
Track.TestIdResolver = () => current;

var woven = typeof(Cases).Assembly.GetTypes().Any(t => t.Name == "__AssertionTrackingWeaved__");
Console.WriteLine($"Kronikol {typeof(Track).Assembly.GetName().Version} · woven: {woven}");

foreach (var m in typeof(Cases).GetMethods(BindingFlags.Public | BindingFlags.Static).OrderBy(m => m.MetadataToken))
{
    current = "probe-" + m.Name;
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

public record Order(int Id, string Name, List<int> Items);

public static class Cases
{
    public static void ShouldBe_passes()
    {
        int result = 5;
        int expected = 5;
        result.ShouldBe(expected);
    }
    public static void ShouldBe_fails()
    {
        int result = 3;
        int expected = 5;
        result.ShouldBe(expected);
    }
    public static void ShouldNotBeNull_fails()
    {
        string? name = null;
        name.ShouldNotBeNull();
    }
    public static void ShouldBeTrue_fails()
    {
        bool flag = false;
        flag.ShouldBeTrue();
    }
    public static void ShouldNotBeEmpty_fails()
    {
        var items = new List<int>();
        items.ShouldNotBeEmpty();
    }
    public static void ShouldBe_with_custom_message_fails()
    {
        int result = 3;
        result.ShouldBe(5, "custom message");
    }
    public static void ShouldBe_array_initializer_passes()
    {
        var items = new List<int> { 1, 2 };
        items.ShouldBe(new[] { 1, 2 });
    }
    public static void ShouldBe_null_conditional_passes()
    {
        Order? order = new(1, "a", []);
        order?.Name.ShouldBe("a");
    }

    public static void ShouldSatisfyAllConditions_two_fail()
    {
        var order = new Order(1, "a", []);
        order.ShouldSatisfyAllConditions(
            () => order.Id.ShouldBe(2),
            () => order.Name.ShouldBe("b"));
    }

    public static void Should_Throw_passes()
    {
        var ex = Should.Throw<InvalidOperationException>(() => throw new InvalidOperationException("boom"));
        ex.Message.ShouldBe("boom");
    }

    public static async Task Should_ThrowAsync_fails()
    {
        await Should.ThrowAsync<InvalidOperationException>(async () => await Task.Yield());
    }

#if CONTROL
    public static void Control_Should_Be_fails()
    {
        int result = 3;
        int expected = 5;
        result.Should().Be(expected);
    }

    // The label formatter drops every '!', a string literal's included.
    public static void Control_label_keeps_a_bang_in_a_literal()
    {
        string greeting = "Hi!";
        greeting.Should().Be("Hi!");
    }

    // Assertions inside lambda arguments, one per line: the label is read from the line, not the statement.
    public static void Control_lambda_conditions()
    {
        var items = new List<int> { 1, 2 };
        items.Should().SatisfyRespectively(
            first => first.Should().Be(1),
            second => second.Should().Be(2));
    }

    // Inside an AssertionScope a failed assertion does not throw until the scope ends.
    public static void Control_AssertionScope_fails()
    {
        int result = 3;
        using (new AssertionScope())
        {
            result.Should().Be(5);
            result.Should().Be(3);
        }
    }
#endif
}
