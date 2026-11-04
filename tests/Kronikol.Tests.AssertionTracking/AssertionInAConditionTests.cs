using System.Reflection;
using Kronikol.AssertionTracking;
using Kronikol.Tracking;
using static Kronikol.Tests.AssertionTracking.TestAssemblyBuilder;

namespace Kronikol.Tests.AssertionTracking;

/// <summary>
/// An assertion whose result a condition uses (STEP_METHOD_SHAPES_PLAN F29). The weave sends a branch out of a statement to
/// the statement's exit, which carries on with what follows. That is right for a null-conditional, whose branch goes there
/// anyway, and wrong for a condition, whose branch goes to the else: on 5.28.2, in Release, <c>if (s.ShouldNotBeNull().Length &gt; 5)</c>
/// ran the then-part whatever the length, and a loop over it ran the wrong number of times or never ended, with no word from
/// anything. An awaited statement did so in Debug too. Each fixture here is bounded, so that a regression fails a fact and
/// does not hang the run, and each is driven down both ways, as a row that takes one proves nothing of the other.
/// </summary>
public class AssertionInAConditionTests
{
    private const string Short = "a";
    private const string Long = "abcdefgh";

    private static readonly BuildConfiguration[] Configurations =
        [BuildConfiguration.Debug, BuildConfiguration.Release, BuildConfiguration.ReleaseDebugPlus];

    /// <summary>The assertion, and what it hands back, in each library: <c>s</c> is never null here, so it always passes.</summary>
    private static readonly IReadOnlyDictionary<string, (string Usings, string Assert, bool Awaited)> Libraries =
        new Dictionary<string, (string, string, bool)>
        {
            ["Shouldly"] = ("using Shouldly;", "s.ShouldNotBeNull()", false),
            ["FluentAssertions"] = ("using FluentAssertions;", "s.Should().NotBeNull().And.Subject", false),
            ["AwesomeAssertions"] = ("using AwesomeAssertions;", "s.Should().NotBeNull().And.Subject", false),
            ["TUnit"] = ("using TUnit.Assertions;\nusing TUnit.Assertions.Extensions;", "(await Assert.That(s).IsNotNull())", true),
        };

    /// <summary>Each shape's body, with <c>@</c> for the assertion, and what it hands back for a short and for a long string.</summary>
    private static readonly (string Name, string Body, string OnShort, string OnLong)[] Shapes =
    [
        ("IfElse", """if (@.Length > 5) return "then"; else return "else";""", "else", "then"),
        ("IfThenMore", """var t = ""; if (@.Length > 5) t += "then"; t += "more"; return t;""", "more", "thenmore"),
        ("EarlyReturn", """if (@.Length > 5) return "returned"; return "carried on";""", "carried on", "returned"),
        ("Guard", """if (@.Length > 5) throw new ArithmeticException("too long"); return "no throw";""", "no throw", "threw ArithmeticException"),
        ("While", """var n = 0; while (@.Length > n) { if (++n > 99) return "ran on"; } return n.ToString();""", "1", "8"),
        ("For", """var n = 0; for (var i = 0; @.Length > i; i++) { if (++n > 99) return "ran on"; } return n.ToString();""", "1", "8"),
        ("DoWhile", """var n = 0; do { if (++n > 99) return "ran on"; } while (@.Length > n); return n.ToString();""", "1", "8"),
        ("EmptyLoop", """var n = 0; while (@.Length > n && n < 99) n++; return n.ToString();""", "1", "8"),
        ("LoopWithNoBody", """var n = 0; while (@.Length > n++ && n < 99) ; return n.ToString();""", "2", "9"),
        ("Break", """var n = 0; while (true) { if (@.Length <= n) break; if (++n > 99) return "ran on"; } return n.ToString();""", "1", "8"),
        ("And", """if (@ != null && s.Length > 5) return "then"; return "else";""", "else", "then"),
        ("Or", """if (@.Length > 5 || s == "z") return "then"; return "else";""", "else", "then"),
        ("Not", """if (!(@.Length > 5)) return "else"; return "then";""", "else", "then"),
        ("NullConditional", """if (@?.Length > 5) return "then"; return "else";""", "else", "then"),
        ("Pattern", """if (@ is { Length: > 5 }) return "then"; return "else";""", "else", "then"),
        ("Ternary", """return @.Length > 5 ? "then" : "else";""", "else", "then"),
        ("Stored", """var b = @.Length > 5; return b ? "then" : "else";""", "else", "then"),
        ("Switch", """switch (@.Length) { case 0: return "none"; case 1: return "one"; case 2: return "two"; default: return "more"; }""", "one", "more"),
        ("SwitchExpression", """return @.Length switch { 0 => "none", 1 => "one", 2 => "two", _ => "more" };""", "one", "more"),
        ("Foreach", """var n = 0; foreach (var c in @) n++; return n.ToString();""", "1", "8"),
        ("InATry", """try { if (@.Length > 5) return "then"; } finally { GC.KeepAlive(s); } return "else";""", "else", "then"),
        ("InAUsing", """using (new System.IO.MemoryStream()) { if (@.Length > 5) return "then"; } return "else";""", "else", "then"),
        ("Nested", """if (@.Length > 0) { if (s.Length > 5) return "long"; return "short"; } return "none";""", "short", "long"),
        ("Goto", """if (@.Length > 5) goto done; return "else"; done: return "then";""", "else", "then"),
    ];

