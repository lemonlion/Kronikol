using System.Reflection;
using System.Text;
using Kronikol.AssertionTracking;
using Kronikol.Tracking;
using Mono.Cecil;
using Mono.Cecil.Cil;
using static Kronikol.Tests.AssertionTracking.TestAssemblyBuilder;

namespace Kronikol.Tests.AssertionTracking;

// Measurement harness for GitHub issue #213 (plans/CAPTURED_FIELD_VALUES_PLAN.md). NOT a fact to keep: it is gated on
// environment variables so the normal suite never runs it, and it is copied out to the plan's harness/ afterwards.
//
// The defect: AssertionWeaver.GetStateFieldOriginalName maps ANY field named <name>... to "name" (written for a state
// machine's hoisted locals <name>5__N). A primary-constructor parameter is a class field named <name>P, and five of
// DetectCapturedVariables' arms capture a field by name without checking it is declared on the state machine (or that a
// lambda/standalone read it off `this`). The value-load then emits `ldarg.0; ldfld Class::<name>P` inside MoveNext,
// where ldarg.0 is the state machine, reading the wrong offset. In a static method ldarg.0 is the first parameter.
//
// Two gated entry points, both reflection-driven so WovenIlVerifiesTests' "no weave outside WovenIl" scan does not see
// them (it flags the literal method call spelt out, which this file never writes), and so a shape whose weave adds a
// verifier error is measured, not thrown on:
//   * Dive  — env KRONIKOL_213_DIVE=<file>. Static analysis only, never runs woven code, so no shape can crash it:
//             per shape x build it builds, weaves, runs ILVerify, and dumps each captured variable's metadata and the
//             woven value-load IL. Writes the big table, flushing per block.
//   * Case  — env KRONIKOL_213_CASE=<shapeId>, KRONIKOL_213_CASE_OUT=<file>, KRONIKOL_213_CASE_SKIP=<csv of build|entry>.
//             Runs every (build, entry) of one shape, woven and unwoven, under its own test identity, recording the
//             outcome and the note. A marker is flushed before each run and after it finishes; a run that crashes the
//             host (AccessViolationException on stelem.ref) kills the process, so the orchestrator reads the exit code
//             and the missing DONE marker. SKIP lets a re-run resume past cases already done or known to crash.
public class Probe213Tests
{
    private const string CommonHarness = """

        public sealed class Harness
        {
            private readonly string _tag;
            public Harness() => _tag = "default";
            public Harness(string tag) => _tag = tag;
            public System.Threading.Tasks.Task Run(System.Func<System.Threading.Tasks.Task> run) => run();
            public System.Threading.Tasks.Task Run(string name, System.Func<System.Threading.Tasks.Task> run) => run();
            public System.Threading.Tasks.Task Start(System.Threading.CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                return System.Threading.Tasks.Task.CompletedTask;
            }
            public override string ToString() => "Harness(" + _tag + ")";
        }
        """;

    private const string CommonC = """

        public sealed class C
        {
            public int _id = 1;
            public Harness _harness = new Harness("c-default");
            public void Fail() => throw new System.InvalidOperationException("boom _id=" + _id);
        }
        """;

    private sealed record Shape(string Id, string Source, string TypeName, string[] Entries, BuildConfiguration[] Builds);

    private static readonly BuildConfiguration[] AllBuilds =
        [BuildConfiguration.Debug, BuildConfiguration.Release, BuildConfiguration.ReleaseDebugPlus];

