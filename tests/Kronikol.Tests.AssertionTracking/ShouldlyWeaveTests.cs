using Kronikol.AssertionTracking;
using Microsoft.CodeAnalysis;

namespace Kronikol.Tests.AssertionTracking;

/// <summary>
/// Shouldly (#141, SHOULDLY_ASSERTIONS_PLAN R2): the plan's eleven probe cases, woven in Debug and in Release. Each
/// case is one statement per line, as Shouldly reads the failing line's code for its message (plan section 10, trap 4).
/// </summary>
public class ShouldlyWeaveTests
{
    private const string Cases = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Kronikol.Tracking;
        using Shouldly;

        [assembly: TrackAssertions]

        public record Order(int Id, string Name);

        public static class Cases
        {
            public static void ShouldBe_passes()
            {
                int result = 3;
                result.ShouldBe(3);
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
                var items = new[] { 1, 2 };
                items.ShouldBe(new[] { 1, 2 });
            }

            public static void ShouldBe_null_conditional_passes()
            {
                Order? order = new Order(1, "a");
                order?.Name.ShouldBe("a");
            }

            public static void ShouldSatisfyAllConditions_two_fail()
            {
                var order = new Order(1, "a");
                order.ShouldSatisfyAllConditions(
                    () => order.Id.ShouldBe(2),
                    () => order.Name.ShouldBe("b"));
            }

            public static void Should_Throw_passes()
            {
                Should.Throw<InvalidOperationException>(() => throw new InvalidOperationException());
            }

            public static async Task Should_ThrowAsync_fails()
            {
                await Should.ThrowAsync<InvalidOperationException>(async () => await Task.Yield());
            }
        }
        """;

    // Each case: whether it throws, and the outcome of each note it draws, in the order drawn.
    public static TheoryData<string, bool, bool[]> Outcomes() => new()
    {
        { "ShouldBe_passes", false, [true] },
        { "ShouldBe_fails", true, [false] },
        { "ShouldNotBeNull_fails", true, [false] },
        { "ShouldBeTrue_fails", true, [false] },
        { "ShouldNotBeEmpty_fails", true, [false] },
        { "ShouldBe_with_custom_message_fails", true, [false] },
        { "ShouldBe_array_initializer_passes", false, [true] },
        { "ShouldBe_null_conditional_passes", false, [true] },
        // Q4: a row per condition, then the outer call's row.
        { "ShouldSatisfyAllConditions_two_fail", true, [false, false, false] },
        { "Should_Throw_passes", false, [true] },
        { "Should_ThrowAsync_fails", true, [false] },
    };

    // Each case's labels, in the order drawn (plan section 3.5: by rule, the words after "Should" split on case).
    public static TheoryData<string, string[]> Labels() => new()
    {
        { "ShouldBe_passes", ["Result should be 3"] },
        { "ShouldBe_fails", ["Result should be '5'"] },
        { "ShouldNotBeNull_fails", ["Name should not be null"] },
        { "ShouldBeTrue_fails", ["Flag should be true"] },
        { "ShouldNotBeEmpty_fails", ["Items should not be empty"] },
        { "ShouldBe_with_custom_message_fails", ["Result should be 5"] },
        { "ShouldBe_array_initializer_passes", ["Items should be new[] { 1, 2 }"] },
        { "ShouldBe_null_conditional_passes", ["Order name should be \"a\""] },
        { "ShouldSatisfyAllConditions_two_fail", ["Order id should be 2", "Order name should be \"b\"", "Order should satisfy all conditions"] },
        { "Should_Throw_passes", ["Should throw InvalidOperationException"] },
        { "Should_ThrowAsync_fails", ["Should throw InvalidOperationException"] },
    };

    [Theory]
    [MemberData(nameof(Labels))]
    public void Each_Shouldly_case_is_labelled_as_a_sentence(string method, string[] labels)
    {
        foreach (var optimization in new[] { OptimizationLevel.Debug, OptimizationLevel.Release })
        {
            var (_, notes) = WovenRun.Run(Woven(optimization), "Cases", method);

            Assert.Equal(labels, notes.Select(n => n.Label).ToArray());
        }
    }

    public static TheoryData<OptimizationLevel> Builds() => [OptimizationLevel.Debug, OptimizationLevel.Release];

    [Theory]
    [MemberData(nameof(Builds))]
    public void A_Shouldly_only_assembly_is_woven(OptimizationLevel optimization)
    {
        // Eleven statements, and the two conditions inside ShouldSatisfyAllConditions.
        WovenRun.BuildAndWeave($"ShouldlyOnly_{optimization}", Cases, optimization, woven: 13);
    }

    [Theory]
    [MemberData(nameof(Outcomes))]
    public void Each_Shouldly_case_records_its_outcome(string method, bool throws, bool[] outcomes)
    {
        foreach (var optimization in new[] { OptimizationLevel.Debug, OptimizationLevel.Release })
        {
            var path = Woven(optimization);

            var (thrown, notes) = WovenRun.Run(path, "Cases", method);

            Assert.True(throws == thrown is not null, $"{optimization}: {thrown}");
            Assert.IsNotType<InvalidProgramException>(thrown);
            Assert.Equal(outcomes, notes.Select(n => n.Passed).ToArray());
        }
    }

    [Theory]
    [MemberData(nameof(Builds))]
    public void A_Shouldly_failure_note_carries_Shouldlys_message(OptimizationLevel optimization)
    {
        var (thrown, notes) = WovenRun.Run(Woven(optimization), "Cases", "ShouldBe_fails");

        var note = Assert.Single(notes);
        Assert.Equal("result\n    should be\n5\n    but was\n3", note.Message?.ReplaceLineEndings("\n"));
        Assert.Equal(thrown!.Message.ReplaceLineEndings("\n"), note.Message?.ReplaceLineEndings("\n"));
    }

    [Theory]
    [MemberData(nameof(Outcomes))]
    public void A_woven_Shouldly_message_equals_the_unwoven_one(string method, bool throws, bool[] outcomes)
    {
        _ = outcomes;
        if (!throws)
            return;
        foreach (var optimization in new[] { OptimizationLevel.Debug, OptimizationLevel.Release })
        {
            // Shouldly reads the failing line's code for its message, so a sequence point the weave left on the
            // wrong instruction shows here (F7). The message to keep is the Debug build's: in Release the unwoven
            // message is itself wrong wherever the compiler kept the subject on the stack from the line above, as the
            // optimised frame maps to that line ("3;⏎ int expected = 5;⏎ result⏎ should be…"), and the weave, which
            // empties the stack at the statement's own line, reads it right.
            var (expected, _) = WovenRun.Run(Unwoven(OptimizationLevel.Debug), "Cases", method);
            var (actual, _) = WovenRun.Run(Woven(optimization), "Cases", method);

            Assert.Equal(expected!.Message, actual!.Message);
        }
    }

    private const string Values = """
        using Kronikol.Tracking;
        using Shouldly;

