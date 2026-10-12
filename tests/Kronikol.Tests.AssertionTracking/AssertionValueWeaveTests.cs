using Microsoft.CodeAnalysis;

namespace Kronikol.Tests.AssertionTracking;

/// <summary>
/// #163 through the weave (ASSERTION_VALUE_SUBSTITUTION_PLAN facts 11 to 13), in Debug and in Release: the issue's
/// statements label the values they compared, a Shouldly argument holding a join captures its values (F7), and
/// <c>[SuppressAssertionTracking]</c> covers the code the compiler moves out of a method or class (F8).
/// </summary>
public class AssertionValueWeaveTests
{
    private const string Values = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using FluentAssertions;
        using Kronikol.Tracking;
        using Shouldly;

        [assembly: TrackAssertions]

        public enum Colour { Red, Blue }

        public sealed class Inner
        {
            public Inner(string value) => Value = value;
            public string Value { get; }
        }

        public sealed class Order
        {
            public string Id { get; init; } = "A1";
            public Colour Colour { get; init; } = Colour.Blue;
            public List<string> Lines { get; init; } = new() { "x", "y" };
            public Inner? Inner { get; init; } = new Inner("v");
            public string? Missing { get; init; }
        }

        public sealed class Split
        {
            public string SplitId { get; init; } = "S1";
        }

        public static class Issue
        {
            public static void Call_after_a_member()
            {
                var actual = new Order();
                var expected = new Order();
                actual.Colour.ToString().ShouldBe(expected.Colour.ToString());
            }

            public static void First_of_a_list()
            {
                var actual = new Order();
                var expected = new Order();
                actual.Lines.First().ShouldBe(expected.Lines.First());
            }

            public static void Two_calls()
            {
                var actual = new Order();
                var expected = new Order();
                actual.Colour.ToString().ToLowerInvariant().ShouldBe(expected.Colour.ToString().ToLowerInvariant());
            }

            public static void Fluent_call_after_a_member()
            {
                var actual = new Order();
                var expected = new Order();
                actual.Colour.ToString().Should().Be(expected.Colour.ToString());
            }

            public static void Member_after_a_call()
            {
                var response = new Split();
                var splits = new List<Split> { new Split() };
                response.SplitId.ShouldBe(splits.First().SplitId);
            }

            public static void Call_named_like_a_property()
            {
                var actual = new Order();
                var expected = new Order();
                actual.Lines.Count(l => l == "x").ShouldBe(expected.Lines.Count(l => l == "x"));
            }

            public static void Failing_call_after_a_member()
            {
                var actual = new Order { Colour = Colour.Red };
                var expected = new Order();
                actual.Colour.ToString().ShouldBe(expected.Colour.ToString());
            }
        }

        public static class Joins
        {
            public static void Conditional()
            {
                var actual = new Order();
                var expected = new Order();
                var flag = true;
                actual.Id.ShouldBe(flag ? expected.Id : "none");
            }

            public static void Coalesce()
            {
                var actual = new Order();
                var expected = new Order();
                actual.Id.ShouldBe(expected.Missing ?? expected.Id);
            }

            public static void Null_conditional()
            {
                var expected = new Order();
                var value = "v";
                value.ShouldBe(expected.Inner?.Value);
            }

            public static void Join_before_a_custom_message()
            {
                var actual = new Order();
                var expected = new Order();
                var flag = true;
                actual.Id.ShouldBe(flag ? expected.Id : "none", "the id");
            }

            public static void Message_holding_a_variable_after_a_join()
            {
                var actual = new Order();
                var expected = new Order();
                var flag = true;
                var why = "the id";
                actual.Id.ShouldBe(flag ? expected.Id : "none", $"{why} matters");
            }
        }
        """;

    public static TheoryData<string, string, string> IssueLabels => new()
    {
        { "Issue", "Call_after_a_member", "Actual colour should be 'Blue'.ToString()" },
        { "Issue", "First_of_a_list", "First value of actual lines should be expected.Lines.First()" },
        { "Issue", "Two_calls", "Actual colour to lower invariant() should be 'Blue'.ToString().ToLowerInvariant()" },
        { "Issue", "Fluent_call_after_a_member", "Actual colour should be 'Blue'.ToString()" },
        { "Issue", "Member_after_a_call", "Response split id should be splits.First().SplitId" },
        { "Issue", "Call_named_like_a_property", "Actual lines count(l => l == \"x\") should be [ expected.Lines.Count(l => l == \"x\") ]" },
        { "Joins", "Conditional", "Actual id should be 'True' ? 'A1' : \"none\"" },
        { "Joins", "Coalesce", "Actual id should be 'null' ?? 'A1'" },
        { "Joins", "Null_conditional", "Value should be 'v'" },
        { "Joins", "Join_before_a_custom_message", "Actual id should be 'True' ? 'A1' : \"none\"" },
        { "Joins", "Message_holding_a_variable_after_a_join", "Actual id should be 'True' ? 'A1' : \"none\"" },
    };

    [Theory]
    [MemberData(nameof(IssueLabels))]
    public void A_woven_statement_labels_the_values_it_compared(string type, string method, string label)
    {
        foreach (var optimization in new[] { OptimizationLevel.Debug, OptimizationLevel.Release })
        {
            var (thrown, notes) = WovenRun.Run(Woven(optimization), type, method);

            Assert.True(thrown is null, $"{optimization}: {thrown}");
            Assert.Equal(label, Assert.Single(notes).Label);
        }
    }

    [Fact]
    public void A_failing_statement_labels_the_expected_value_its_message_names()
    {
        foreach (var optimization in new[] { OptimizationLevel.Debug, OptimizationLevel.Release })
        {
            var (thrown, notes) = WovenRun.Run(Woven(optimization), "Issue", "Failing_call_after_a_member");

            Assert.NotNull(thrown);
            var note = Assert.Single(notes);
            Assert.False(note.Passed);
            Assert.Equal("Actual colour should be 'Blue'.ToString()", note.Label);
            Assert.Contains("\"Blue\"", note.Message);
        }
    }

    private const string Suppression = """
        using System;
        using System.Threading.Tasks;
        using Kronikol.Tracking;
        using Shouldly;