    private static Dictionary<string, Shape> Shapes()
    {
        var shapes = new List<Shape>();
        void Add(string id, string body, string type, string[] entries, BuildConfiguration[]? builds = null) =>
            shapes.Add(new Shape(id, body + CommonHarness, type, entries, builds ?? AllBuilds));
        void AddC(string id, string body, string type, string[] entries) =>
            shapes.Add(new Shape(id, body + CommonHarness + CommonC, type, entries, AllBuilds));

        // P1 — the issue's second test: primary ctor (first, second, third), async, ThrowAsync lambda reads `third`.
        Add("P1", """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            using Kronikol.Tracking;
            using Shouldly;
            [assembly: TrackAssertions]
            public class P1(Harness first, Harness second, Harness third)
            {
                private Exception? _exception;
                public async Task AssertionPasses()
                {
                    await Task.Yield();
                    _exception = await Should.ThrowAsync<InvalidOperationException>(() => third.Run(() => throw new InvalidOperationException()));
                    _exception.ShouldNotBeNull();
                    (first, second).ShouldNotBe((null, null));
                }
            }
            """, "P1", ["AssertionPasses"]);

        // P2 — the issue's first shape: primary ctor (db, handlers, state), field _service, cancelled token.
        Add("P2", """
            #nullable enable
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Kronikol.Tracking;
            using Shouldly;
            [assembly: TrackAssertions]
            public class P2(Harness db, Harness handlers, Harness state)
            {
                private Harness? _service = new Harness("service");
                private Exception? _exception;
                public async Task AssertionPasses()
                {
                    using var cts = new CancellationTokenSource();
                    await cts.CancelAsync();
                    _exception = await Should.ThrowAsync<OperationCanceledException>(() => handlers.Run(nameof(Harness), () => _service!.Start(cts.Token)));
                }
            }
            """, "P2", ["AssertionPasses"]);

        // P3 — P1 made synchronous (control: the lambda's ldarg.0 is `this`).
        Add("P3", """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            using Kronikol.Tracking;
            using Shouldly;
            [assembly: TrackAssertions]
            public class P3(Harness first, Harness second, Harness third)
            {
                private Exception? _exception;
                public void AssertionPasses()
                {
                    _exception = Should.Throw<InvalidOperationException>(() => third.Run(() => throw new InvalidOperationException()).GetAwaiter().GetResult());
                    _exception.ShouldNotBeNull();
                    (first, second).ShouldNotBe((null, null));
                }
            }
            """, "P3", ["AssertionPasses"]);

        // P4 — P1 with ordinary fields instead of parameters (control: the proper <>4__this path).
        Add("P4", """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            using Kronikol.Tracking;
            using Shouldly;
            [assembly: TrackAssertions]
            public class P4
            {
                private readonly Harness _first = new("first");
                private readonly Harness _second = new("second");
                private readonly Harness _third = new("third");
                private Exception? _exception;
                public async Task AssertionPasses()
                {
                    await Task.Yield();
                    _exception = await Should.ThrowAsync<InvalidOperationException>(() => _third.Run(() => throw new InvalidOperationException()));
                    _exception.ShouldNotBeNull();
                    (_first, _second).ShouldNotBe((null, null));
                }
            }
            """, "P4", ["AssertionPasses"]);

        // P5 — the statement itself reads the parameter, no lambda; async. `third.ShouldBe(third)` surfaces the value
        // (the argument `third`) in the note, so this is the shape that shows right/wrong/garbage directly.
        Add("P5", """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            using Kronikol.Tracking;
            using Shouldly;
            [assembly: TrackAssertions]
            public class P5(Harness first, Harness second, Harness third)
            {
                public async Task AssertionPasses()
                {
                    await Task.Yield();
                    third.ShouldNotBeNull();
                    third.ShouldBe(third);
                    (first, second).ShouldNotBe((null, null));
                }
            }
            """, "P5", ["AssertionPasses"]);

        // P6 — async method whose only use of `this` is inside the lambda (does MoveNext hold <>4__this?).
        Add("P6", """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            using Kronikol.Tracking;
            using Shouldly;
            [assembly: TrackAssertions]
            public class P6(Harness only)
            {
                public async Task AssertionPasses()
                {
                    await Task.Yield();
                    await Should.ThrowAsync<InvalidOperationException>(() => only.Run(() => throw new InvalidOperationException()));
                }
            }
            """, "P6", ["AssertionPasses"]);

        // P7 — generic class Probe<T>(T value), async test reading `value` in a lambda. Run closed over Harness.
        Add("P7", """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            using Kronikol.Tracking;
            using Shouldly;
            [assembly: TrackAssertions]
            public class P7<T>(T value)
            {
                public async Task AssertionPasses()
                {
                    await Task.Yield();
                    await Should.ThrowAsync<InvalidOperationException>(async () => { System.Console.WriteLine(value); await Task.Yield(); throw new InvalidOperationException(); });
                }
            }
            """, "P7`1", ["AssertionPasses"]);

        // P8 — expression-tree argument reading a primary-ctor parameter (FluentAssertions' Contain(Expression<..>)),
        // async and sync.
        Add("P8", """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;
            [assembly: TrackAssertions]
            public class P8(Harness expected)
            {
                public void SyncAssert()
                {
                    new List<Harness> { expected }.Should().Contain(x => x == expected);
                }
                public async Task AsyncAssert()
                {
                    await Task.Yield();
                    new List<Harness> { expected }.Should().Contain(x => x == expected);
                }
            }
            """, "P8", ["SyncAssert", "AsyncAssert"]);

        // P9 — an async lambda inside ThrowAsync, reading a primary-ctor parameter.
        Add("P9", """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            using Kronikol.Tracking;
            using Shouldly;
            [assembly: TrackAssertions]
            public class P9(Harness first, Harness second, Harness third)
            {
                public async Task AssertionPasses()
                {
                    await Should.ThrowAsync<InvalidOperationException>(async () => { await Task.Yield(); await third.Run(() => throw new InvalidOperationException()); });
                }
            }
            """, "P9", ["AssertionPasses"]);

        // P10 — a lambda reading a parameter AND an ordinary field AND a captured local, async.
        Add("P10", """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            using Kronikol.Tracking;
            using Shouldly;
            [assembly: TrackAssertions]
            public class P10(Harness first, Harness second, Harness third)
            {
                private readonly Harness _field = new("field");
                public async Task AssertionPasses()
                {
                    await Task.Yield();
                    var local = new Harness("local");
                    await Should.ThrowAsync<InvalidOperationException>(() => third.Run(() => { GC.KeepAlive(_field); GC.KeepAlive(local); throw new InvalidOperationException(); }));
                }
            }
            """, "P10", ["AssertionPasses"]);

        // P11 — a local function reading the outer class's primary-ctor parameter (record what compiles).
        Add("P11", """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            using Kronikol.Tracking;
            using Shouldly;
            [assembly: TrackAssertions]
            public class P11(Harness first, Harness second, Harness third)
            {
                public async Task AssertionPasses()
                {
                    await Task.Yield();
                    await Should.ThrowAsync<InvalidOperationException>(Body);
                    Task Body() => third.Run(() => throw new InvalidOperationException());
                }
            }
            """, "P11", ["AssertionPasses"]);

        // P13 — expression-tree predicate reading ANOTHER instance's field of the same class (FluentAssertions).
        AddC("P13", """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;
            [assembly: TrackAssertions]
            public class P13
            {
                private int _id = 1;
                public void SyncAssert()
                {
                    var other = new P13(); other._id = 2;
                    new[] { 2 }.Should().Contain(x => x == other._id);
                }
                public async Task AsyncAssert()
                {
                    await Task.Yield();
                    var other = new P13(); other._id = 2;
                    new[] { 2 }.Should().Contain(x => x == other._id);
                }
            }
            """, "P13", ["SyncAssert", "AsyncAssert"]);

        // P14 — a delegate lambda reading another instance; Fail() reads _id and throws.
        AddC("P14", """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            using Kronikol.Tracking;
            using Shouldly;
            [assembly: TrackAssertions]
            public class P14
            {
                private int _id = 1;
                private void Fail() => throw new InvalidOperationException("boom _id=" + _id);
                public void SyncAssert()
                {
                    var other = new P14(); other._id = 2;
                    Should.Throw<InvalidOperationException>(() => other.Fail());
                }
                public async Task AsyncAssert()
                {
                    await Task.Yield();
                    var other = new P14(); other._id = 2;
                    await Should.ThrowAsync<InvalidOperationException>(() => { other.Fail(); return Task.CompletedTask; });
                }
            }
            """, "P14", ["SyncAssert", "AsyncAssert"]);

        // P15 — a STATIC method of the test class with a parameter C c; _harness is an instance field of C. ldarg.0 is
        // the first parameter, not this. Two variants: c first, c second (after an int).
        AddC("P15", """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            using Kronikol.Tracking;
            using Shouldly;
            [assembly: TrackAssertions]
            public class P15
            {
                public static void First(C c)
                {
                    Should.Throw<InvalidOperationException>(() => c._harness.Run(() => throw new InvalidOperationException()).GetAwaiter().GetResult());
                }
                public static void Second(int n, C c)
                {
                    Should.Throw<InvalidOperationException>(() => c._harness.Run(() => throw new InvalidOperationException()).GetAwaiter().GetResult());
                }
            }
            """, "P15", ["First", "Second"]);

        // P16 — P15 but async static.
        AddC("P16", """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            using Kronikol.Tracking;
            using Shouldly;
            [assembly: TrackAssertions]
            public class P16
            {
                public static async Task First(C c)
                {
                    await Task.Yield();
                    await Should.ThrowAsync<InvalidOperationException>(() => c._harness.Run(() => throw new InvalidOperationException()));
                }
                public static async Task Second(int n, C c)
                {
                    await Task.Yield();
                    await Should.ThrowAsync<InvalidOperationException>(() => c._harness.Run(() => throw new InvalidOperationException()));
                }
            }
            """, "P16", ["First", "Second"]);

        // P17 — the standalone arm, no lambda: the statement reads another instance's field of the same class. The
        // async variants call a this-method (Touch) so MoveNext holds <>4__this, which the standalone arm needs.
        Add("P17", """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            using Kronikol.Tracking;
            using Shouldly;
            [assembly: TrackAssertions]
            public class P17
            {
                private int _id = 1;
                private void Touch() { }
                public void SyncA() { var other = new P17(); other._id = 2; other._id.ShouldBe(2); }
                public void SyncB() { var other = new P17(); other._id = 2; 2.ShouldBe(other._id); }
                public async Task AsyncA() { await Task.Yield(); Touch(); var other = new P17(); other._id = 2; other._id.ShouldBe(2); }
                public async Task AsyncB() { await Task.Yield(); Touch(); var other = new P17(); other._id = 2; 2.ShouldBe(other._id); }
            }
            """, "P17", ["SyncA", "SyncB", "AsyncA", "AsyncB"]);

        // P18 — an AWAITED statement that names the parameter outside a lambda. An awaited statement has no argument
        // path (ArgumentCapture is null), so its values come from the captured variables alone; Field is the twin with
        // an ordinary field, which the chained arm (Debug) or the standalone arm (Release) captures.
        Add("P18", """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            using Kronikol.Tracking;
            using Shouldly;
            [assembly: TrackAssertions]
            public class P18(Harness expected)
            {
                private readonly Harness _field = new Harness("field");
                private Task<Harness> GetParameter() => Task.FromResult(expected);
                private Task<Harness> GetField() => Task.FromResult(_field);
                public async Task Parameter()
                {
                    await Task.Yield();
                    (await GetParameter()).ShouldBe(expected);
                }
                public async Task Field()
                {
                    await Task.Yield();
                    (await GetField()).ShouldBe(_field);
                }
            }
            """, "P18", ["Parameter", "Field"]);

        // P19 — a value-type primary constructor parameter read in a lambda of an async method: the load boxes the bytes
        // at the parameter's offset of the state machine. The label prints the lambda with the value substituted.
        Add("P19", """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            using Kronikol.Tracking;
            using Shouldly;
            [assembly: TrackAssertions]
            public class P19(int count, long total, Harness third)
            {
                public async Task Int()
                {
                    await Task.Yield();
                    third.ShouldSatisfyAllConditions(h => GC.KeepAlive(count));
                }
                public async Task Long()
                {
                    await Task.Yield();
                    third.ShouldSatisfyAllConditions(h => GC.KeepAlive(total));
                }
            }
            """, "P19", ["Int", "Long"]);

        // P20 — a local function that captures a local and not this: Roslyn makes it static and hands it the closure, a
        // struct, by reference as its first parameter, so its ldarg.0 is the closure (method.HasThis is false).
        Add("P20", """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            using Kronikol.Tracking;
            using Shouldly;
            [assembly: TrackAssertions]
            public class P20
            {
                public void LocalFunction()
                {
                    var expected = new Harness("expected");
                    var actual = expected;
                    Check();
                    void Check()
                    {
                        actual.ShouldBe(expected);
                        new[] { actual }.ShouldContain(expected);
                    }
                }
            }
            """, "P20", ["LocalFunction"]);

        // P21 — a static method whose statement reads a field of its parameter itself (no lambda): ldarg.0 is c, and
        // the compiled code reads _id off it. Async: c is a field of the state machine.
        AddC("P21", """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            using Kronikol.Tracking;
            using Shouldly;
            [assembly: TrackAssertions]
            public class P21
            {
                public static void Sync(C c)
                {
                    2.ShouldBe(c._id);
                    new[] { 2 }.ShouldContain(c._id);
                }
                public static async Task Async(C c)
                {
                    await Task.Yield();
                    (await Task.FromResult(2)).ShouldBe(c._id);
                }
            }
            """, "P21", ["Sync", "Async"]);

        // P22 — F1 through FluentAssertions: the lambda of Awaiting reads the parameter, as Shouldly's ThrowAsync does.
        Add("P22", """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;
            [assembly: TrackAssertions]
            public class P22(Harness first, Harness second, Harness third)
            {
                private Harness[] Others() => new[] { first, second };
                public async Task AssertionPasses()
                {
                    await Task.Yield();
                    await FluentActions.Awaiting(() => third.Run(() => throw new InvalidOperationException()))
                        .Should().ThrowAsync<InvalidOperationException>();
                }
            }
            """, "P22", ["AssertionPasses"]);

        // P22b — F1 through FluentAssertions after .Should(), where the scan starts: AllSatisfy's delegate reads the
        // parameter, in an async method.
        Add("P22b", """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            using FluentAssertions;
            using Kronikol.Tracking;
            [assembly: TrackAssertions]
            public class P22b(Harness first, Harness second, Harness third)
            {
                private Harness[] Others() => new[] { first, second };
                public async Task AssertionPasses()
                {
                    await Task.Yield();
                    new[] { 1 }.Should().AllSatisfy(x => GC.KeepAlive(third));
                }
            }
            """, "P22b", ["AssertionPasses"]);

        // P23 — the assertion runs inside a lambda that captures a local as well as this, so the woven method is the
        // closure's own (a display class, whose ldarg.0 is the closure), and the assertion's lambda uses the parameter.
        // InAClosure is synchronous; InAnAsyncClosure is an async lambda, whose state machine's <>4__this is the closure.
        Add("P23", """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Kronikol.Tracking;
            using Shouldly;
            [assembly: TrackAssertions]
            public class P23(Harness third)
            {
                public void InAClosure()
                {
                    var local = new Harness("local");
                    new List<int> { 1 }.ForEach(x =>
                    {
                        GC.KeepAlive(local);
                        Should.Throw<InvalidOperationException>(() => third.Run(() => throw new InvalidOperationException()).GetAwaiter().GetResult());
                    });
                }
                public async Task InAnAsyncClosure()
                {
                    var local = new Harness("local");
                    Func<Task> body = async () =>
                    {
                        GC.KeepAlive(local);
                        await Task.Yield();
                        await Should.ThrowAsync<InvalidOperationException>(() => third.Run(() => throw new InvalidOperationException()));
                    };
                    await body();
                }
            }
            """, "P23", ["InAClosure", "InAnAsyncClosure"]);

        // P12 — crash hunt: the P1 shape with varying parameter counts and extra long/int fields that shift <last>P's
        // offset. Run through Case (child processes) because a bad stelem.ref aborts the host.
        foreach (var (id, count, fields) in new[]
                 {
                     ("P12_1p", 1, ""), ("P12_2p", 2, ""), ("P12_4p", 4, ""), ("P12_6p", 6, ""),
                     ("P12_1p_fields", 1, "    private long _n1 = 0x0123456789ABCDEF; private int _n2 = 0x7FFFFFFF; private long _n3 = -1;\n"),
                     ("P12_2p_fields", 2, "    private long _n1 = 0x0123456789ABCDEF; private int _n2 = 0x7FFFFFFF; private long _n3 = -1;\n"),
                     ("P12_4p_fields", 4, "    private long _n1 = 0x0123456789ABCDEF; private int _n2 = 0x7FFFFFFF; private long _n3 = -1;\n"),
                     ("P12_6p_fields", 6, "    private long _n1 = 0x0123456789ABCDEF; private int _n2 = 0x7FFFFFFF; private long _n3 = -1;\n"),
                 })
        {
            var names = Enumerable.Range(0, count).Select(i => "p" + i).ToArray();
            var ctorParams = string.Join(", ", names.Select(n => "Harness " + n));
            var last = names[^1];
            var body = "#nullable enable\n" +
                       "using System;\nusing System.Threading.Tasks;\nusing Kronikol.Tracking;\nusing Shouldly;\n" +
                       "[assembly: TrackAssertions]\n" +
                       $"public class {id}({ctorParams})\n{{\n" +
                       fields +
                       "    public async Task AssertionPasses()\n    {\n" +
                       "        await Task.Yield();\n" +
                       $"        await Should.ThrowAsync<InvalidOperationException>(() => {last}.Run(() => throw new InvalidOperationException()));\n" +
                       "    }\n}\n";
            Add(id, body, id, ["AssertionPasses"]);
        }

        return shapes.ToDictionary(s => s.Id, s => s, StringComparer.Ordinal);
    }