        [assembly: TrackAssertions]

        public record Order(int Id, string Name);

        public static class Values
        {
            // The value is the argument after the subject; the subject itself is not one (FluentAssertions' parity).
            public static void Argument_after_the_subject()
            {
                var total = 3;
                var expected = 3;
                total.ShouldBe(expected);
            }

            // A custom message held in a variable is Shouldly's to show, never a value.
            public static void Custom_message_in_a_variable()
            {
                var total = 3;
                var message = "the total";
                total.ShouldBe(3, message);
            }

            // Passed by name, the message stays in the statement's text for the label to drop.
            public static void Custom_message_by_name()
            {
                var total = 3;
                var message = "the total";
                total.ShouldBe(3, customMessage: message);
            }

            // A condition lambda's closure value is read through the closure.
            public static void Condition_closure()
            {
                var order = new Order(2, "b");
                var expectedId = 2;
                order.ShouldSatisfyAllConditions(
                    () => order.Id.ShouldBe(expectedId));
            }
        }
        """;

    [Theory]
    [MemberData(nameof(Builds))]
    public void Shouldly_values_are_the_arguments_after_the_subject(OptimizationLevel optimization)
    {
        var path = WovenRun.BuildAndWeave($"ShouldlyValues_{optimization}", Values, optimization);

        var (_, notes) = WovenRun.Run(path, "Values", "Argument_after_the_subject");

        Assert.Equal("Total should be '3'", Assert.Single(notes).Label);
    }

    [Theory]
    [MemberData(nameof(Builds))]
    public void A_custom_message_is_not_captured(OptimizationLevel optimization)
    {
        var path = WovenRun.BuildAndWeave($"ShouldlyMessage_{optimization}", Values, optimization);

        foreach (var name in new[] { "Custom_message_in_a_variable", "Custom_message_by_name" })
        {
            var (_, notes) = WovenRun.Run(path, "Values", name);

            Assert.Equal("Total should be 3", Assert.Single(notes).Label);
            // Nor read as a value. The label never shows one (a positional message leaves the statement's text, a
            // named one is dropped from the label), so what the weave hands Track is read from the woven method: a
            // value the weave reads is named by an ldstr.
            using var module = Mono.Cecil.ModuleDefinition.ReadModule(path, new Mono.Cecil.ReaderParameters { InMemory = true });
            var method = module.GetType("Values").Methods.Single(m => m.Name == name);
            Assert.DoesNotContain(method.Body.Instructions,
                i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr && (string)i.Operand == "message");
        }
    }

    [Theory]
    [MemberData(nameof(Builds))]
    public void A_condition_lambdas_closure_value_is_captured(OptimizationLevel optimization)
    {
        var path = WovenRun.BuildAndWeave($"ShouldlyClosure_{optimization}", Values, optimization);

        var (_, notes) = WovenRun.Run(path, "Values", "Condition_closure");

        Assert.Equal(["Order id should be '2'", "Order should satisfy all conditions"], notes.Select(n => n.Label).ToArray());
    }

    private const string Helpers = """
        using Kronikol.Tracking;
        using Shouldly;

