using Microsoft.CodeAnalysis;

namespace Kronikol.Tests.AssertionTracking;

/// <summary>
/// The text a woven statement is labelled with is its sequence point's own columns. It was whole source lines,
/// so an assertion that shares its line took its neighbours' code: a condition lambda on its own line was
/// labelled with the lambda's parameter and arrow, as <c>First => first should be 1</c>
/// (SHOULDLY_ASSERTIONS_PLAN F11).
/// </summary>
public class StatementTextTests
{
    [Theory]
    [InlineData(OptimizationLevel.Debug)]
    [InlineData(OptimizationLevel.Release)]
    public void A_lambda_condition_on_its_own_line_is_labelled_by_its_body(OptimizationLevel optimization)
    {
        var path = WovenRun.BuildAndWeave($"LambdaConditionText_{optimization}", """
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public void Method()
                {
                    var items = new[] { 1, 2 };
                    items.Should().SatisfyRespectively(
                        first => first.Should().Be(1),
                        second => second.Should().Be(2));
                }
            }
            """, optimization);

        var (thrown, notes) = WovenRun.Run(path, "Tests", "Method");

        Assert.Null(thrown);
        var labels = notes.Select(n => n.Label).ToArray();
        Assert.Contains("First should be 1", labels);
        Assert.Contains("Second should be 2", labels);
        Assert.DoesNotContain(labels, l => l.Contains("=>") && !l.StartsWith("Items should satisfy respectively", StringComparison.Ordinal));
    }

    [Fact]
    public void A_statement_sharing_its_line_is_labelled_by_its_own_columns()
    {
        var path = WovenRun.BuildAndWeave("SharedLineText", """
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public void Method()
                {
                    var ok = true;
                    var total = 3;
                    if (ok) total.Should().Be(3);
                }
            }
            """);

        var (thrown, notes) = WovenRun.Run(path, "Tests", "Method");

        Assert.Null(thrown);
        Assert.Equal("Total should be 3", Assert.Single(notes).Label);
    }

    [Fact]
    public void A_statement_alone_on_its_line_reads_as_before()
    {
        var path = WovenRun.BuildAndWeave("AloneOnLineText", """
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public void Method()
                {
                    var total = 3;
                    total.Should().Be(3);
                    total.Should()
                        .BeGreaterThan(2);
                }
            }
            """);

        var (thrown, notes) = WovenRun.Run(path, "Tests", "Method");

        Assert.Null(thrown);
        Assert.Equal(["Total should be 3", "Total should be greater than 2"], notes.Select(n => n.Label).ToArray());
    }

    [Fact]
    public void A_bracket_inside_a_literal_does_not_pull_in_the_next_lines()
    {
        // The search for a statement's end counted brackets inside string literals, so "(" read as an
        // unclosed call and the following lines were joined onto the label (found while fixing F11).
        var path = WovenRun.BuildAndWeave("BracketLiteralText", """
            using FluentAssertions;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public void Method()
                {
                    var open = "(";
                    var total = 3;
                    open.Should().Be("(");
                    total.Should().Be(3);
                }
            }
            """);

        var (thrown, notes) = WovenRun.Run(path, "Tests", "Method");

        Assert.Null(thrown);
        Assert.Equal(["Open should be \"(\"", "Total should be 3"], notes.Select(n => n.Label).ToArray());
    }
}