    // The weaver's entry, by reflection, so the "no weave outside WovenIl" scan never sees a literal call and so a
    // verifier error is measured rather than thrown. GetMethod avoids the spelt-out invocation the scan flags.
    private static readonly MethodInfo WeaveMethod =
        typeof(AssertionWeaver).GetMethods().First(m => m.Name == "Weave" && m.GetParameters().Length == 2);

    private static readonly MethodInfo FindStatements =
        typeof(AssertionWeaver).GetMethod("FindAssertionStatements", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static (int WeavedCount, List<string> Unwoven) WeaveByReflection(string path)
    {
        var result = (WeaveResult)WeaveMethod.Invoke(new AssertionWeaver(), new object?[] { path, Path.ChangeExtension(path, ".pdb") })!;
        return (result.WeavedCount, result.Unwoven);
    }

    [Fact]
    public void Dive()
    {
        var output = Environment.GetEnvironmentVariable("KRONIKOL_213_DIVE");
        if (string.IsNullOrEmpty(output))
            return;

        using var writer = new StreamWriter(output, append: false) { AutoFlush = true };
        writer.WriteLine($"# Probe213 static dive: weaver {typeof(AssertionWeaver).Assembly.GetName().Version}, runtime {Environment.Version}");
        writer.WriteLine("# Per shape x build: compile, weave (reflection), ILVerify errors the weave ADDED, and each");
        writer.WriteLine("# captured variable's metadata + its reconstructed value-load. No woven code is run here.");

        var shapes = Shapes();
        foreach (var shape in shapes.Values.OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            foreach (var build in shape.Builds)
            {
                writer.WriteLine();
                writer.WriteLine($"=== {shape.Id} [{build}] ===");
                string path;
                try
                {
                    path = Build($"Probe213_{shape.Id}_{build}", shape.Source, build);
                }
                catch (Exception e)
                {
                    writer.WriteLine("  COMPILE FAILED: " + Flat(e.Message));
                    continue;
                }

                var before = File.ReadAllBytes(path);
                (int WeavedCount, List<string> Unwoven) weave;
                try
                {
                    weave = WeaveByReflection(path);
                }
                catch (Exception e)
                {
                    writer.WriteLine("  WEAVE THREW: " + Flat(e.Message));
                    continue;
                }

                var added = WovenIl.NewErrors(before, File.ReadAllBytes(path), path);
                writer.WriteLine($"  weavedCount={weave.WeavedCount}; ILVerify errors the weave added={added.Count}");
                foreach (var error in added)
                    writer.WriteLine("    ILVERIFY+ " + Flat(error));
                foreach (var u in weave.Unwoven)
                    writer.WriteLine("    unwoven: " + Flat(u));

                DumpStatements(path, writer);
                writer.Flush();
            }
        }
        writer.WriteLine();
        writer.WriteLine("# end");
    }

    private static void DumpStatements(string path, TextWriter writer)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(AppContext.BaseDirectory);
        resolver.AddSearchDirectory(Path.GetDirectoryName(path)!);
        // Read the UNWOVEN statements from a fresh compile is not needed: FindAssertionStatements analyses the current
        // (woven) module's method bodies, but the weave appends; the pre-weave detection is what set the metadata. Read
        // a second, unwoven, build to show the detection the weave acted on.
        using var module = ModuleDefinition.ReadModule(path, new ReaderParameters { ReadSymbols = true, AssemblyResolver = resolver });
        var weaver = new AssertionWeaver();
        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods.Where(m => m.HasBody && m.DebugInformation.HasSequencePoints))
            {
                List<AssertionStatement> statements;
                try
                {
                    statements = (List<AssertionStatement>)FindStatements.Invoke(weaver, new object?[] { method, new WeaveResult() })!;
                }
                catch
                {
                    continue;
                }
                foreach (var st in statements)
                {
                    writer.WriteLine($"  stmt {type.Name}::{method.Name} L{st.SequencePoint.StartLine} awaited={st.IsAwaited} \"{Flat(st.SourceText)}\"");
                    if (st.CapturedVariables.Count == 0)
                    {
                        writer.WriteLine("    captured: none");
                        continue;
                    }
                    foreach (var cv in st.CapturedVariables)
                    {
                        writer.WriteLine($"    captured '{cv.Name}': local={cv.LocalIndex} closureLocal={cv.ClosureLocalIndex} " +
                                         $"closureField={Short(cv.ClosureField)} stateField={Short(cv.StateField)} param={cv.ParameterIndex} box={cv.NeedsBoxing}");
                        writer.WriteLine("      load: " + ReconstructLoad(cv, method));
                        if (cv.StateField is { } sf)
                            writer.WriteLine($"      stateField declaringType={sf.DeclaringType?.FullName} (MoveNext/method owner={method.DeclaringType.FullName})");
                    }
                }
            }
        }
    }

    // The value-load the weave emits for a captured variable, reproduced from its metadata exactly as AssertionWeaver
    // builds the values array (ldarg.0 is the state machine inside MoveNext, or the first parameter in a static method).
    private static string ReconstructLoad(CapturedVariable cv, MethodDefinition method)
    {
        var box = cv.NeedsBoxing ? $"; box {cv.Type?.Name}" : "";
        if (cv.ClosureField != null && cv.ClosureLocalIndex >= 0)
            return $"ldloc {cv.ClosureLocalIndex}; ldfld {Short(cv.ClosureField)}{box}";
        if (cv.ClosureField != null && cv.StateField != null)
            return $"ldarg.0; ldfld {Short(cv.StateField)}; ldfld {Short(cv.ClosureField)}{box}";
        if (cv.StateField != null)
            return $"ldarg.0; ldfld {Short(cv.StateField)}{box}   <-- ldarg.0 is {(method.IsStatic ? "the first parameter" : "the state machine / this")}";
        if (cv.ParameterIndex >= 0)
            return $"ldarg {cv.ParameterIndex}{box}";
        return $"ldloc {cv.LocalIndex}{box}";
    }

    private static string Short(FieldReference? f) =>
        f == null ? "null" : $"{f.DeclaringType?.Name}::{f.Name}";

    [Fact]
    public void Case()
    {
        // List mode: every case the probe knows, one "shape|build|entry" per line, so an orchestrator never keeps its own
        // copy of the shapes' entries.
        var list = Environment.GetEnvironmentVariable("KRONIKOL_213_LIST");
        if (!string.IsNullOrEmpty(list))
        {
            File.WriteAllLines(list, Shapes().Values.OrderBy(s => s.Id, StringComparer.Ordinal)
                .SelectMany(s => s.Builds.SelectMany(b => s.Entries.Select(e => $"{s.Id}|{b}|{e}"))));
            return;
        }

        var shapeId = Environment.GetEnvironmentVariable("KRONIKOL_213_CASE");
        var outFile = Environment.GetEnvironmentVariable("KRONIKOL_213_CASE_OUT");
        if (string.IsNullOrEmpty(shapeId) || string.IsNullOrEmpty(outFile))
            return;

        var skip = (Environment.GetEnvironmentVariable("KRONIKOL_213_CASE_SKIP") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        // ONLY "build|entry": run exactly that case, so each case can have a fresh process of its own.
        var only = Environment.GetEnvironmentVariable("KRONIKOL_213_CASE_ONLY");

        var shape = Shapes()[shapeId];
        using var writer = new StreamWriter(outFile, append: true) { AutoFlush = true };

        foreach (var build in shape.Builds)
        {
            foreach (var entry in shape.Entries)
            {
                var key = $"{build}|{entry}";
                if (skip.Contains(key) || (!string.IsNullOrEmpty(only) && key != only))
                    continue;

                writer.WriteLine($"ENTER {shapeId}|{build}|{entry}");
                writer.Flush();

                string woven, plain;
                try
                {
                    plain = Build($"Probe213Case_{shapeId}_{build}_{entry}_plain", shape.Source, build);
                    woven = Build($"Probe213Case_{shapeId}_{build}_{entry}", shape.Source, build);
                    WeaveByReflection(woven);
                }
                catch (Exception e)
                {
                    writer.WriteLine($"RESULT {shapeId}|{build}|{entry} :: BUILD/WEAVE FAILED: {Flat(e.Message)}");
                    writer.WriteLine($"DONE {shapeId}|{build}|{entry}");
                    writer.Flush();
                    continue;
                }

                // KRONIKOL_213_VALUES=1: redirect the woven Track.*WithValues calls to Probe213Recorder (same signatures;
                // it keeps the values array and forwards), so the values the weave LOADED can be identified even where
                // the note does not show them. Off by default, so the outcome table is measured on the weave as written.
                var record = Environment.GetEnvironmentVariable("KRONIKOL_213_VALUES") == "1";
                if (record)
                    RedirectToRecorder(woven);

                var (plainThrown, _, _, _) = RunEntry(plain, shape.TypeName, entry);
                Probe213Recorder.Clear();
                var (wovenThrown, notes, registry, entryTask) = RunEntry(woven, shape.TypeName, entry);

                var noteText = notes.Length == 0
                    ? "(no note)"
                    : string.Join(" || ", notes.Select(n => $"{(n.Passed ? "PASS" : "FAIL")} [{Flat(n.Label)}]{(n.Message is { } m ? " msg=" + Flat(m) : "")}"));
                writer.WriteLine($"RESULT {shapeId}|{build}|{entry} :: unwoven={Describe(plainThrown)} :: woven={Describe(wovenThrown)} :: note={noteText}");
                if (record)
                    writer.WriteLine($"VALUES {shapeId}|{build}|{entry} :: {DescribeRecorded(registry, entryTask, typesToo: false)}");
                writer.WriteLine($"DONE {shapeId}|{build}|{entry}");
                writer.Flush();
                // Last, after DONE: the type of any value the probe did not create. A value read from the wrong offset can
                // be a pointer that is not an object, and reading its type is the one step here that could kill the host,
                // so it runs only once everything else about the case is on disk.
                if (record)
                {
                    writer.WriteLine($"TYPES {shapeId}|{build}|{entry} :: {DescribeRecorded(registry, entryTask, typesToo: true)}");
                    writer.Flush();
                }
            }
        }
        writer.WriteLine($"ALLDONE {shapeId}");
        writer.Flush();
    }

    // Swaps each woven call to Track.AssertionPassedWithValues / AssertionFailedWithValues / AssertionTaskWithValues(<T>)
    // for the recorder's method of the same signature. Only the call's target changes: every value-load the weave wrote
    // runs exactly as before, and the recorder forwards to Track, so the note is the same.
    private static void RedirectToRecorder(string path)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(AppContext.BaseDirectory);
        resolver.AddSearchDirectory(Path.GetDirectoryName(path)!);
        var recorder = typeof(Probe213Recorder);
        using var module = ModuleDefinition.ReadModule(path,
            new ReaderParameters { ReadWrite = true, ReadSymbols = true, AssemblyResolver = resolver });
        foreach (var instruction in module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody)
                     .SelectMany(m => m.Body.Instructions))
        {
            if (instruction.OpCode != OpCodes.Call || instruction.Operand is not MethodReference called ||
                called.DeclaringType.FullName != "Kronikol.Tracking.Track" ||
                called.Name is not ("AssertionPassedWithValues" or "AssertionFailedWithValues" or "AssertionTaskWithValues"))
                continue;
            if (called is GenericInstanceMethod generic)
            {
                var definition = recorder.GetMethods().Single(m => m.Name == called.Name && m.IsGenericMethodDefinition);
                var instance = new GenericInstanceMethod(module.ImportReference(definition));
                foreach (var argument in generic.GenericArguments)
                    instance.GenericArguments.Add(argument);
                instruction.Operand = instance;
            }
            else
            {
                var definition = recorder.GetMethods().Single(m => m.Name == called.Name && !m.IsGenericMethodDefinition &&
                                                                   m.GetParameters().Length == called.Parameters.Count);
                instruction.Operand = module.ImportReference(definition);
            }
        }
        module.Write(new WriterParameters { WriteSymbols = true });
    }

    // Each recorded call: its expression and every name = value the weave passed, a value named by what it IS among the
    // objects this run created (the test instance, each Harness and C by its tag, the entry's own task, which for an
    // async method is its state-machine box), or by its value for a number or string. typesToo adds the runtime type of
    // anything else, which is the risky step (see the caller).
    private static string DescribeRecorded(List<(string Name, object Obj)> registry, object? entryTask, bool typesToo)
    {
        var calls = Probe213Recorder.Snapshot();
        if (calls.Count == 0)
            return "(no values call recorded)";
        return string.Join(" || ", calls.Select(c =>
            $"[{Flat(c.Expression)}] " + string.Join(", ", c.Names.Select((n, i) =>
                $"{n} = {Identify(i < c.Values.Length ? c.Values[i] : null, registry, entryTask, typesToo)}"))));
    }

    private static string Identify(object? value, List<(string Name, object Obj)> registry, object? entryTask, bool typesToo)
    {
        if (value is null)
            return "null";
        foreach (var (name, obj) in registry)
            if (ReferenceEquals(obj, value))
                return name;
        if (entryTask != null && ReferenceEquals(entryTask, value))
            return "the entry's own Task (its async state-machine box)";
        // `is` compares the object's type handle with the target's and dereferences nothing further.
        switch (value)
        {
            case int i: return $"int {i}";
            case long l: return $"long {l}";
            case bool b: return $"bool {b}";
            case string s: return $"\"{s}\"";
        }
        if (value.GetType() == typeof(object))
            return "a plain object (an argument slot the statement never filled)";
        return typesToo ? $"UNCREATED {value.GetType().FullName}: {Flat(SafeText(value))}" : "UNCREATED (not an object this run made)";
    }

    private static string SafeText(object value)
    {
        try { return value.ToString() ?? "null"; }
        catch (Exception e) { return "ToString threw " + e.GetType().Name; }
    }

    // Builds an instance with the type's widest constructor, giving each Harness/C argument a distinctive tag so the
    // note (or the recorded values) reveals WHICH object was loaded. Closes a generic test type over Harness. Every object
    // it makes is registered under a name, so a recorded value can be identified by reference.
    private static (Exception? Thrown, WovenRun.Note[] Notes, List<(string Name, object Obj)> Registry, object? EntryTask)
        RunEntry(string assemblyPath, string typeName, string entry)
    {
        var registry = new List<(string Name, object Obj)>();
        var assembly = Assembly.LoadFrom(assemblyPath);
        var type = assembly.GetType(typeName, throwOnError: true)!;
        if (type.IsGenericTypeDefinition)
            type = type.MakeGenericType(assembly.GetType("Harness", throwOnError: true)!);

        var method = type.GetMethod(entry)!;
        object? instance = method.IsStatic ? null : MakeInstance(type, assembly, registry);
        if (instance != null)
            registry.Add(($"the test instance ({type.Name})", instance));
        var methodArgs = method.GetParameters().Select(p => MakeArg(p.ParameterType, p.Name ?? "arg", assembly, registry)).ToArray();

        var testId = $"{typeName}_{entry}_{Guid.NewGuid():N}";
        Exception? thrown;
        object? entryTask = null;
        using (TestIdentityScope.Begin(testId, testId))
        {
            thrown = Record.Exception(() =>
            {
                var r = method.Invoke(instance, methodArgs.Length == 0 ? null : methodArgs);
                entryTask = r;
                if (r is Task task)
                    task.GetAwaiter().GetResult();
            });
        }
        if (thrown is TargetInvocationException { InnerException: { } inner })
            thrown = inner;
        return (thrown, WovenRun.NotesFor(testId), registry, entryTask);
    }

    private static object MakeInstance(Type type, Assembly assembly, List<(string Name, object Obj)> registry)
    {
        var ctor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var args = ctor.GetParameters().Select(p => MakeArg(p.ParameterType, p.Name ?? "arg", assembly, registry)).ToArray();
        return ctor.Invoke(args)!;
    }

    private static object? MakeArg(Type parameterType, string name, Assembly assembly, List<(string Name, object Obj)> registry)
    {
        if (parameterType.Name == "Harness")
        {
            var harness = Activator.CreateInstance(parameterType, new object?[] { name })!;
            registry.Add(($"Harness({name}) [the `{name}` argument]", harness));
            return harness;
        }
        if (parameterType.Name == "C")
        {
            var c = Activator.CreateInstance(parameterType)!;
            parameterType.GetField("_id")?.SetValue(c, 2);
            var harnessType = assembly.GetType("Harness", throwOnError: true)!;
            var inner = Activator.CreateInstance(harnessType, new object?[] { name + "-harness" })!;
            parameterType.GetField("_harness")?.SetValue(c, inner);
            registry.Add(($"C [the `{name}` argument, _id=2]", c));
            registry.Add(($"Harness({name}-harness) [{name}._harness]", inner));
            return c;
        }
        if (parameterType == typeof(int)) return 7;
        if (parameterType == typeof(long)) return 7L;
        if (parameterType == typeof(string)) return name;
        return parameterType.IsValueType ? Activator.CreateInstance(parameterType) : null;
    }

    private static string Describe(Exception? e) =>
        e == null ? "nothing" : $"{e.GetType().Name}: {Flat(e.Message)}";

    private static string Flat(string? text) =>
        (text ?? "").ReplaceLineEndings(" / ").Replace("​", "<zwsp>");
}