    public static TheoryData<string, BuildConfiguration> LibrariesAndBuilds()
    {
        var data = new TheoryData<string, BuildConfiguration>();
        foreach (var library in Libraries.Keys)
            foreach (var configuration in Configurations)
                data.Add(library, configuration);
        return data;
    }

    private static string Fixture(string library)
    {
        var (usings, assert, awaited) = Libraries[library];
        var signature = awaited ? "public async Task<string>" : "public string";
        var methods = Shapes.Select(shape => $"    {signature} {shape.Name}(string s) {{ {shape.Body.Replace("@", assert)} }}");
        return $$"""
            using System;
            using System.Threading.Tasks;
            using Kronikol.Tracking;
            {{usings}}

            [assembly: TrackAssertions]

            public class Conditions
            {
            {{string.Join("\n", methods)}}
            }
            """;
    }

    private static string Build(string name, string source, BuildConfiguration configuration) =>
        TestAssemblyBuilder.Build(name, source, configuration, [typeof(TUnit.Assertions.Assert).Assembly.Location]);

    /// <summary>What a call hands back, or the exception it ends in.</summary>
    private static string Read(Type type, object instance, string method, params object?[] arguments)
    {
        try
        {
            var result = type.GetMethod(method)!.Invoke(instance, arguments);
            return result is Task<string> task ? task.GetAwaiter().GetResult() : (string)result!;
        }
        catch (TargetInvocationException e)
        {
            return "threw " + e.InnerException!.GetType().Name;
        }
        catch (Exception e)
        {
            return "threw " + e.GetType().Name;
        }
    }

    [Theory]
    [MemberData(nameof(LibrariesAndBuilds))]
    public void An_assertion_in_a_condition_takes_the_way_the_condition_says(string library, BuildConfiguration configuration)
    {
        var path = Build($"Condition_{library}_{configuration}", Fixture(library), configuration);
        WovenIl.Weave(new AssertionWeaver(), path);

        var type = Assembly.LoadFrom(path).GetType("Conditions")!;
        var instance = Activator.CreateInstance(type)!;
        var wrong = new List<string>();
        foreach (var (name, _, onShort, onLong) in Shapes)
        {
            foreach (var (input, expected) in new[] { (Short, onShort), (Long, onLong) })
            {
                var actual = Read(type, instance, name, input);
                if (actual != expected)
                    wrong.Add($"{name}(\"{input}\"): expected {expected}, read {actual}");
            }
        }

        Assert.True(wrong.Count == 0, $"{library}, {configuration}:\n  " + string.Join("\n  ", wrong));
    }

    private const string ThrownAndAwaited = """
        using System;
        using System.Threading.Tasks;
        using Kronikol.Tracking;
        using Shouldly;

        [assembly: TrackAssertions]

        public class Conditions
        {
            public string Thrown(string message)
            {
                if (Should.Throw<ArithmeticException>(() => throw new ArithmeticException("x")).Message == message) return "then"; else return "else";
            }

            public async Task<string> Awaited(string message)
            {
                if ((await Should.ThrowAsync<ArithmeticException>(() => Task.FromException(new ArithmeticException("x")))).Message == message) return "then"; else return "else";
            }

            public async Task<string> AfterAnAwait(string s)
            {
                await Task.Yield();
                if (s.ShouldNotBeNull().Length > 5) return "then"; else return "else";
            }
        }
        """;

