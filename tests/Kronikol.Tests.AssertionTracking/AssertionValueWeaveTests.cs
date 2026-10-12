using Microsoft.CodeAnalysis;

namespace Kronikol.Tests.AssertionTracking;

/// <summary>
/// #163 through the weave (ASSERTION_VALUE_SUBSTITUTION_PLAN facts 11 to 13, and R2), in Debug and in Release: the
/// issue's statements label the values they compared, read from each argument as the assertion received it (Q3); a
/// Shouldly argument holding a join is labelled by its value (F7); constants and code with no variable in it stay as
/// written; and <c>[SuppressAssertionTracking]</c> covers the code the compiler moves out of a method or class (F8).
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

            public static void Inside_satisfy_all_conditions()
            {
                var actual = new Order();
                var expected = new Order();
                actual.ShouldSatisfyAllConditions(
                    () => actual.Colour.ToString().ShouldBe(expected.Colour.ToString()),
                    () => actual.Lines.First().ShouldBe(expected.Lines.First()));
            }
        }

        public static class Defaults
        {
            public static StringCompareShould Compare => StringCompareShould.IgnoreCase;
        }

        public static class Arguments
        {
            public static readonly Order StaticExpected = new Order();

            public static void Static_field_chain()
            {
                var actual = new Order();
                actual.Colour.ToString().ShouldBe(StaticExpected.Colour.ToString());
            }

            public static void String_constant()
            {
                var actual = new Order();
                actual.Id.ShouldBe("A1");
            }

            public static void Number_constant()
            {
                var count = 2;
                count.ShouldBe(2);
            }

            public static void Enum_constant()
            {
                var actual = new Order();
                actual.Colour.ShouldBe(Colour.Blue);
            }

            public static void Variable_alone()
            {
                var actual = new Order();
                var expected = "A1";
                actual.Id.ShouldBe(expected);
            }

            public static void Value_type_argument()
            {
                var actual = new Order();
                var expected = new Order();
                actual.Lines.Count.ShouldBe(expected.Lines.Count + 0);
            }

            public static void Comparison_argument()
            {
                var actual = new Order();
                var expected = new Order();
                actual.Id.Equals("A1").ShouldBe(expected.Id == "A1");
            }

            public static void Char_join()
            {
                // Each branch pushes an int32 constant: only the parameter's type says the value is a char.
                var actual = new Order();
                var flag = true;
                actual.Id[0].ShouldBe(flag ? 'A' : 'B');
            }

            public static void Interpolated_argument()
            {
                var actual = new Order { Id = "A1-1" };
                var expected = new Order();
                actual.Id.ShouldBe($"{expected.Id}-1");
            }

            public static void Argument_that_throws()
            {
                var actual = new Order();
                var empty = new Order { Inner = null };
                actual.Id.ShouldBe(empty.Inner!.Value);
            }

            public static void Fluent_join()
            {
                var actual = new Order();
                var expected = new Order();
                var flag = false;
                actual.Id.Should().Be(flag ? "none" : expected.Id);
            }

            public static void Fluent_because()
            {
                var actual = new Order();
                var expected = new Order();
                actual.Id.Should().Be(expected.Id.Trim(), "ids match");
            }

            public static void Fluent_because_holding_a_variable()
            {
                var actual = new Order();
                var expected = new Order();
                var who = "the buyer";
                actual.Id.Should().Be(expected.Id.Trim(), $"ids match for {who}");
            }

            public static void Shouldly_message_in_a_variable()
            {
                var actual = new Order();
                var expected = new Order();
                var why = "ids match";
                actual.Id.ShouldBe(expected.Id.Trim(), why);
            }

            public static void Argument_after_a_custom_message()
            {
                // Shouldly 4.3.0 takes the custom message before the comparison's options here.
                var actual = new Order();
                var expected = new Order();
                actual.Id.ShouldBe(expected.Id.ToLowerInvariant(), "ids", Defaults.Compare);
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

            public static void Join_in_a_custom_message_taken()
            {
                var actual = new Order();
                var expected = new Order();
                var flag = true;
                actual.Id.ShouldBe(expected.Id.Trim(), flag ? "first" : "second");
            }

            public static void Join_in_a_custom_message_not_taken()
            {
                var actual = new Order();
                var expected = new Order();
                var flag = false;
                actual.Id.ShouldBe(expected.Id.Trim(), flag ? "first" : "second");
            }
        }
        """;

    // Each argument reads the value the assertion received for it (R2, Q3): 4.14.8 read 'Blue'.ToString(), the
    // value before the call, and showed a list's First(), a Count(...) and a join's branches as code.
    public static TheoryData<string, string, string> IssueLabels => new()
    {
        { "Issue", "Call_after_a_member", "Actual colour should be 'Blue'" },
        { "Issue", "First_of_a_list", "First value of actual lines should be 'x'" },
        { "Issue", "Two_calls", "Actual colour to lower invariant() should be 'blue'" },
        { "Issue", "Fluent_call_after_a_member", "Actual colour should be 'Blue'" },
        { "Issue", "Member_after_a_call", "Response split id should be 'S1'" },
        { "Issue", "Call_named_like_a_property", "Actual lines count(l => l == \"x\") should be '1'" },
        { "Joins", "Conditional", "Actual id should be 'A1'" },
        { "Joins", "Coalesce", "Actual id should be 'A1'" },
        { "Joins", "Null_conditional", "Value should be 'v'" },
        { "Joins", "Join_before_a_custom_message", "Actual id should be 'A1'" },
        { "Joins", "Message_holding_a_variable_after_a_join", "Actual id should be 'A1'" },
        // A join in the last argument ends at the call itself: one of its branches jumps straight there.
        { "Joins", "Join_in_a_custom_message_taken", "Actual id should be 'A1'" },
        { "Joins", "Join_in_a_custom_message_not_taken", "Actual id should be 'A1'" },
        { "Arguments", "Static_field_chain", "Actual colour should be 'Blue'" },
        { "Arguments", "String_constant", "Actual id should be \"A1\"" },
        { "Arguments", "Number_constant", "Count should be 2" },
        { "Arguments", "Enum_constant", "Actual colour should be Blue" },
        { "Arguments", "Variable_alone", "Actual id should be 'A1'" },
        { "Arguments", "Value_type_argument", "Actual lines count should be '2'" },
        { "Arguments", "Char_join", "Actual id[0] should be 'A'" },
        { "Arguments", "Interpolated_argument", "Actual id should be 'A1-1'" },
        { "Arguments", "Comparison_argument", "Actual id equals(\"A1\") should be 'True'" },
        { "Arguments", "Fluent_join", "Actual id should be 'A1'" },
        { "Arguments", "Fluent_because", "Actual id should be 'A1', \"ids match\"" },
        // The plan's section 8 item 4: a variable in a because is the reason's text, never a value put inside it.
        { "Arguments", "Fluent_because_holding_a_variable", "Actual id should be 'A1', $\"ids match for {who}\"" },
        { "Arguments", "Shouldly_message_in_a_variable", "Actual id should be 'A1'" },
        { "Arguments", "Argument_after_a_custom_message", "Actual id should be 'a1', 'IgnoreCase'" },
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
    public void A_statement_inside_satisfy_all_conditions_labels_its_values()
    {
        // The issue's suite drew these inside ShouldSatisfyAllConditions as well as outside it: each condition is a
        // lambda, woven as a statement of its own.
        foreach (var optimization in new[] { OptimizationLevel.Debug, OptimizationLevel.Release })
        {
            var (thrown, notes) = WovenRun.Run(Woven(optimization), "Issue", "Inside_satisfy_all_conditions");

            Assert.True(thrown is null, $"{optimization}: {thrown}");
            Assert.Contains(notes, n => n.Label == "Actual colour should be 'Blue'");
            Assert.Contains(notes, n => n.Label == "First value of actual lines should be 'x'");
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
            Assert.Equal("Actual colour should be 'Blue'", note.Label);
            Assert.Contains("\"Blue\"", note.Message);
        }
    }

    [Fact]
    public void An_argument_the_statement_threw_before_evaluating_is_shown_as_written()
    {
        // The value's slot still holds the plain object the weave started it with, so the label reads the variable's
        // chain, which here cannot be read: as written, never a value the assertion did not receive.
        foreach (var optimization in new[] { OptimizationLevel.Debug, OptimizationLevel.Release })
        {
            var (thrown, notes) = WovenRun.Run(Woven(optimization), "Arguments", "Argument_that_throws");

            Assert.IsType<NullReferenceException>(thrown);
            var note = Assert.Single(notes);
            Assert.False(note.Passed);
            Assert.Equal("Actual id should be empty.Inner.Value", note.Label);
        }
    }

    [Theory]
    [InlineData("Fluent_because", "\"ids match\"")]
    [InlineData("Shouldly_message_in_a_variable", "why")]
    public void A_message_argument_is_not_captured_as_a_value(string method, string argument)
    {
        // FluentAssertions' because and Shouldly's customMessage are the library's to show: the weave never names
        // them among the values it passes (an argument's value is passed under its text).
        foreach (var optimization in new[] { OptimizationLevel.Debug, OptimizationLevel.Release })
        {
            using var module = Mono.Cecil.ModuleDefinition.ReadModule(Woven(optimization), new Mono.Cecil.ReaderParameters { InMemory = true });
            var woven = module.GetType("Arguments").Methods.Single(m => m.Name == method);

            Assert.Contains(woven.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr && (string)i.Operand == "expected.Id.Trim()");
            Assert.DoesNotContain(woven.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr && (string)i.Operand == argument);
        }
    }

    [Fact]
    public void A_variable_passed_alone_is_named_once()
    {
        // A bare variable is captured as a variable already (its members are read off it); the weave does not pass
        // it a second time as an argument.
        foreach (var optimization in new[] { OptimizationLevel.Debug, OptimizationLevel.Release })
        {
            using var module = Mono.Cecil.ModuleDefinition.ReadModule(Woven(optimization), new Mono.Cecil.ReaderParameters { InMemory = true });
            var woven = module.GetType("Arguments").Methods.Single(m => m.Name == "Variable_alone");

            Assert.Single(woven.Body.Instructions, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr && (string)i.Operand == "expected");
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