        [assembly: TrackAssertions]

        public record Order(int Id, string Name);

        // Shouldly's way to write a custom assertion: its calls are one row each, and its body is not woven (Q5).
        [ShouldlyMethods]
        public static class OrderAssertions
        {
            public static void ShouldBeValid(this Order order)
            {
                order.Id.ShouldBeGreaterThan(0);
                order.Name.ShouldNotBeNullOrEmpty();
            }
        }

        // A suite's own method that starts with "Should" is not an assertion: no name rule (plan section 3.1).
        public static class NotShouldly
        {
            public static void ShouldBeTheSame<T>(this T actual, T expected)
            {
                if (!Equals(actual, expected))
                    throw new System.Exception("differs");
            }
        }

        public static class Helpers
        {
            public static void Helper_is_one_row()
            {
                var order = new Order(0, "a");
                order.ShouldBeValid();
            }

            public static void Not_an_assertion()
            {
                var total = 3;
                total.ShouldBeTheSame(3);
            }
        }
        """;

    [Theory]
    [MemberData(nameof(Builds))]
    public void A_ShouldlyMethods_helper_is_one_row(OptimizationLevel optimization)
    {
        var path = WovenRun.BuildAndWeave($"ShouldlyHelper_{optimization}", Helpers, optimization);

        var (thrown, notes) = WovenRun.Run(path, "Helpers", "Helper_is_one_row");

        Assert.NotNull(thrown);
        var note = Assert.Single(notes);
        Assert.False(note.Passed);
        Assert.Equal("Order should be valid", note.Label);
    }

    [Theory]
    [MemberData(nameof(Builds))]
    public void Its_body_is_not_woven(OptimizationLevel optimization)
    {
        // One statement in the whole assembly: the helper's call. Neither the helper's two checks nor the call to a
        // method that only looks like Shouldly's.
        WovenRun.BuildAndWeave($"ShouldlyHelperBody_{optimization}", Helpers, optimization, woven: 1);
    }

    [Theory]
    [MemberData(nameof(Builds))]
    public void A_users_ShouldBe_method_outside_Shouldly_is_not_an_assertion(OptimizationLevel optimization)
    {
        var path = WovenRun.BuildAndWeave($"ShouldlyLookalike_{optimization}", Helpers, optimization);

        var (thrown, notes) = WovenRun.Run(path, "Helpers", "Not_an_assertion");

        Assert.Null(thrown);
        Assert.Empty(notes);
    }

    private const string Shapes = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Kronikol.Tracking;
        using Shouldly;
        using Shouldly.ShouldlyExtensionMethods;

        [assembly: TrackAssertions]

        public record Order(int Id, string Name);

        public readonly record struct Point(int X, int Y);

        [Flags]
        public enum Options { None = 0, A = 1, B = 2 }

        public static class Shapes
        {
            private static readonly object Gate = new();

            public static void ShouldBeOfType_result_used()
            {
                object value = "text";
                var text = value.ShouldBeOfType<string>();
                text.Length.ShouldBe(4);
            }

            public static void ShouldContain_predicate()
            {
                var items = new List<int> { 1, 2 };
                items.ShouldContain(x => x > 1);
            }

            public static void ShouldAllBe_predicate()
            {
                var items = new List<int> { 1, 2 };
                items.ShouldAllBe(x => x > 0);
            }

            public static void A_parameter() => Check(3);

            private static void Check(int value)
            {
                value.ShouldBe(3);
            }

            public static void A_generic_T() => Same(5, 5);

            private static void Same<T>(T actual, T expected)
            {
                actual.ShouldBe(expected);
            }

            public static void A_struct()
            {
                var point = new Point(1, 2);
                point.ShouldBe(new Point(1, 2));
            }

            public static void A_record_with()
            {
                var order = new Order(1, "a");
                var renamed = order with { Name = "b" };
                renamed.Name.ShouldBe("b");
            }

            public static void ShouldHaveFlag_in_another_namespace()
            {
                var options = Options.A | Options.B;
                options.ShouldHaveFlag(Options.B);
            }

            public static void DynamicShould_HaveProperty()
            {
                dynamic thing = new System.Dynamic.ExpandoObject();
                thing.Name = "a";
                DynamicShould.HaveProperty((object)thing, "Name");
            }

            public static void Inside_lock()
            {
                lock (Gate)
                {
                    3.ShouldBe(3);
                }
            }

            public static void Inside_try_finally()
            {
                var touched = false;
                try
                {
                    3.ShouldBe(3);
                }
                finally
                {
                    touched = true;
                }
                _ = touched;
            }

            public static void In_a_switch_arm()
            {
                var kind = 2;
                switch (kind)
                {
                    case 1:
                        1.ShouldBe(1);
                        break;
                    default:
                        2.ShouldBe(2);
                        break;
                }
            }

            public static void A_ternary_argument()
            {
                var flag = true;
                3.ShouldBe(flag ? 3 : 4);
            }

            public static async Task Async_without_an_await()
            {
                3.ShouldBe(3);
            }

            public static async Task Async_after_an_await()
            {
                await Task.Yield();
                3.ShouldBe(3);
            }

            public static async Task ShouldThrowAsync_on_a_task()
            {
                var task = Task.FromException(new InvalidOperationException());
                await task.ShouldThrowAsync<InvalidOperationException>();
            }

            public static async Task An_await_inside_an_argument()
            {
                3.ShouldBe(await Task.FromResult(3));
            }
        }
        """;