    [Theory]
    [InlineData(BuildConfiguration.Debug)]
    [InlineData(BuildConfiguration.Release)]
    [InlineData(BuildConfiguration.ReleaseDebugPlus)]
    public void What_an_assertion_hands_back_reads_the_same_with_and_without_an_await(BuildConfiguration configuration)
    {
        var path = Build($"ConditionThrown_{configuration}", ThrownAndAwaited, configuration);
        WovenIl.Weave(new AssertionWeaver(), path);

        var type = Assembly.LoadFrom(path).GetType("Conditions")!;
        var instance = Activator.CreateInstance(type)!;
        Assert.Equal(
            ["else", "then", "else", "then", "else", "then"],
            [
                Read(type, instance, "Thrown", "y"), Read(type, instance, "Thrown", "x"),
                Read(type, instance, "Awaited", "y"), Read(type, instance, "Awaited", "x"),
                Read(type, instance, "AfterAnAwait", Short), Read(type, instance, "AfterAnAwait", Long),
            ]);
    }

    private const string Reasons = """
        using System;
        using System.Threading.Tasks;
        using Kronikol.Tracking;
        using Shouldly;

        [assembly: TrackAssertions]

        public class Conditions
        {
            public string InAnIf()
            {
                var s = "a";
                if (s.ShouldNotBeNull().Length > 5) return "then";
                return "else";
            }

            public async Task<string> Awaited()
            {
                if ((await Should.ThrowAsync<ArithmeticException>(() => Task.FromException(new ArithmeticException("x")))).Message == "y") return "then";
                return "else";
            }

            public string Switched()
            {
                var s = "a";
                return s.ShouldNotBeNull().Length switch { 0 => "none", 1 => "one", 2 => "two", _ => "more" };
            }
        }
        """;

    private const string BranchReason = "a branch out of the statement goes somewhere other than what follows it";

    [Theory]
    [InlineData(BuildConfiguration.Release)]
    [InlineData(BuildConfiguration.ReleaseDebugPlus)]
    public void An_optimised_build_leaves_such_a_statement_as_written_and_says_why(BuildConfiguration configuration)
    {
        var path = Build($"ConditionRefused_{configuration}", Reasons, configuration);

        var result = WovenIl.Weave(new AssertionWeaver(), path);

        Assert.Equal(0, result.WeavedCount);
        Assert.Equal(3, result.Unwoven.Count);
        Assert.All(result.Unwoven, reason => Assert.EndsWith(": " + BranchReason, reason));
        var (thrown, notes) = WovenRun.Run(path, "Conditions", "InAnIf");
        Assert.Null(thrown);
        Assert.Empty(notes);
    }

    [Fact]
    public void A_Debug_build_keeps_the_note_of_a_condition_it_can_follow_and_says_why_of_an_awaited_one()
    {
        // Debug writes a hidden sequence point before the condition's branch, which puts the branch outside the statement.
        // An awaited statement's result is read at its merge point, so its branch is inside whatever the build.
        var path = Build("ConditionDebug", Reasons, BuildConfiguration.Debug);

        var result = WovenIl.Weave(new AssertionWeaver(), path);

        Assert.Equal(2, result.WeavedCount);
        Assert.EndsWith(": " + BranchReason, Assert.Single(result.Unwoven));
        var (thrown, notes) = WovenRun.Run(path, "Conditions", "InAnIf");
        Assert.Null(thrown);
        Assert.True(Assert.Single(notes).Passed);
    }

    private const string OnItsOwnLine = """
        using System.Threading.Tasks;
        using Kronikol.Tracking;
        using Shouldly;

        [assembly: TrackAssertions]

        public class Conditions
        {
            public string TheCondition(string s)
            {
                var checkedValue = s.ShouldNotBeNull();
                if (checkedValue.Length > 5) return "then";
                return "else";
            }

            public async Task<string> TheAwaitedCondition()
            {
                var thrown = await Should.ThrowAsync<System.ArithmeticException>(() => Task.FromException(new System.ArithmeticException("x")));
                if (thrown.Message == "y") return "then";
                return "else";
            }

            public string Short() => TheCondition("a");
            public string Long() => TheCondition("abcdefgh");
        }
        """;