/// <summary>
/// Probe213 only (KRONIKOL_213_VALUES=1): the woven calls to Track's value-taking methods are redirected here, with the
/// same signatures. Each keeps a reference to the values array the weave built (no copy, so nothing is read from a value
/// that may not be an object) and forwards to Track, so the note is drawn exactly as it would be.
/// </summary>
public static class Probe213Recorder
{
    private static readonly object Gate = new();
    private static readonly List<(string Expression, string[] Names, object?[] Values)> Calls = new();

    public static void Clear()
    {
        lock (Gate) Calls.Clear();
    }

    public static List<(string Expression, string[] Names, object?[] Values)> Snapshot()
    {
        lock (Gate) return new List<(string, string[], object?[])>(Calls);
    }

    private static void Keep(string expression, string[] varNames, object?[] varValues)
    {
        lock (Gate) Calls.Add((expression, varNames, varValues));
    }

    public static void AssertionPassedWithValues(string expression, string[] varNames, object?[] varValues,
        string? callerFilePath = null, int callerLineNumber = 0)
    {
        Keep(expression, varNames, varValues);
        Track.AssertionPassedWithValues(expression, varNames, varValues, callerFilePath, callerLineNumber);
    }

    public static void AssertionFailedWithValues(string expression, string failureMessage, string[] varNames, object?[] varValues,
        string? callerFilePath = null, int callerLineNumber = 0)
    {
        Keep(expression, varNames, varValues);
        Track.AssertionFailedWithValues(expression, failureMessage, varNames, varValues, callerFilePath, callerLineNumber);
    }

    public static Task AssertionTaskWithValues(Task task, string expression, string[] varNames, object?[] varValues,
        string? callerFilePath = null, int callerLineNumber = 0)
    {
        Keep(expression, varNames, varValues);
        return Track.AssertionTaskWithValues(task, expression, varNames, varValues, callerFilePath, callerLineNumber);
    }

    public static Task<T> AssertionTaskWithValues<T>(Task<T> task, string expression, string[] varNames, object?[] varValues,
        string? callerFilePath = null, int callerLineNumber = 0)
    {
        Keep(expression, varNames, varValues);
        return Track.AssertionTaskWithValues(task, expression, varNames, varValues, callerFilePath, callerLineNumber);
    }
}