    // Each shape: the notes it draws, all passes; an await inside an argument is left unwoven (plan section 3.8).
    public static TheoryData<string, int> ShapeNotes() => new()
    {
        { "ShouldBeOfType_result_used", 2 },
        { "ShouldContain_predicate", 1 },
        { "ShouldAllBe_predicate", 1 },
        { "A_parameter", 1 },
        { "A_generic_T", 1 },
        { "A_struct", 1 },
        { "A_record_with", 1 },
        { "ShouldHaveFlag_in_another_namespace", 1 },
        { "DynamicShould_HaveProperty", 1 },
        { "Inside_lock", 1 },
        { "Inside_try_finally", 1 },
        { "In_a_switch_arm", 1 },
        { "A_ternary_argument", 1 },
        { "Async_without_an_await", 1 },
        { "Async_after_an_await", 1 },
        { "ShouldThrowAsync_on_a_task", 1 },
        { "An_await_inside_an_argument", 0 },
    };

    private static readonly Dictionary<OptimizationLevel, string> BuiltShapes = [];

    [Theory]
    [MemberData(nameof(ShapeNotes))]
    public void Each_Shouldly_shape_runs_woven(string method, int notes)
    {
        foreach (var optimization in new[] { OptimizationLevel.Debug, OptimizationLevel.Release })
        {
            string path;
            lock (Gate)
            {
                if (!BuiltShapes.TryGetValue(optimization, out path!))
                    BuiltShapes[optimization] = path = WovenRun.BuildAndWeave($"ShouldlyShapes_{optimization}", Shapes, optimization);
            }

            var (thrown, drawn) = WovenRun.Run(path, "Shapes", method);

            Assert.True(thrown is null, $"{optimization}: {thrown}");
            Assert.Equal(notes, drawn.Length);
            Assert.All(drawn, note => Assert.True(note.Passed, $"{optimization}: {note.Label}"));
        }
    }

    private static readonly Dictionary<OptimizationLevel, string> Built = [];
    private static readonly object Gate = new();

    private static readonly Dictionary<OptimizationLevel, string> BuiltUnwoven = [];

    private static string Unwoven(OptimizationLevel optimization)
    {
        lock (Gate)
        {
            if (!BuiltUnwoven.TryGetValue(optimization, out var path))
                BuiltUnwoven[optimization] = path = TestAssemblyBuilder.Build($"ShouldlyUnwoven_{optimization}", Cases, optimization);
            return path;
        }
    }

    private static string Woven(OptimizationLevel optimization)
    {
        lock (Gate)
        {
            if (!Built.TryGetValue(optimization, out var path))
                Built[optimization] = path = WovenRun.BuildAndWeave($"ShouldlyCases_{optimization}", Cases, optimization);
            return path;
        }
    }
}