    [Theory]
    [InlineData(BuildConfiguration.Debug)]
    [InlineData(BuildConfiguration.Release)]
    [InlineData(BuildConfiguration.ReleaseDebugPlus)]
    public void An_assertion_on_a_line_of_its_own_before_the_condition_keeps_its_note(BuildConfiguration configuration)
    {
        // What the wiki tells a project to do with a statement this leaves as written.
        var path = Build($"ConditionOwnLine_{configuration}", OnItsOwnLine, configuration);

        var result = WovenIl.Weave(new AssertionWeaver(), path);

        Assert.Empty(result.Unwoven);
        foreach (var (entry, expected) in new[] { ("Short", "else"), ("Long", "then"), ("TheAwaitedCondition", "else") })
        {
            var type = Assembly.LoadFrom(path).GetType("Conditions")!;
            Assert.Equal(expected, Read(type, Activator.CreateInstance(type)!, entry));
            var (thrown, notes) = WovenRun.Run(path, "Conditions", entry);
            Assert.Null(thrown);
            Assert.True(Assert.Single(notes).Passed, entry);
        }
    }

    private const string InAFilter = """
        using System;
        using Kronikol.Tracking;
        using Shouldly;

        [assembly: TrackAssertions]

        public class Filters
        {
            public static bool Check(string s) => s.Length > 0;

            public void Caught()
            {
                var s = "a";
                try { throw new ArithmeticException("x"); }
                catch (ArithmeticException) when (Check(s.ShouldNotBeNull())) { }
            }
        }
        """;

    [Theory]
    [InlineData(BuildConfiguration.Debug)]
    [InlineData(BuildConfiguration.Release)]
    [InlineData(BuildConfiguration.ReleaseDebugPlus)]
    public void An_assertion_in_an_exception_filter_is_left_as_written(BuildConfiguration configuration)
    {
        // A filter starts with the exception on the stack and can only end at its endfilter, so no try can stand round it:
        // on 5.28.2 the method threw InvalidProgramException at every call in Debug.
        var path = Build($"ConditionFilter_{configuration}", InAFilter, configuration);

        var result = WovenIl.Weave(new AssertionWeaver(), path);

        Assert.EndsWith(": the statement is in an exception filter", Assert.Single(result.Unwoven));
        var (thrown, notes) = WovenRun.Run(path, "Filters", "Caught");
        Assert.Null(thrown);
        Assert.Empty(notes);
    }

    private const string NullPaths = """
        using System;
        using Kronikol.Tracking;
        using Shouldly;

        [assembly: TrackAssertions]

        public class NullPaths
        {
            public int V;

            private void ThenArm(bool b, string s) { if (b) s?.ShouldBe("a"); else V = 1; }
            private void ThenArmWithMoreAfter(bool b, string s) { if (b) s?.ShouldBe("a"); else V = 1; V += 2; }
            private void InATry(string s) { try { s?.ShouldBe("a"); } finally { GC.KeepAlive(s); } }
            private void EndsTheMethod(string s) { s?.ShouldBe("a"); }
            private int WithMoreAfter(string s) { s?.ShouldBe("a"); return 7; }

            public void ThenArm_Value() => ThenArm(true, "a");
            public void ThenArm_Null() => ThenArm(true, null);
            public void ThenArmWithMoreAfter_Value() => ThenArmWithMoreAfter(true, "a");
            public void ThenArmWithMoreAfter_Null() => ThenArmWithMoreAfter(true, null);
            public void InATry_Value() => InATry("a");
            public void InATry_Null() => InATry(null);
            public void EndsTheMethod_Value() => EndsTheMethod("a");
            public void EndsTheMethod_Null() => EndsTheMethod(null);
            public void WithMoreAfter_Value() => WithMoreAfter("a");
            public void WithMoreAfter_Null() => WithMoreAfter(null);
        }
        """;

    [Theory]
    [InlineData(BuildConfiguration.Debug)]
    [InlineData(BuildConfiguration.Release)]
    [InlineData(BuildConfiguration.ReleaseDebugPlus)]
    public void A_null_path_that_goes_where_the_statement_ends_keeps_its_note(BuildConfiguration configuration)
    {
        // A null-conditional's branch reaches what follows the statement, past a nop, a br or a leave, or another return.
        // It is no condition, and none of them is left as written.
        var path = Build($"ConditionNullPaths_{configuration}", NullPaths, configuration);

        var result = WovenIl.Weave(new AssertionWeaver(), path);

        Assert.Empty(result.Unwoven);
        foreach (var shape in new[] { "ThenArm", "ThenArmWithMoreAfter", "InATry", "EndsTheMethod", "WithMoreAfter" })
        {
            var (thrown, notes) = WovenRun.Run(path, "NullPaths", shape + "_Value");
            Assert.Null(thrown);
            Assert.True(Assert.Single(notes).Passed, shape);

            (thrown, notes) = WovenRun.Run(path, "NullPaths", shape + "_Null");
            Assert.Null(thrown);
            Assert.Empty(notes);
        }
    }

