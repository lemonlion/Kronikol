using System.Reflection;
using System.Text.RegularExpressions;
using Kronikol.AssertionTracking;
using Kronikol.Tracking;
using Mono.Cecil;
using Mono.Cecil.Cil;
using static Kronikol.Tests.AssertionTracking.TestAssemblyBuilder;

namespace Kronikol.Tests.AssertionTracking;

/// <summary>
/// A field an assertion's values read is read off the object the code reads it from (#213). Through 5.31.1 a lambda's
/// primary-constructor parameter, kept in a field named <c>&lt;name&gt;P</c>, was loaded <c>ldarg.0; ldfld</c> in an
/// async method's <c>MoveNext</c> and in a closure's method, where <c>ldarg.0</c> is the state machine or the closure:
/// the note recorded null, the test instance or another object, or the test process died with an
/// AccessViolationException. Each shape is compiled the three ways a project builds it, and <see cref="WovenIl.Weave"/>
/// verifies the woven IL before anything runs, so a shape that would kill the process fails a fact instead.
/// </summary>
public class CapturedFieldValueTests
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

        // P13d — P13's read in a delegate: the lambda reads _id off `other`, through its closure, never off `this`.
        AddC("P13d", """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            using Kronikol.Tracking;
            using Shouldly;
            [assembly: TrackAssertions]
            public class P13d
            {
                private int _id = 1;
                public void SyncAssert()
                {
                    var other = new P13d(); other._id = 2;
                    Should.NotThrow(() => GC.KeepAlive(other._id));
                }
                public async Task AsyncAssert()
                {
                    await Task.Yield();
                    var other = new P13d(); other._id = 2;
                    Should.NotThrow(() => GC.KeepAlive(other._id));
                }
            }
            """, "P13d", ["SyncAssert", "AsyncAssert"]);

        // P24 — a field read at a merge point: (_parent ?? this)._depth has its object from two paths.
        AddC("P24", """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            using Kronikol.Tracking;
            using Shouldly;
            [assembly: TrackAssertions]
            public class P24
            {
                private readonly P24? _parent;
                private readonly int _depth;
                public P24() : this(new P24(null, 2), 1) { }
                private P24(P24? parent, int depth) { _parent = parent; _depth = depth; }
                public void SyncAssert() => 2.ShouldBe((_parent ?? this)._depth);
                public async Task AsyncAssert() { await Task.Yield(); 2.ShouldBe((_parent ?? this)._depth); }
            }
            """, "P24", ["SyncAssert", "AsyncAssert"]);

        // P25 — a method group on another object: its body's _id is other's, never this one's.
        AddC("P25", """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            using Kronikol.Tracking;
            using Shouldly;
            [assembly: TrackAssertions]
            public class P25
            {
                private int _id = 1;
                public void Fail() { GC.KeepAlive(_id); throw new InvalidOperationException(); }
                public void SyncAssert()
                {
                    var other = new P25(); other._id = 2;
                    Should.Throw<InvalidOperationException>(other.Fail, "_id");
                }
            }
            """, "P25", ["SyncAssert"]);

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

    public static TheoryData<string, BuildConfiguration> ShapesAndBuilds()
    {
        var data = new TheoryData<string, BuildConfiguration>();
        foreach (var shape in Shapes().Values.OrderBy(s => s.Id, StringComparer.Ordinal))
            foreach (var build in shape.Builds)
                data.Add(shape.Id, build);
        return data;
    }

    public static TheoryData<string, BuildConfiguration, string> Cases()
    {
        var data = new TheoryData<string, BuildConfiguration, string>();
        foreach (var shape in Shapes().Values.OrderBy(s => s.Id, StringComparer.Ordinal))
            foreach (var build in shape.Builds)
                foreach (var entry in shape.Entries)
                    data.Add(shape.Id, build, entry);
        return data;
    }

    [Theory]
    [MemberData(nameof(ShapesAndBuilds))]
    public void A_woven_shape_verifies(string id, BuildConfiguration build)
    {
        var shape = Shapes()[id];

        WovenIl.Weave(new AssertionWeaver(), Build($"Captured213_{id}_{build}_Verify", shape.Source, build));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_woven_shape_runs_as_the_unwoven_one(string id, BuildConfiguration build, string entry)
    {
        var shape = Shapes()[id];
        var plain = Build($"Captured213_{id}_{build}_{entry}_Plain", shape.Source, build);
        var woven = Build($"Captured213_{id}_{build}_{entry}_Woven", shape.Source, build);
        WovenIl.Weave(new AssertionWeaver(), woven);

        var (plainThrown, _, _, _) = RunEntry(plain, shape.TypeName, entry);
        var (wovenThrown, _, _, _) = RunEntry(woven, shape.TypeName, entry);

        Assert.Equal($"{id} {build} {entry}: {Describe(plainThrown)}", $"{id} {build} {entry}: {Describe(wovenThrown)}");
    }

        private const string Scratch = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using FluentAssertions;
        using Shouldly;
        using Kronikol.Tracking;

        [assembly: TrackAssertions]

        public sealed class Harness
        {
            public Harness(string name) => Name = name;
            public string Name { get; }
            public Task Run(Func<Task> run) => run();
            public Task Run(string what, Func<Task> run) => run();
            public override string ToString() => Name;
        }

        public class Other { public int _id = 42; }

        public class Pc(Harness first, Harness second, Harness third)
        {
            private long _count = 7;
            private Exception? _exception;
            private readonly List<string> _items = new() { "a1", "b2" };

            public async Task Async_lambda_this_only()
            {
                await Task.Yield();
                _exception = await Should.ThrowAsync<InvalidOperationException>(() => third.Run(() => throw new InvalidOperationException()));
                _exception.ShouldNotBeNull();
                (first, second).ShouldNotBe((null, null));
            }

            public async Task Async_lambda_with_local()
            {
                using var cts = new CancellationTokenSource();
                await cts.CancelAsync();
                _exception = await Should.ThrowAsync<OperationCanceledException>(
                    () => third.Run("x", () => Task.FromCanceled(cts.Token)));
            }

            public async Task Async_statement_names_parameter()
            {
                await Task.Yield();
                third.Name.ShouldBe(third.Name);
                first.Name.Should().Be(first.Name);
                _count.ShouldBe(7);
            }

            public void Sync_lambda_and_statement()
            {
                Should.Throw<InvalidOperationException>(() => third.Run(() => throw new InvalidOperationException()).GetAwaiter().GetResult());
                third.Name.ShouldBe(third.Name);
            }

            public void Sync_expression_tree()
            {
                var expected = "a1";
                _items.Should().Contain(x => x == expected && first.Name == "first");
            }

            public async Task Async_expression_tree()
            {
                await Task.Yield();
                _items.Should().Contain(x => x != third.Name);
            }
        }

        public class Receivers
        {
            public static string Expected = "a1";
            private long _count = 3;
            private readonly List<string> _items = new() { "a1", "b2" };

            public void Lambda_reads_another_objects_field()
            {
                var other = new Other();
                Should.NotThrow(() => Use(other._id));
            }

            public async Task Async_another_objects_field()
            {
                var other = await Task.FromResult(new Other());
                other._id.Should().Be(other._id);
                _count.ShouldBe(3);
            }

            public static void Static_parameters_field(Other foo)
            {
                foo._id.Should().Be(foo._id);
            }

            public void Expression_tree_static_field()
            {
                _items.Should().Contain(x => x == Expected);
            }

            private static void Use(int x) { }
        }

        public abstract class BaseOf<T> { protected T Value = default!; }

        public class Derived : BaseOf<int>
        {
            public Derived() { Value = 5; }
            public void Sync_generic_base_field() { Value.Should().Be(Value); }
            public async Task Async_generic_base_field() { await Task.Yield(); Value.Should().Be(Value); }
            public void Sync_lambda_generic_base_field() { Should.NotThrow(() => Use(Value)); }
            public void Sync_expression_generic_base_field() { new[] { 5 }.Should().Contain(x => x == Value); }
            private static void Use(int x) { }
        }

        public static class Defaults { public static readonly string Expected = "a1"; }

        public class Statics
        {
            private static readonly string Mine = "a1";
            private readonly List<string> _items = new() { "a1", "b2" };
            private int _n = 1;

            public void Other_class_static() { _items.Should().Contain(x => x == Defaults.Expected); }
            public async Task Async_own_static() { await Task.Yield(); _n.ShouldBe(1); _items.Should().Contain(x => x == Mine); }
        }

        public class Gen<T>(T value)
        {
            public async Task Async_generic_lambda() { await Task.Yield(); Should.NotThrow(() => Use(value)); }
            public void Sync_generic_expression() { new List<T> { value }.Should().Contain(x => Equals(x, value)); }
            private static void Use(T x) { }
        }

        public class Closures
        {
            private volatile Harness _v = new Harness("v");

            public void Inner_lambda_reads_its_closures_field()
            {
                var expected = 1;
                new List<int> { 1 }.ForEach(x => x.ShouldSatisfyAllConditions(v => v.ShouldBe(expected)));
            }

            public void Child_closure_reads_its_parents_field()
            {
                var expected = 1;
                new List<int> { 1 }.ForEach(x => { var y = 0; x.ShouldSatisfyAllConditions(v => v.ShouldBe(expected + y)); });
            }

            public async Task Async_lambda_reads_its_closures_field()
            {
                var expected = 1;
                Func<int, Task> check = async x => { await Task.Yield(); x.ShouldSatisfyAllConditions(v => v.ShouldBe(expected)); };
                await check(1);
            }

            public void Lambda_reads_a_volatile_field() { _v.ShouldSatisfyAllConditions(h => GC.KeepAlive(_v)); }
        }

        file class FileSteps(Harness third)
        {
            public void Lambda_reads_a_parameter() { third.ShouldSatisfyAllConditions(h => GC.KeepAlive(third)); }
        }

        public struct StructSteps
        {
            private int _f;
            public StructSteps() { _f = 7; }
            public async Task Async_struct_field() { await Task.Yield(); (await Task.FromResult(7)).ShouldBe(_f); }
        }

        public class InLambdas
        {
            private readonly string _expected = "a1";
            private readonly List<List<string>> _lists = new() { new() { "a1" } };

            public void Assertion_inside_lambda()
            {
                var local = "a1";
                _lists.ForEach(list => list.Should().Contain(x => x == local && x == _expected));
            }

            public void Nested_lambda_inside_lambda()
            {
                var expected = 1;
                new List<int> { 1 }.ForEach(x => Should.NotThrow(() => Check(x, expected)));
            }

            private static void Check(int a, int b) { }
        }
        """;

    // Each case of Scratch: the type, the method, how to make its instance (null for a static method) and its arguments.
    private static (string Type, string Method, Func<Assembly, object?> Instance, Func<Assembly, object?[]?> Args)[] ScratchCases()
    {
        static object Harness(Assembly a, string name) => Activator.CreateInstance(a.GetType("Harness")!, name)!;
        static object Pc(Assembly a) =>
            Activator.CreateInstance(a.GetType("Pc")!, Harness(a, "first"), Harness(a, "second"), Harness(a, "third"))!;
        static object New(Assembly a, string type) => Activator.CreateInstance(a.GetType(type)!)!;
        static object Gen(Assembly a) => Activator.CreateInstance(a.GetType("Gen`1")!.MakeGenericType(typeof(string)), "v")!;
        static object?[]? None(Assembly a) => null;
        static Type FileLocal(Assembly a, string name) => a.GetTypes().Single(t => t.Name.EndsWith("__" + name, StringComparison.Ordinal));
        return
        [
            ("Pc", "Async_lambda_this_only", Pc, None),
            ("Pc", "Async_lambda_with_local", Pc, None),
            ("Pc", "Async_statement_names_parameter", Pc, None),
            ("Pc", "Sync_lambda_and_statement", Pc, None),
            ("Pc", "Sync_expression_tree", Pc, None),
            ("Pc", "Async_expression_tree", Pc, None),
            ("Receivers", "Lambda_reads_another_objects_field", a => New(a, "Receivers"), None),
            ("Receivers", "Async_another_objects_field", a => New(a, "Receivers"), None),
            ("Receivers", "Static_parameters_field", _ => null, a => [New(a, "Other")]),
            ("Receivers", "Expression_tree_static_field", a => New(a, "Receivers"), None),
            ("Derived", "Sync_generic_base_field", a => New(a, "Derived"), None),
            ("Derived", "Async_generic_base_field", a => New(a, "Derived"), None),
            ("Derived", "Sync_lambda_generic_base_field", a => New(a, "Derived"), None),
            ("Derived", "Sync_expression_generic_base_field", a => New(a, "Derived"), None),
            ("Statics", "Other_class_static", a => New(a, "Statics"), None),
            ("Statics", "Async_own_static", a => New(a, "Statics"), None),
            ("Gen`1", "Async_generic_lambda", Gen, None),
            ("Gen`1", "Sync_generic_expression", Gen, None),
            ("Closures", "Inner_lambda_reads_its_closures_field", a => New(a, "Closures"), None),
            ("Closures", "Child_closure_reads_its_parents_field", a => New(a, "Closures"), None),
            ("Closures", "Async_lambda_reads_its_closures_field", a => New(a, "Closures"), None),
            ("Closures", "Lambda_reads_a_volatile_field", a => New(a, "Closures"), None),
            ("FileSteps", "Lambda_reads_a_parameter", a => Activator.CreateInstance(FileLocal(a, "FileSteps"), Harness(a, "third")), None),
            ("StructSteps", "Async_struct_field", a => New(a, "StructSteps"), None),
            ("InLambdas", "Assertion_inside_lambda", a => New(a, "InLambdas"), None),
            ("InLambdas", "Nested_lambda_inside_lambda", a => New(a, "InLambdas"), None),
        ];
    }

    // What each case of Scratch does once woven: "Type.Method: ran" or "...: threw X", then a line per note it drew.
    private static List<string> ScratchOutcomes(BuildConfiguration build)
    {
        var path = Build($"Captured213_Scratch_{build}", Scratch, build);
        WovenIl.Weave(new AssertionWeaver(), path);
        var assembly = Assembly.LoadFrom(path);
        var lines = new List<string>();
        foreach (var (typeName, methodName, instance, args) in ScratchCases())
        {
            var target = instance(assembly);
            var method = (target?.GetType() ?? assembly.GetType(typeName)!).GetMethod(methodName)!;
            var testId = $"{methodName}_{Guid.NewGuid():N}";
            Exception? thrown;
            using (TestIdentityScope.Begin(testId, testId))
            {
                thrown = Record.Exception(() =>
                {
                    if (method.Invoke(target, args(assembly)) is Task task)
                        task.GetAwaiter().GetResult();
                });
            }
            if (thrown is TargetInvocationException { InnerException: { } inner })
                thrown = inner;
            lines.Add($"{typeName}.{methodName}: {(thrown == null ? "ran" : "threw " + thrown.GetType().Name)}");
            lines.AddRange(WovenRun.NotesFor(testId).Select(n => $"  {(n.Passed ? "pass" : "fail")} | {n.Label}"));
        }
        return lines;
    }

    // The cases whose note the rule decides. On 5.31.1 each of them ended the test process, or drew null, another
    // object, a number that was not the parameter's, or the parameter's name where its value belongs.
    private static readonly (string Id, BuildConfiguration Build, string Entry, string Notes)[] Decided =
    [
        ("P12_1p_fields", BuildConfiguration.ReleaseDebugPlus, "AssertionPasses", "PASS [Should throw InvalidOperationException]"),
        ("P12_1p_fields", BuildConfiguration.Release, "AssertionPasses", "PASS [Should throw InvalidOperationException]"),
        ("P12_1p", BuildConfiguration.ReleaseDebugPlus, "AssertionPasses", "PASS [Should throw InvalidOperationException]"),
        ("P12_1p", BuildConfiguration.Release, "AssertionPasses", "PASS [Should throw InvalidOperationException]"),
        ("P12_2p_fields", BuildConfiguration.ReleaseDebugPlus, "AssertionPasses", "PASS [Should throw InvalidOperationException]"),
        ("P12_2p_fields", BuildConfiguration.Release, "AssertionPasses", "PASS [Should throw InvalidOperationException]"),
        ("P12_2p", BuildConfiguration.ReleaseDebugPlus, "AssertionPasses", "PASS [Should throw InvalidOperationException]"),
        ("P12_2p", BuildConfiguration.Release, "AssertionPasses", "PASS [Should throw InvalidOperationException]"),
        ("P12_4p_fields", BuildConfiguration.ReleaseDebugPlus, "AssertionPasses", "PASS [Should throw InvalidOperationException]"),
        ("P12_4p_fields", BuildConfiguration.Release, "AssertionPasses", "PASS [Should throw InvalidOperationException]"),
        ("P12_4p", BuildConfiguration.ReleaseDebugPlus, "AssertionPasses", "PASS [Should throw InvalidOperationException]"),
        ("P12_4p", BuildConfiguration.Release, "AssertionPasses", "PASS [Should throw InvalidOperationException]"),
        ("P12_6p_fields", BuildConfiguration.ReleaseDebugPlus, "AssertionPasses", "PASS [Should throw InvalidOperationException]"),
        ("P12_6p_fields", BuildConfiguration.Release, "AssertionPasses", "PASS [Should throw InvalidOperationException]"),
        ("P12_6p", BuildConfiguration.ReleaseDebugPlus, "AssertionPasses", "PASS [Should throw InvalidOperationException]"),
        ("P12_6p", BuildConfiguration.Release, "AssertionPasses", "PASS [Should throw InvalidOperationException]"),
        ("P18", BuildConfiguration.Debug, "Parameter", "PASS [(await GetParameter()) should be 'Harness(expected)']"),
        ("P18", BuildConfiguration.ReleaseDebugPlus, "Parameter", "PASS [(await GetParameter()) should be 'Harness(expected)']"),
        ("P18", BuildConfiguration.Release, "Parameter", "PASS [(await GetParameter()) should be 'Harness(expected)']"),
        ("P19", BuildConfiguration.Debug, "Int", "PASS [Third should satisfy all conditions [ h => GC.KeepAlive('7') ]]"),
        ("P19", BuildConfiguration.Debug, "Long", "PASS [Third should satisfy all conditions [ h => GC.KeepAlive('7') ]]"),
        ("P19", BuildConfiguration.ReleaseDebugPlus, "Int", "PASS [Third should satisfy all conditions [ h => GC.KeepAlive('7') ]]"),
        ("P19", BuildConfiguration.ReleaseDebugPlus, "Long", "PASS [Third should satisfy all conditions [ h => GC.KeepAlive('7') ]]"),
        ("P19", BuildConfiguration.Release, "Int", "PASS [Third should satisfy all conditions [ h => GC.KeepAlive('7') ]]"),
        ("P19", BuildConfiguration.Release, "Long", "PASS [Third should satisfy all conditions [ h => GC.KeepAlive('7') ]]"),
        ("P1", BuildConfiguration.Debug, "AssertionPasses", "PASS [Should throw InvalidOperationException] || PASS [Exception should not be null] || PASS [(first, second) should not be (null, null)]"),
        ("P22b", BuildConfiguration.Debug, "AssertionPasses", "PASS [New[] { 1 } should all satisfy [ x => GC.KeepAlive('Harness(third)') ]]"),
        ("P22b", BuildConfiguration.ReleaseDebugPlus, "AssertionPasses", "PASS [New[] { 1 } should all satisfy [ x => GC.KeepAlive('Harness(third)') ]]"),
        ("P22b", BuildConfiguration.Release, "AssertionPasses", "PASS [New[] { 1 } should all satisfy [ x => GC.KeepAlive('Harness(third)') ]]"),
        ("P23", BuildConfiguration.ReleaseDebugPlus, "InAnAsyncClosure", "PASS [Should throw InvalidOperationException]"),
        ("P23", BuildConfiguration.Release, "InAnAsyncClosure", "PASS [Should throw InvalidOperationException]"),
        ("P6", BuildConfiguration.ReleaseDebugPlus, "AssertionPasses", "PASS [Should throw InvalidOperationException]"),
        ("P6", BuildConfiguration.Release, "AssertionPasses", "PASS [Should throw InvalidOperationException]"),
        ("P8", BuildConfiguration.Debug, "AsyncAssert", "PASS [New List~<Harness> { expected } should contain [ x => x == 'Harness(expected)' ]]"),
        ("P8", BuildConfiguration.Debug, "SyncAssert", "PASS [New List~<Harness> { expected } should contain [ x => x == 'Harness(expected)' ]]"),
        ("P8", BuildConfiguration.ReleaseDebugPlus, "AsyncAssert", "PASS [New List~<Harness> { expected } should contain [ x => x == 'Harness(expected)' ]]"),
        ("P8", BuildConfiguration.ReleaseDebugPlus, "SyncAssert", "PASS [New List~<Harness> { expected } should contain [ x => x == 'Harness(expected)' ]]"),
        ("P8", BuildConfiguration.Release, "AsyncAssert", "PASS [New List~<Harness> { expected } should contain [ x => x == 'Harness(expected)' ]]"),
        ("P8", BuildConfiguration.Release, "SyncAssert", "PASS [New List~<Harness> { expected } should contain [ x => x == 'Harness(expected)' ]]"),
    ];

    public static TheoryData<string, BuildConfiguration, string> DecidedCases()
    {
        var data = new TheoryData<string, BuildConfiguration, string>();
        foreach (var (id, build, entry, _) in Decided)
            data.Add(id, build, entry);
        return data;
    }

    [Theory]
    [MemberData(nameof(DecidedCases))]
    public void A_note_draws_the_value_the_source_names(string id, BuildConfiguration build, string entry)
    {
        var shape = Shapes()[id];
        var woven = Build($"Captured213_{id}_{build}_{entry}_Note", shape.Source, build);
        WovenIl.Weave(new AssertionWeaver(), woven);

        var (thrown, notes, _, _) = RunEntry(woven, shape.TypeName, entry);

        Assert.Null(thrown);
        Assert.Equal(Decided.Single(d => d.Id == id && d.Build == build && d.Entry == entry).Notes, NotesText(notes));
    }

    private static string NotesText(WovenRun.Note[] notes) =>
        notes.Length == 0
            ? "(no note)"
            : string.Join(" || ", notes.Select(n => $"{(n.Passed ? "PASS" : "FAIL")} [{Flat(n.Label)}]{(n.Message is { } m ? " msg=" + Flat(m) : "")}"));

    // What each case of Scratch does once woven, the same in every build: each runs, and each note draws the value its
    // source names (a primary constructor's parameter, a member read through it, a closure's own field), or the name as
    // written where the weave cannot read it.
    private static readonly string[] ScratchNotes =
    [
        "Pc.Async_lambda_this_only: ran",
        "  pass | Should throw InvalidOperationException",
        "  pass | Exception should not be null",
        "  pass | (first, second) should not be (null, null)",
        "Pc.Async_lambda_with_local: ran",
        "  pass | Should throw OperationCanceledException",
        "Pc.Async_statement_names_parameter: ran",
        "  pass | Third name should be 'third'",
        "  pass | First name should be 'first'",
        "  pass | Count should be 7",
        "Pc.Sync_lambda_and_statement: ran",
        "  pass | Should throw InvalidOperationException",
        "  pass | Third name should be 'third'",
        "Pc.Sync_expression_tree: ran",
        "  pass | Items should contain [ x => x == 'a1' && 'first' == \"first\" ]",
        "Pc.Async_expression_tree: ran",
        "  pass | Items should contain [ x => x != 'third' ]",
        "Receivers.Lambda_reads_another_objects_field: ran",
        "  pass | Should not throw",
        "Receivers.Async_another_objects_field: ran",
        "  pass | Other id should be '42'",
        "  pass | Count should be 3",
        "Receivers.Static_parameters_field: ran",
        "  pass | Foo id should be '42'",
        "Receivers.Expression_tree_static_field: ran",
        "  pass | Items should contain [ x => x == 'a1' ]",
        "Derived.Sync_generic_base_field: ran",
        "  pass | Value should be '5'",
        "Derived.Async_generic_base_field: ran",
        "  pass | Value should be '5'",
        "Derived.Sync_lambda_generic_base_field: ran",
        "  pass | Should not throw",
        "Derived.Sync_expression_generic_base_field: ran",
        "  pass | New[] { 5 } should contain [ x => x == '5' ]",
        "Statics.Other_class_static: ran",
        "  pass | Items should contain [ x => x == Defaults.Expected ]",
        "Statics.Async_own_static: ran",
        "  pass | N should be 1",
        "  pass | Items should contain [ x => x == 'a1' ]",
        "Gen`1.Async_generic_lambda: ran",
        "  pass | Should not throw",
        "Gen`1.Sync_generic_expression: ran",
        "  pass | New List~<T> { value } should contain [ x => Equals(x, 'v') ]",
        "Closures.Inner_lambda_reads_its_closures_field: ran",
        "  pass | V should be '1'",
        "  pass | X should satisfy all conditions [ v => v.ShouldBe('1') ]",
        "Closures.Child_closure_reads_its_parents_field: ran",
        "  pass | V should be '1'",
        "  pass | X should satisfy all conditions [ v => v.ShouldBe('1' + '0') ]",
        "Closures.Async_lambda_reads_its_closures_field: ran",
        "  pass | V should be '1'",
        "  pass | X should satisfy all conditions [ v => v.ShouldBe('1') ]",
        "Closures.Lambda_reads_a_volatile_field: ran",
        "  pass | V should satisfy all conditions [ h => GC.KeepAlive('v') ]",
        "FileSteps.Lambda_reads_a_parameter: ran",
        "  pass | Third should satisfy all conditions [ h => GC.KeepAlive('third') ]",
        "StructSteps.Async_struct_field: ran",
        "  pass | (await Task.FromResult(7)) should be '7'",
        "InLambdas.Assertion_inside_lambda: ran",
        "  pass | List should contain [ x => x == 'a1' && x == 'a1' ]",
        "InLambdas.Nested_lambda_inside_lambda: ran",
        "  pass | Should not throw",
    ];

    [Theory]
    [InlineData(BuildConfiguration.Debug)]
    [InlineData(BuildConfiguration.Release)]
    [InlineData(BuildConfiguration.ReleaseDebugPlus)]
    public void The_scratch_cases_run_and_draw_what_their_source_names(BuildConfiguration build)
    {
        Assert.Equal(string.Join("\n", ScratchNotes), string.Join("\n", ScratchOutcomes(build)));
    }

    // Every value the weave records is the object its name stands for in the source: never the test instance or the
    // state machine's own box, a primary constructor's parameter its own argument, and a field named bare only where the
    // source names it bare (a field read off another object, as in `other._id`, is not also read off `this`).
    [Theory]
    [MemberData(nameof(Cases))]
    public void The_values_are_the_objects_the_source_names(string id, BuildConfiguration build, string entry)
    {
        var shape = Shapes()[id];
        var woven = Build($"Captured213_{id}_{build}_{entry}_Values", shape.Source, build);
        WovenIl.Weave(new AssertionWeaver(), woven);
        RedirectToRecorder(woven);
        CapturedValueRecorder.Clear();

        var (_, _, registry, entryTask) = RunEntry(woven, shape.TypeName, entry);

        var wrong = new List<string>();
        foreach (var (expression, names, values) in CapturedValueRecorder.Snapshot())
        {
            // A name inside a string literal is not one the code reads.
            var code = Regex.Replace(expression, @"""(?:[^""\\]|\\.)*""", @"""""");
            for (var i = 0; i < names.Length; i++)
            {
                var name = names[i];
                var value = Identify(i < values.Length ? values[i] : null, registry, entryTask);
                var argument = $"Harness({name}) [the `{name}` argument]";
                if (value.StartsWith("the test instance", StringComparison.Ordinal) ||
                    value.StartsWith("the entry's own Task", StringComparison.Ordinal) ||
                    (registry.Any(r => r.Name == argument) && value != argument) ||
                    (!name.Contains('.') && !Regex.IsMatch(code, $@"(?<![\w.]){Regex.Escape(name)}\b")))
                    wrong.Add($"{id} {build} {entry} [{expression}] {name} = {value}");
            }
        }

        Assert.Empty(wrong);
    }

    // The values the weave must record, each the object its name stands for: a primary constructor's parameter as its
    // own argument, and P19's value types as their own number. A value left out fails here, where the property above
    // cannot see it.
    private static readonly (string Id, BuildConfiguration Build, string Entry, string Value)[] Recorded =
    [
        ("P1", BuildConfiguration.Debug, "AssertionPasses", "third = Harness(third) [the `third` argument]"),
        ("P1", BuildConfiguration.Release, "AssertionPasses", "third = Harness(third) [the `third` argument]"),
        ("P1", BuildConfiguration.ReleaseDebugPlus, "AssertionPasses", "third = Harness(third) [the `third` argument]"),
        ("P10", BuildConfiguration.Debug, "AssertionPasses", "third = Harness(third) [the `third` argument]"),
        ("P10", BuildConfiguration.Release, "AssertionPasses", "third = Harness(third) [the `third` argument]"),
        ("P10", BuildConfiguration.ReleaseDebugPlus, "AssertionPasses", "third = Harness(third) [the `third` argument]"),
        ("P12_1p", BuildConfiguration.Debug, "AssertionPasses", "p0 = Harness(p0) [the `p0` argument]"),
        ("P12_1p", BuildConfiguration.Release, "AssertionPasses", "p0 = Harness(p0) [the `p0` argument]"),
        ("P12_1p", BuildConfiguration.ReleaseDebugPlus, "AssertionPasses", "p0 = Harness(p0) [the `p0` argument]"),
        ("P12_1p_fields", BuildConfiguration.Debug, "AssertionPasses", "p0 = Harness(p0) [the `p0` argument]"),
        ("P12_1p_fields", BuildConfiguration.Release, "AssertionPasses", "p0 = Harness(p0) [the `p0` argument]"),
        ("P12_1p_fields", BuildConfiguration.ReleaseDebugPlus, "AssertionPasses", "p0 = Harness(p0) [the `p0` argument]"),
        ("P12_2p", BuildConfiguration.Debug, "AssertionPasses", "p1 = Harness(p1) [the `p1` argument]"),
        ("P12_2p", BuildConfiguration.Release, "AssertionPasses", "p1 = Harness(p1) [the `p1` argument]"),
        ("P12_2p", BuildConfiguration.ReleaseDebugPlus, "AssertionPasses", "p1 = Harness(p1) [the `p1` argument]"),
        ("P12_2p_fields", BuildConfiguration.Debug, "AssertionPasses", "p1 = Harness(p1) [the `p1` argument]"),
        ("P12_2p_fields", BuildConfiguration.Release, "AssertionPasses", "p1 = Harness(p1) [the `p1` argument]"),
        ("P12_2p_fields", BuildConfiguration.ReleaseDebugPlus, "AssertionPasses", "p1 = Harness(p1) [the `p1` argument]"),
        ("P12_4p", BuildConfiguration.Debug, "AssertionPasses", "p3 = Harness(p3) [the `p3` argument]"),
        ("P12_4p", BuildConfiguration.Release, "AssertionPasses", "p3 = Harness(p3) [the `p3` argument]"),
        ("P12_4p", BuildConfiguration.ReleaseDebugPlus, "AssertionPasses", "p3 = Harness(p3) [the `p3` argument]"),
        ("P12_4p_fields", BuildConfiguration.Debug, "AssertionPasses", "p3 = Harness(p3) [the `p3` argument]"),
        ("P12_4p_fields", BuildConfiguration.Release, "AssertionPasses", "p3 = Harness(p3) [the `p3` argument]"),
        ("P12_4p_fields", BuildConfiguration.ReleaseDebugPlus, "AssertionPasses", "p3 = Harness(p3) [the `p3` argument]"),
        ("P12_6p", BuildConfiguration.Debug, "AssertionPasses", "p5 = Harness(p5) [the `p5` argument]"),
        ("P12_6p", BuildConfiguration.Release, "AssertionPasses", "p5 = Harness(p5) [the `p5` argument]"),
        ("P12_6p", BuildConfiguration.ReleaseDebugPlus, "AssertionPasses", "p5 = Harness(p5) [the `p5` argument]"),
        ("P12_6p_fields", BuildConfiguration.Debug, "AssertionPasses", "p5 = Harness(p5) [the `p5` argument]"),
        ("P12_6p_fields", BuildConfiguration.Release, "AssertionPasses", "p5 = Harness(p5) [the `p5` argument]"),
        ("P12_6p_fields", BuildConfiguration.ReleaseDebugPlus, "AssertionPasses", "p5 = Harness(p5) [the `p5` argument]"),
        ("P18", BuildConfiguration.Debug, "Parameter", "expected = Harness(expected) [the `expected` argument]"),
        ("P18", BuildConfiguration.Release, "Parameter", "expected = Harness(expected) [the `expected` argument]"),
        ("P18", BuildConfiguration.ReleaseDebugPlus, "Parameter", "expected = Harness(expected) [the `expected` argument]"),
        ("P19", BuildConfiguration.Debug, "Int", "count = int 7"),
        ("P19", BuildConfiguration.Debug, "Long", "total = long 7"),
        ("P19", BuildConfiguration.Release, "Int", "count = int 7"),
        ("P19", BuildConfiguration.Release, "Long", "total = long 7"),
        ("P19", BuildConfiguration.ReleaseDebugPlus, "Int", "count = int 7"),
        ("P19", BuildConfiguration.ReleaseDebugPlus, "Long", "total = long 7"),
        ("P2", BuildConfiguration.Debug, "AssertionPasses", "handlers = Harness(handlers) [the `handlers` argument]"),
        ("P2", BuildConfiguration.Release, "AssertionPasses", "handlers = Harness(handlers) [the `handlers` argument]"),
        ("P2", BuildConfiguration.ReleaseDebugPlus, "AssertionPasses", "handlers = Harness(handlers) [the `handlers` argument]"),
        ("P22b", BuildConfiguration.Debug, "AssertionPasses", "third = Harness(third) [the `third` argument]"),
        ("P22b", BuildConfiguration.Release, "AssertionPasses", "third = Harness(third) [the `third` argument]"),
        ("P22b", BuildConfiguration.ReleaseDebugPlus, "AssertionPasses", "third = Harness(third) [the `third` argument]"),
        ("P23", BuildConfiguration.Debug, "InAClosure", "third = Harness(third) [the `third` argument]"),
        ("P23", BuildConfiguration.Release, "InAClosure", "third = Harness(third) [the `third` argument]"),
        ("P23", BuildConfiguration.ReleaseDebugPlus, "InAClosure", "third = Harness(third) [the `third` argument]"),
        ("P3", BuildConfiguration.Debug, "AssertionPasses", "third = Harness(third) [the `third` argument]"),
        ("P3", BuildConfiguration.Release, "AssertionPasses", "third = Harness(third) [the `third` argument]"),
        ("P3", BuildConfiguration.ReleaseDebugPlus, "AssertionPasses", "third = Harness(third) [the `third` argument]"),
        ("P5", BuildConfiguration.Debug, "AssertionPasses", "third = Harness(third) [the `third` argument]"),
        ("P5", BuildConfiguration.Release, "AssertionPasses", "third = Harness(third) [the `third` argument]"),
        ("P5", BuildConfiguration.ReleaseDebugPlus, "AssertionPasses", "third = Harness(third) [the `third` argument]"),
        ("P6", BuildConfiguration.Debug, "AssertionPasses", "only = Harness(only) [the `only` argument]"),
        ("P6", BuildConfiguration.Release, "AssertionPasses", "only = Harness(only) [the `only` argument]"),
        ("P6", BuildConfiguration.ReleaseDebugPlus, "AssertionPasses", "only = Harness(only) [the `only` argument]"),
        ("P8", BuildConfiguration.Debug, "SyncAssert", "expected = Harness(expected) [the `expected` argument]"),
        ("P8", BuildConfiguration.Debug, "AsyncAssert", "expected = Harness(expected) [the `expected` argument]"),
        ("P8", BuildConfiguration.Release, "SyncAssert", "expected = Harness(expected) [the `expected` argument]"),
        ("P8", BuildConfiguration.Release, "AsyncAssert", "expected = Harness(expected) [the `expected` argument]"),
        ("P8", BuildConfiguration.ReleaseDebugPlus, "SyncAssert", "expected = Harness(expected) [the `expected` argument]"),
        ("P8", BuildConfiguration.ReleaseDebugPlus, "AsyncAssert", "expected = Harness(expected) [the `expected` argument]"),
    ];

    public static TheoryData<string, BuildConfiguration, string> RecordedCases()
    {
        var data = new TheoryData<string, BuildConfiguration, string>();
        foreach (var (id, build, entry) in Recorded.Select(r => (r.Id, r.Build, r.Entry)).Distinct())
            data.Add(id, build, entry);
        return data;
    }

    [Theory]
    [MemberData(nameof(RecordedCases))]
    public void A_primary_constructors_parameter_is_recorded_as_its_own_argument(string id, BuildConfiguration build, string entry)
    {
        var shape = Shapes()[id];
        var woven = Build($"Captured213_{id}_{build}_{entry}_Recorded", shape.Source, build);
        WovenIl.Weave(new AssertionWeaver(), woven);
        RedirectToRecorder(woven);
        CapturedValueRecorder.Clear();

        var (_, _, registry, entryTask) = RunEntry(woven, shape.TypeName, entry);

        var recorded = CapturedValueRecorder.Snapshot()
            .SelectMany(c => c.Names.Select((n, i) => $"{n} = {Identify(i < c.Values.Length ? c.Values[i] : null, registry, entryTask)}"))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var (_, _, _, value) in Recorded.Where(r => r.Id == id && r.Build == build && r.Entry == entry))
            Assert.Contains(value, recorded);
    }

    // The check where the values are loaded: with every arm right it drops nothing, so these hand it what a wrong arm
    // would, a field read off an object that does not hold it.
    private const string LoadCheckSource = """
        public class Holder
        {
            public int Own = 1;
            public int Instance() => Own;
            public static int Static() => 0;
            public int WithLocal() { var e = new Elsewhere(); return e.Other + Own; }
        }
        public class Elsewhere { public int Other = 2; }
        """;

    private static AssertionStatement Holding(CapturedVariable variable)
    {
        var assertion = new AssertionStatement();
        assertion.CapturedVariables.Add(variable);
        assertion.CarriedAfter[variable] = Instruction.Create(OpCodes.Nop);
        return assertion;
    }

    private static ModuleDefinition LoadCheckModule(string name) =>
        ModuleDefinition.ReadModule(Build($"Captured213_LoadCheck_{name}", LoadCheckSource, BuildConfiguration.Debug));

    [Fact]
    public void The_load_check_keeps_a_field_of_the_methods_own_instance()
    {
        using var module = LoadCheckModule("Keeps");
        var holder = module.GetType("Holder");
        var variable = new CapturedVariable { Name = "Own", StateField = holder.Fields.Single(f => f.Name == "Own"), Type = module.TypeSystem.Int32 };
        var assertion = Holding(variable);

        AssertionWeaver.DropWhatCannotBeLoaded(holder.Methods.Single(m => m.Name == "Instance"), assertion);

        Assert.Equal([variable], assertion.CapturedVariables);
        Assert.True(assertion.CarriedAfter.ContainsKey(variable));
    }

    [Fact]
    public void The_load_check_drops_a_field_read_off_a_static_methods_first_argument()
    {
        using var module = LoadCheckModule("Static");
        var holder = module.GetType("Holder");
        var variable = new CapturedVariable { Name = "Own", StateField = holder.Fields.Single(f => f.Name == "Own"), Type = module.TypeSystem.Int32 };
        var assertion = Holding(variable);

        AssertionWeaver.DropWhatCannotBeLoaded(holder.Methods.Single(m => m.Name == "Static"), assertion);

        Assert.Empty(assertion.CapturedVariables);
    }

    [Fact]
    public void The_load_check_drops_another_types_field_and_does_not_carry_it()
    {
        using var module = LoadCheckModule("Another");
        var holder = module.GetType("Holder");
        var other = module.GetType("Elsewhere").Fields.Single(f => f.Name == "Other");
        var variable = new CapturedVariable { Name = "Other", StateField = other, Type = module.TypeSystem.Int32 };
        var assertion = Holding(variable);

        AssertionWeaver.DropWhatCannotBeLoaded(holder.Methods.Single(m => m.Name == "Instance"), assertion);

        Assert.Empty(assertion.CapturedVariables);
        Assert.Empty(assertion.CarriedAfter);
    }

    [Fact]
    public void The_load_check_reads_a_closure_field_only_off_a_local_of_its_type()
    {
        using var module = LoadCheckModule("Closure");
        var holder = module.GetType("Holder");
        var method = holder.Methods.Single(m => m.Name == "WithLocal");
        var local = method.Body.Variables.Single(v => v.VariableType.Name == "Elsewhere").Index;
        var right = new CapturedVariable
        {
            Name = "Other", ClosureLocalIndex = local, ClosureField = module.GetType("Elsewhere").Fields.Single(f => f.Name == "Other"),
            Type = module.TypeSystem.Int32,
        };
        var wrong = new CapturedVariable
        {
            Name = "Own", ClosureLocalIndex = local, ClosureField = holder.Fields.Single(f => f.Name == "Own"), Type = module.TypeSystem.Int32,
        };
        var assertion = Holding(right);
        assertion.CapturedVariables.Add(wrong);

        AssertionWeaver.DropWhatCannotBeLoaded(method, assertion);

        Assert.Equal([right], assertion.CapturedVariables);
    }

    private static void RedirectToRecorder(string path)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(AppContext.BaseDirectory);
        resolver.AddSearchDirectory(Path.GetDirectoryName(path)!);
        var recorder = typeof(CapturedValueRecorder);
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

    // A recorded value named by what it IS among the objects this run created (the test instance, each Harness and C by
    // its tag, the entry's own task, which for an async method is its state-machine box), or by its value for a number
    // or a string. Anything else is not read further: a value read off the wrong object need not be an object.
    private static string Identify(object? value, List<(string Name, object Obj)> registry, object? entryTask)
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
        return "UNCREATED (not an object this run made)";
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
/// The woven calls to Track's value-taking methods, redirected here by <see cref="CapturedFieldValueTests"/> with the same
/// signatures. Each keeps a reference to the values array the weave built (no copy, so nothing is read from a value that
/// may not be an object) and forwards to Track, so the note is drawn exactly as it would be.
/// </summary>
public static class CapturedValueRecorder
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
