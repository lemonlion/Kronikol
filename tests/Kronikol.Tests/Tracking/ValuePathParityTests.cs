using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

/// <summary>
/// One assertion draws one label whichever path read its values: the weave's (<c>Track.AssertionPassedWithValues</c>,
/// given the variables' values) or <c>Track.That</c>'s (<see cref="ClosureValueResolver"/>, reading the lambda's
/// closure). The two read values by different rules until #163, so one assertion run both ways drew two labels
/// (ASSERTION_VALUE_SUBSTITUTION_PLAN F6). An operand of an arithmetic operator is the one designed difference
/// (the plan's §3.5): <c>Track.That</c> leaves it as code. No shape here has one.
/// </summary>
[Collection("DiagramsFetcher")]
public class ValuePathParityTests : IDisposable
{
    private readonly string _testId = $"ValuePathParityTests.{Guid.NewGuid():N}";

    public void Dispose()
    {
        Track.TestIdResolver = null;
    }

    public static TheoryData<string> Shapes => new()
    {
        "() => actual.Should().Be(expected.Id)",
        "() => actual.Should().Be(expected.Colour)",
        "() => actual.Should().Be(expected.Colour.ToString())",
        "() => actual.Should().Be(expected.Colour.ToString(\"G\"))",
        "() => actual.Should().Be(expected.Lines.First())",
        "() => actual.Should().Be(expected.Lines.Count)",
        "() => actual.Should().Be(expected.Lines.Count())",
        "() => actual.Should().Be(expected.Lines.Count(l => l == \"x\"))",
        "() => actual.Should().Be(expected.Lines[0])",
        "() => actual.Should().Be(expected.Plain)",
        "() => actual.Should().Be(expected.Inner!.Value)",
        "() => actual.Should().Be(expected.Inner?.Value)",
        "() => actual.Should().Be(expected.Box.Inner.Value.Length)",
        "() => actual.Should().Be(expected.Missing)",
        "() => actual.Should().Be(expected.NoSuchMember)",
        "() => actual.Should().Be(empty.Inner.Value)",
        "() => actual.Should().Be(empty.Inner?.Value)",
        "() => actual.Should().Be(faulty.Throws)",
        "() => actual.Should().Be(splits.First().Id)",
        "() => actual.Should().Be(id.Trim())",
        "() => actual.Should().Be(id)",
        "() => actual.Should().Be(string.Concat(expected.Id))",
        "() => actual.Should().BeOneOf(faulty.Throws, expected.Id)",
        "() => actual.ShouldBe(expected.Colour.ToString())",
        "() => actual.ShouldBe(expected.Lines.Count(l => l == \"x\"))",
        "() => actual.ShouldBe(nameof(expected.Colour))",
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Both_paths_label_one_assertion_alike(string expression)
    {
        var expected = new Order("A1", Colour.Blue, ["x", "y"])
        {
            Inner = new Inner("v"),
            Box = new Box { Inner = new Inner("v") },
        };
        var empty = expected with { Inner = null };
        var faulty = new Faulty();
        var splits = new List<Order> { expected };
        var id = "A1";
        object? actual = null;

        using (TestIdentityScope.Begin(_testId, _testId))
        {
            Track.That(() => Use(actual, expected, empty, faulty, splits, id), expression);
            Track.AssertionPassedWithValues(expression,
                ["actual", "expected", "empty", "faulty", "splits", "id"],
                [actual, expected, empty, faulty, splits, id],
                "Test.cs", 1);
        }

        var labels = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == _testId && l.PlantUml is not null && l.PlantUml.Contains("<<assertionNote>>"))
            .Select(l => l.PlantUml!.Split('\n').Single(line => line.StartsWith(Track.PassSymbol + " ", StringComparison.Ordinal)))
            .ToList();
        Assert.Equal(2, labels.Count);
        Assert.Equal(labels[0], labels[1]);
    }

    private static void Use(params object?[] _) { }

    private enum Colour { Red, Blue }

    private sealed record Order(string Id, Colour Colour, List<string> Lines)
    {
        public Inner? Inner { get; init; }
        public Box? Box { get; init; }
        public Plain Plain { get; init; } = new();
        public string? Missing { get; init; }
    }

    private sealed class Inner(string value)
    {
        public string Value { get; } = value;
        public override string ToString() => $"Inner({Value})";
    }

    private sealed class Box
    {
        public Inner? Inner { get; init; }
    }

    private sealed class Plain
    {
        public string Name { get; init; } = "plain";
    }

    private sealed class Faulty
    {
        public string Throws => throw new InvalidOperationException("boom");
    }
}