    // ---- the differential: one body twice, with an assertion and with a plain identity in its place, and only the first is woven ----

    private static readonly (string Id, string Text)[] DifferentialConditions =
    [
        ("gt", "@A(s).Length > 5"),
        ("and", """@A(s) != null && s.Length > 5"""),
        ("or", """@A(s).Length > 5 || s == "a" """),
        ("not", "!(@A(s).Length > 5)"),
        ("pattern", "@A(s) is { Length: > 5 }"),
        ("ternary", """(@A(s).Length > 5 ? s == "abcdefgh" : s == "b")"""),
        ("nullcond", "@A(s)?.Length > 5"),
        ("eqtrue", "(@A(s).Length > 5) == true"),
        ("list", "@A(l).Count > n"),
        ("second", "s != null && @A(s).Length > 5"),
        ("orsecond", "s == null || @A(s).Length > 5"),
        ("two", "@A(s).Length > 5 && @A(s).Length < 100"),
        ("call", "string.IsNullOrEmpty(@A(s))"),
        ("value", "@B(n) > 2"),
        ("cast", "@C(o).Length > 5"),
    ];

    private static readonly (string Id, string Body, bool Async)[] DifferentialForms =
    [
        ("ifelse", """if (@) t += "T"; else t += "E";""", false),
        ("if", """if (@) t += "T"; t += "x";""", false),
        ("ifreturn", """if (@) return t + "R"; t += "x";""", false),
        ("while", """var i = 0; while ((@) && i < 3) { i++; t += "L"; }""", false),
        ("whilefirst", """var i = 0; while (@) { if (++i > 3) break; t += "L"; }""", false),
        ("for", """for (var i = 0; (@) && i < 3; i++) t += "L";""", false),
        ("dowhile", """var k = 0; do { t += "D"; k++; } while ((@) && k < 3);""", false),
        ("break", """var k = 0; while (true) { if (@) break; t += "B"; if (++k > 3) break; }""", false),
        ("continue", """foreach (var q in new[] { 1, 2 }) { if (@) continue; t += q; }""", false),
        ("finally", """try { if (@) t += "T"; else t += "E"; } finally { t += "F"; }""", false),
        ("catch", """try { if (@) throw new ArithmeticException("x"); t += "N"; } catch (ArithmeticException) { t += "C"; }""", false),
        ("incatch", """try { throw new ArithmeticException("x"); } catch (ArithmeticException) { if (@) t += "T"; else t += "E"; }""", false),
        ("infinally", """try { t += "B"; } finally { if (@) t += "T"; else t += "E"; }""", false),
        ("when", """try { throw new ArithmeticException("x"); } catch (ArithmeticException) when (@) { t += "W"; } catch (ArithmeticException) { t += "C"; }""", false),
        ("using", """using (new System.IO.MemoryStream()) { if (@) t += "T"; else t += "E"; }""", false),
        ("usingreturn", """using (new System.IO.MemoryStream()) { if (@) return t + "R"; } t += "x";""", false),
        ("lock", """lock (this) { if (@) t += "T"; t += "x"; }""", false),
        ("stored", """var v = (@) ? "a" : "b"; t += v;""", false),
        ("assign", """t += (@) ? "a" : "b";""", false),
        ("switch", """switch ((@) ? 1 : 2) { case 1: t += "1"; break; default: t += "d"; break; }""", false),
        ("switchexpr", """t += ((@) ? 1 : 2) switch { 1 => "1", _ => "d" };""", false),
        ("bool", "var b = @; t += b;", false),
        ("nested", """if (@) { if (s == "abcdefgh") t += "A"; else t += "Z"; } else t += "E";""", false),
        ("goto", """if (@) goto done; t += "x"; done: t += "y";""", false),
        ("lambda", "Func<bool> f = () => @; t += f();", false),
        ("local", "bool G() => @; t += G();", false),
        ("iterator", """IEnumerable<string> It() { if (@) yield return "T"; else yield return "E"; } t += string.Concat(It());""", false),
        ("returnvalue", """return (@) ? t + "a" : t + "b";""", false),
        ("async", """await Task.Yield(); if (@) t += "T"; else t += "E"; await Task.Yield();""", true),
        ("asyncwhile", """await Task.Yield(); var i = 0; while ((@) && i < 3) { i++; await Task.Yield(); t += "L"; }""", true),
        ("asyncfinally", """try { await Task.Yield(); if (@) t += "T"; else t += "E"; } finally { t += "F"; }""", true),
    ];

