using Microsoft.CodeAnalysis;

namespace Kronikol.Tests.AssertionTracking;

/// <summary>
/// In Release the compiler keeps a value on the evaluation stack from one statement to the next when nothing else
/// needs it in between: <c>int result = 3; int expected = 5; result.Should().Be(expected);</c> pushes 3, stores 5
/// and then calls <c>Should</c> on the 3 still on the stack. The weave read the depth line by line and wrapped the
/// statement without that value, and the runtime rejected the method: on 4.11.0 the plan's probe threw
/// <c>InvalidProgramException</c> for this case in Release (SHOULDLY_ASSERTIONS_PLAN F21, harness
/// <c>r1/acceptance/published-4.11.0-release-run.txt</c>). The stack analysis spills the value and reloads it.
/// </summary>
public class StackAcrossStatementsTests
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
        using Kronikol.Tracking;

        [assembly: TrackAssertions]

        public static class Tests
        {
            public static void Fails()
            {
                int result = 3;
                int expected = 5;
                result.Should().Be(expected);
            }

            public static void Passes()
            {
                int result = 3;
                int expected = 3;
                result.Should().Be(expected);
            }
        }
        """;

    [Theory]
    [MemberData(nameof(Libraries))]
    public void A_subject_kept_on_the_stack_across_a_statement_is_woven(string library, OptimizationLevel optimization)
    {
        var path = WovenRun.BuildAndWeave($"StackAcross_{library}_{optimization}", Fixture(library), optimization, woven: 2);

        var (thrown, notes) = WovenRun.Run(path, "Tests", "Fails");

        // The library's own failure (whichever exception type it picks for the test framework it finds), never the
        // runtime's rejection of the method.
        Assert.NotNull(thrown);
        Assert.IsNotType<InvalidProgramException>(thrown);
        var note = Assert.Single(notes);
        Assert.False(note.Passed);
        Assert.Equal("Expected result to be 5, but found 3.", note.Message);

        (thrown, notes) = WovenRun.Run(path, "Tests", "Passes");

        Assert.Null(thrown);
        Assert.True(Assert.Single(notes).Passed);
    }
}