        [assembly: TrackAssertions]

        public static class Methods
        {
            [SuppressAssertionTracking]
            public static void Own_statement()
            {
                var x = 1;
                x.ShouldBe(1);
            }

            [SuppressAssertionTracking]
            public static void Lambda()
            {
                var expected = 1;
                Action check = () => expected.ShouldBe(1);
                check();
            }

            [SuppressAssertionTracking]
            public static void Lambda_in_a_lambda()
            {
                var expected = 1;
                Action outer = () =>
                {
                    Action inner = () => expected.ShouldBe(1);
                    inner();
                };
                outer();
            }

            [SuppressAssertionTracking]
            public static void Static_lambda()
            {
                Action check = static () => 1.ShouldBe(1);
                check();
            }

            [SuppressAssertionTracking]
            public static void Local_function()
            {
                var expected = 1;
                void Check() => expected.ShouldBe(1);
                Check();
            }

            [SuppressAssertionTracking]
            public static async Task Async_method()
            {
                await Task.Yield();
                var x = 1;
                x.ShouldBe(1);
            }

            [SuppressAssertionTracking]
            public static void Track_that()
            {
                var expected = 1;
                Track.That(() => expected.ShouldBe(1));
            }

            public static void Unsuppressed_lambda()
            {
                var expected = 1;
                Action check = () => expected.ShouldBe(1);
                check();
            }

            public static void Unsuppressed_static_lambda()
            {
                Action check = static () => 2.ShouldBe(2);
                check();
            }
        }

        [SuppressAssertionTracking]
        public static class SuppressedType
        {
            public static void Own_statement()
            {
                var x = 1;
                x.ShouldBe(1);
            }

            public static async Task Async_method()
            {
                await Task.Yield();
                var x = 1;
                x.ShouldBe(1);
            }

            public static void Lambda()
            {
                var expected = 1;
                Action check = () => expected.ShouldBe(1);
                check();
            }

            public static class Nested
            {
                public static void Own_statement()
                {
                    var x = 1;
                    x.ShouldBe(1);
                }
            }
        }
        """;

    // Each case's notes when run: none where the attribute covers the code, Track.That's own note where it is called
    // in a suppressed method, and the woven note where nothing is suppressed.
    public static TheoryData<string, string, int> SuppressionCases => new()
    {
        { "Methods", "Own_statement", 0 },
        { "Methods", "Lambda", 0 },
        { "Methods", "Lambda_in_a_lambda", 0 },
        { "Methods", "Static_lambda", 0 },
        { "Methods", "Local_function", 0 },
        { "Methods", "Async_method", 0 },
        { "Methods", "Track_that", 1 },
        { "SuppressedType", "Own_statement", 0 },
        { "SuppressedType", "Async_method", 0 },
        { "SuppressedType", "Lambda", 0 },
        { "SuppressedType+Nested", "Own_statement", 0 },
        { "Methods", "Unsuppressed_lambda", 1 },
        { "Methods", "Unsuppressed_static_lambda", 1 },
    };

    [Theory]
    [MemberData(nameof(SuppressionCases))]
    public void Suppression_reaches_lambdas_local_functions_state_machines_and_nested_types(string type, string method, int notes)
    {
        foreach (var optimization in new[] { OptimizationLevel.Debug, OptimizationLevel.Release })
        {
            var (thrown, drawn) = WovenRun.Run(Suppressed(optimization), type, method);

            Assert.True(thrown is null, $"{optimization}: {thrown}");
            Assert.True(drawn.Length == notes, $"{optimization}: {string.Join(" | ", drawn.Select(n => n.Label))}");
        }
    }

    private static readonly Dictionary<(string, OptimizationLevel), string> Built = [];
    private static readonly object Gate = new();

    private static string Woven(OptimizationLevel optimization) => Build("AssertionValues", Values, optimization);

    private static string Suppressed(OptimizationLevel optimization) => Build("AssertionSuppression", Suppression, optimization);

    private static string Build(string name, string source, OptimizationLevel optimization)
    {
        lock (Gate)
        {
            if (!Built.TryGetValue((name, optimization), out var path))
                Built[(name, optimization)] = path = WovenRun.BuildAndWeave($"{name}_{optimization}", source, optimization);
            return path;
        }
    }
}