    private static string DifferentialBody((string Id, string Body, bool Async) form, string condition, bool woven)
    {
        var text = form.Body.Replace("@", condition);
        foreach (var (mark, assertion, plain) in new[]
                 {
                     ("@A(s)", "s.ShouldNotBeNull()", "K.Keep(s)"),
                     ("@A(l)", "l.ShouldNotBeNull()", "K.Keep(l)"),
                     ("@B(n)", "((int?)n).ShouldNotBeNull()", "K.KeepInt((int?)n)"),
                     ("@C(o)", "o.ShouldBeOfType<string>()", "K.KeepStr(o)"),
                 })
            text = text.Replace(mark, woven ? assertion : plain);
        return $$"""var t = ""; object o = s; {{text}} return t;""";
    }

    private static string DifferentialFixture(string condition)
    {
        var methods = DifferentialForms.SelectMany(form =>
        {
            var signature = form.Async ? "public async Task<string>" : "public string";
            return new[]
            {
                $"    {signature} Go_{form.Id}(string s, List<int> l, int n) {{ {DifferentialBody(form, condition, woven: true)} }}",
                $"    {signature} Plain_{form.Id}(string s, List<int> l, int n) {{ {DifferentialBody(form, condition, woven: false)} }}",
            };
        });
        return $$"""
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Kronikol.Tracking;
            using Shouldly;

            [assembly: TrackAssertions]

            public static class K
            {
                public static T Keep<T>(T v) => v;
                public static int KeepInt(int? v) => v!.Value;
                public static string KeepStr(object o) => (string)o;
            }

            public class Bodies
            {
            {{string.Join("\n", methods)}}
            }
            """;
    }

    public static TheoryData<string, BuildConfiguration> ConditionsAndBuilds()
    {
        var data = new TheoryData<string, BuildConfiguration>();
        foreach (var (id, _) in DifferentialConditions)
            foreach (var configuration in Configurations)
                data.Add(id, configuration);
        return data;
    }

    /// <summary>
    /// A shape that reads differently fails by what it returned, whatever it draws, so this asks nothing about what a statement
    /// should do: any input on which the two bodies differ is a run the weave changed.
    /// </summary>
    [Theory]
    [MemberData(nameof(ConditionsAndBuilds))]
    public void The_weave_does_not_change_what_a_program_does(string condition, BuildConfiguration configuration)
    {
        var text = DifferentialConditions.Single(c => c.Id == condition).Text;
        var path = Build($"Differential_{condition}_{configuration}", DifferentialFixture(text), configuration);
        WovenIl.Weave(new AssertionWeaver(), path);

        var type = Assembly.LoadFrom(path).GetType("Bodies")!;
        var instance = Activator.CreateInstance(type)!;
        var list = new List<int> { 1, 2, 3 };
        var changed = new List<string>();
        var testId = $"{condition}_{configuration}_{Guid.NewGuid():N}";
        using (TestIdentityScope.Begin(testId, testId))
        {
            foreach (var form in DifferentialForms)
            {
                foreach (var (s, n) in new[] { (Short, 1), (Long, 5), ("", 3), ("abc", 0) })
                {
                    var woven = Read(type, instance, "Go_" + form.Id, s, list, n);
                    var plain = Read(type, instance, "Plain_" + form.Id, s, list, n);
                    if (woven != plain)
                        changed.Add($"{form.Id}(\"{s}\", {n}): as written {plain}, woven {woven}");
                }
            }
        }

        Assert.True(changed.Count == 0, $"{condition}, {configuration}:\n  " + string.Join("\n  ", changed));
    }

    [Fact]
    public void The_differential_runs_every_form_of_every_condition()
    {
        // A vacuity guard: the two lists are the fact, and a fixture that compiled to nothing would pass it.
        var path = Build("DifferentialCount", DifferentialFixture(DifferentialConditions[0].Text), BuildConfiguration.Release);
        var methods = Assembly.LoadFrom(path).GetType("Bodies")!.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.Equal(DifferentialForms.Length * 2, methods.Length);
        Assert.True(DifferentialForms.Length >= 31 && DifferentialConditions.Length >= 15);
        Assert.Equal(DifferentialConditions.Length * Configurations.Length, ConditionsAndBuilds().Count);
    }
}
