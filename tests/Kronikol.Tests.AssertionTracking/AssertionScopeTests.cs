using Microsoft.CodeAnalysis;

namespace Kronikol.Tests.AssertionTracking;

/// <summary>
/// An assertion inside an <c>AssertionScope</c> does not throw when it fails: the scope collects the failure
/// and throws them all from its <c>Dispose</c>, which is in no woven statement. The weave saw a statement that
/// returned and drew it as a pass, so a failing test's diagram said every check passed
/// (SHOULDLY_ASSERTIONS_PLAN F12, measured on 4.9.0 with AwesomeAssertions 9.6.0). FluentAssertions opens such
/// a scope itself around each inspector of <c>SatisfyRespectively</c>, so a failing condition there was drawn
/// as a pass with no scope in the test at all.
/// </summary>
public class AssertionScopeTests
{
    public static TheoryData<string, OptimizationLevel> Libraries() => new()
    {
        { "FluentAssertions", OptimizationLevel.Debug },
        { "FluentAssertions", OptimizationLevel.Release },
        { "AwesomeAssertions", OptimizationLevel.Debug },
        { "AwesomeAssertions", OptimizationLevel.Release },
    };

    private static string Fixture(string library) => $$"""
        using {{library}};
        using {{library}}.Execution;
        using Kronikol.Tracking;

        [assembly: TrackAssertions]

        public class Tests
        {
            public void Scoped()
            {
                var result = 3;
                using (new AssertionScope())
                {
                    result.Should().Be(5);
                    result.Should().Be(3);
                }
            }

            public void Nested()
            {
                var result = 3;
                using (new AssertionScope())
                {
                    using (new AssertionScope())
                    {
                        result.Should().Be(5);
                    }
                    result.Should().Be(3);
                }
            }

            public void Inspectors()
            {
                var items = new[] { 1, 2 };
                items.Should().SatisfyRespectively(
                    first => first.Should().Be(1),
                    second => second.Should().Be(3));
            }

            public void HelperInScope()
            {
                var result = 3;
                using (new AssertionScope())
                {
                    Helper(result);
                    result.Should().Be(3);
                }
            }

            [SuppressAssertionTracking]
            private static void Helper(int value)
            {
                value.Should().Be(4);
            }

            public void AfterACaughtFailure()
            {
                var result = 3;
                try
                {
                    result.Should().Be(5);
                }
                catch (System.Exception)
                {
                }
                result.Should().Be(3);
            }
        }
        """;

    [Theory]
    [MemberData(nameof(Libraries))]
    public void A_failed_assertion_in_an_AssertionScope_is_drawn_failed(string library, OptimizationLevel optimization)
    {
        var path = WovenRun.BuildAndWeave($"Scope_{library}_{optimization}", Fixture(library), optimization);

        var (thrown, notes) = WovenRun.Run(path, "Tests", "Scoped");

        // The scope still throws what it always threw, untouched by the weave.
        Assert.NotNull(thrown);
        Assert.Equal("Expected result to be 5, but found 3.", thrown!.Message.Trim());
        Assert.Equal(
            [(false, "Result should be 5", "Expected result to be 5, but found 3."), (true, "Result should be 3", null)],
            notes.Select(n => (n.Passed, n.Label, n.Message)).ToArray());
    }

    [Theory]
    [MemberData(nameof(Libraries))]
    public void A_failure_a_nested_scope_hands_on_is_not_blamed_on_the_next_assertion(string library, OptimizationLevel optimization)
    {
        var path = WovenRun.BuildAndWeave($"ScopeNested_{library}_{optimization}", Fixture(library), optimization);

        var (thrown, notes) = WovenRun.Run(path, "Tests", "Nested");

        Assert.NotNull(thrown);
        Assert.Equal(
            [(false, "Result should be 5"), (true, "Result should be 3")],
            notes.Select(n => (n.Passed, n.Label)).ToArray());
    }

    [Theory]
    [MemberData(nameof(Libraries))]
    public void A_failed_inspector_is_drawn_failed(string library, OptimizationLevel optimization)
    {
        var path = WovenRun.BuildAndWeave($"ScopeInspectors_{library}_{optimization}", Fixture(library), optimization);

        var (thrown, notes) = WovenRun.Run(path, "Tests", "Inspectors");

        Assert.NotNull(thrown);
        var byLabel = notes.ToDictionary(n => n.Label);
        Assert.True(byLabel["First should be 1"].Passed);
        Assert.False(byLabel["Second should be 3"].Passed);
        Assert.Contains("Expected second to be 3, but found 2.", byLabel["Second should be 3"].Message);
        Assert.Single(notes, n => n.Label.StartsWith("Items should satisfy respectively", StringComparison.Ordinal) && !n.Passed);
    }

    [Theory]
    [MemberData(nameof(Libraries))]
    public void A_failure_uninstrumented_code_adds_to_a_scope_is_drawn_on_the_next_assertion(string library, OptimizationLevel optimization)
    {
        // The limitation the wiki states: the tracker learns of a scope's failures after each instrumented
        // assertion, so one that code outside the weave added is claimed by the next instrumented one.
        var path = WovenRun.BuildAndWeave($"ScopeHelper_{library}_{optimization}", Fixture(library), optimization);

        var (thrown, notes) = WovenRun.Run(path, "Tests", "HelperInScope");

        Assert.NotNull(thrown);
        var note = Assert.Single(notes);
        Assert.Equal((false, "Result should be 3"), (note.Passed, note.Label));
        Assert.Equal("Expected value to be 4, but found 3.", note.Message);
    }

    private const string TUnitFixture = """
        using System.Threading.Tasks;
        using TUnit.Assertions;
        using TUnit.Assertions.Extensions;
        using Kronikol.Tracking;

        [assembly: TrackAssertions]

        public class Tests
        {
            public async Task Multiple()
            {
                var result = 3;
                using (Assert.Multiple())
                {
                    await Assert.That(result).IsEqualTo(5);
                    await Assert.That(result).IsEqualTo(3);
                }
            }
        }
        """;

    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void A_failed_TUnit_assertion_in_Assert_Multiple_is_drawn_failed(OptimizationLevel optimization)
    {
        // TUnit's Assert.Multiple() defers a failure as an AssertionScope does: measured on 4.10.0 with
        // TUnit.Assertions 1.73.5, both assertions were drawn as passes and the test failed at the block's end.
        var path = WovenRun.BuildAndWeave($"ScopeTUnit_{optimization}", TUnitFixture, optimization,
            [typeof(TUnit.Assertions.Assert).Assembly.Location]);

        var (thrown, notes) = WovenRun.Run(path, "Tests", "Multiple");

        Assert.NotNull(thrown);
        Assert.Equal([false, true], notes.Select(n => n.Passed).ToArray());
        Assert.StartsWith("Expected to be 5", notes[0].Message);
    }

    [Theory]
    [MemberData(nameof(Libraries))]
    public void Outside_a_scope_a_pass_is_drawn_after_a_caught_failure(string library, OptimizationLevel optimization)
    {
        // FluentAssertions 8 leaves an implicit scope behind after a failure outside any scope. It collects
        // nothing, so the statement after it is still drawn as what it was.
        var path = WovenRun.BuildAndWeave($"ScopeAfterCatch_{library}_{optimization}", Fixture(library), optimization);

        var (thrown, notes) = WovenRun.Run(path, "Tests", "AfterACaughtFailure");

        Assert.Null(thrown);
        Assert.Equal([(false, "Result should be 5"), (true, "Result should be 3")], notes.Select(n => (n.Passed, n.Label)).ToArray());
    }
}
