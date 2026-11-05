using System.Reflection;
using Kronikol.AssertionTracking;
using static Kronikol.Tests.AssertionTracking.TestAssemblyBuilder;

namespace Kronikol.Tests.AssertionTracking;

/// <summary>
/// The weave reads an assertion's values for its note, and reading them must not change what the program does. On 5.28.x a by-ref or a pointer
/// in the values array killed the test process, a tuple's item or another object's field was read off the test class, a field of a generic base
/// was boxed as a type only its own class can name, a span could not be boxed at all, and a volatile field was stored unboxed. A passing test
/// threw, or its process died. Each shape here is built twice, and what the plain build does is the answer; <see cref="WovenIl.Weave"/> verifies
/// the IL first, so a shape that would kill the process fails a fact and does not end the run.
/// </summary>
public class ValueCaptureLeavesTheProgramAloneTests
{
    private const string Fixture = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading.Tasks;
        using Kronikol.Tracking;
        using Shouldly;

        [assembly: TrackAssertions]

        public class Other { public long pad1, pad2, pad3, pad4, pad5, pad6, pad7, pad8; public string k = "x"; public int n = 1; }
        public static class Holder { public static Other O = new Other(); }
        public class Base<T> { public T V; }

        public class Shape : Base<int>
        {
            public int Z;
            //MEMBERS
            //CONTAINERS
        }
        """;

    private static readonly (string Name, string Members, string Body)[] Shapes =
    [
        ("ARefLocal", "", """var a = new string[2]; ref string r = ref a[0]; r = "x"; "x".ShouldBe(r); return "passed";"""),
        ("ARefInt", "", """int v = 1; ref int r = ref v; 1.ShouldBe(r); return "passed";"""),
        ("ARefIntThatFails", "", """int v = 1; ref int r = ref v; 2.ShouldBe(r); return "passed";"""),
        ("ARefIntoAnArray", "", """var a = new[] { 2 }; ref int r = ref a[0]; 2.ShouldBe(r); return "passed";"""),
        ("AForeachRef", "", """Span<int> items = new int[] { 2, 4 }; var c = 0; foreach (ref int r in items) { 0.ShouldBeLessThan(r); c++; } return c.ToString();"""),
        ("APointer", "", """unsafe { var v = 1; int* p = &v; 1.ShouldBe(*p); } return "passed";"""),
        ("AFunctionPointer", "public static int Nine() => 9;", """unsafe { delegate*<int> f = &Nine; 9.ShouldBe(f()); } return "passed";"""),

        ("ATupleItem", "", """var p = ("x", 1); "x".ShouldBe(p.Item1); return "passed";"""),
        ("ATupleItemThatFails", "", """var p = ("x", 1); "y".ShouldBe(p.Item1); return "passed";"""),
        ("AnIntItem", "", """var p = ("x", 1); 1.ShouldBe(p.Item2); return "passed";"""),
        ("ANestedTuple", "", """var p = (("x", 2), 1); "x".ShouldBe(p.Item1.Item1); return "passed";"""),
        ("ATupleAsTheSubject", "", """var p = ("x", 1); p.Item1.ShouldBe("x"); return "passed";"""),
        ("ATupleAroundTheAssertion", "", """var t = (s.ShouldNotBeNull(), 1).Item1; return t;"""),
        ("ATupleInAnIterator", """public IEnumerable<string> Items() { var p = ("x", 1); "x".ShouldBe(p.Item1); yield return "passed"; }""", """return Items().First();"""),

        ("AnotherObjectsField", "", """var q = new Other(); "x".ShouldBe(q.k); return "passed";"""),
        ("AnotherObjectsFieldThatFails", "", """var q = new Other(); "y".ShouldBe(q.k); return "passed";"""),
        ("AnotherObjectsInt", "", """var q = new Other(); 1.ShouldBe(q.n); return "passed";"""),
        ("ANewObjectsField", "", """var q = 0; "x".ShouldBe(new Other().k); return "passed";"""),
        ("AStaticHoldersField", "", """var q = 0; "x".ShouldBe(Holder.O.k); return "passed";"""),
        ("ACastObjectsField", "", """object o = new Other(); "x".ShouldBe(((Other)o).k); return "passed";"""),
        ("AnArrayElementsField", "", """var q = new[] { new Other() }; "x".ShouldBe(q[0].k); return "passed";"""),
        ("ANullConditionalsField", "", """var q = new Other(); "x".ShouldBe(q?.k); return "passed";"""),
        ("AnAwaitedObjectsField", "", """var r = await Task.FromResult(new Other()); "x".ShouldBe(r.k); return "passed";"""),
        ("TheSubjectRunsBeforeAnArgumentsField", """public string Log = ""; public int Note(int v) { Log += "subject"; return v; }""",
            """Other o = null; try { Note(3).ShouldBe(o.n); } catch (NullReferenceException) { Log += ",caught"; } return Log;"""),

        ("AGenericBasesField", "", """0.ShouldBe(V); V.ShouldBe(0); return "passed";"""),
        ("AGenericClosure", """public int Check<T>(T expected, Func<T, T> get) { Func<T> f = () => get(expected); f().ShouldBe(expected); return 1; }""",
            """return Check("a", x => x).ToString();"""),
        ("AGenericLocalFunction", """public int Local<T>(T expected) { T Get() => expected; Get().ShouldBe(expected); return 1; }""", """return Local("a").ToString();"""),

        ("ASpanArgumentToAHelper", "public static int Len(Span<int> x) => x.Length;", """Span<int> items = new int[2]; 2.ShouldBe(Len(items)); return "passed";"""),
        ("AReadOnlySpanInANew", "", """ReadOnlySpan<char> c = "abc"; "abc".ShouldBe(new string(c)); return "passed";"""),
        ("ARefStructLocal", "public ref struct Bar { public int N; }", """var r = new Bar { N = 2 }; 2.ShouldBe(r.N); return "passed";"""),
        ("ASpanIndexer", "", """Span<int> items = new int[] { 3 }; 3.ShouldBe(items[0]); return "passed";"""),

        ("AVolatileField", "public volatile int W = 1;", """1.ShouldBe(W); return "passed";"""),
    ];

    private static readonly BuildConfiguration[] Configurations =
        [BuildConfiguration.Debug, BuildConfiguration.Release, BuildConfiguration.ReleaseDebugPlus];

    public static TheoryData<string, BuildConfiguration> ShapesAndBuilds()
    {
        var data = new TheoryData<string, BuildConfiguration>();
        foreach (var shape in Shapes)
            foreach (var configuration in Configurations)
                data.Add(shape.Name, configuration);
        return data;
    }

    private static string Source(string members, string body)
    {
        var containers = body.Contains("await ", StringComparison.Ordinal) ? "" : $"public string Sync(string s) {{ {body} }}\n";
        containers += $"public async Task<string> Async(string s) {{ await Task.Yield(); Z++; {body} }}\n";
        return Fixture.Replace("//MEMBERS", members).Replace("//CONTAINERS", containers);
    }

    private static string Read(Type type, string method)
    {
        var instance = Activator.CreateInstance(type);
        try
        {
            var result = type.GetMethod(method)!.Invoke(instance, ["x"]);
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
    [MemberData(nameof(ShapesAndBuilds))]
    public void A_captured_value_changes_nothing_the_program_does(string name, BuildConfiguration configuration)
    {
        var shape = Shapes.Single(s => s.Name == name);
        var source = Source(shape.Members, shape.Body);
        var plain = Assembly.LoadFrom(Build($"Capture_{name}_{configuration}_Plain", source, configuration)).GetType("Shape")!;
        var path = Build($"Capture_{name}_{configuration}_Woven", source, configuration);

        WovenIl.Weave(new AssertionWeaver(), path);

        var woven = Assembly.LoadFrom(path).GetType("Shape")!;
        foreach (var method in plain.GetMethods().Where(m => m.Name is "Sync" or "Async").Select(m => m.Name))
            Assert.Equal($"{name} {method}: {Read(plain, method)}", $"{name} {method}: {Read(woven, method)}");
    }

    [Theory]
    [InlineData(BuildConfiguration.Debug)]
    [InlineData(BuildConfiguration.Release)]
    public void A_lazy_argument_runs_as_often_as_it_does_with_no_weave(BuildConfiguration configuration)
    {
        const string members = "public int Runs; public IEnumerable<int> Gen() { Runs++; yield return 1; }";
        const string body = """new[] { 1 }.ShouldBe(Gen()); return Runs.ToString();""";
        var source = Source(members, body);
        var plain = Assembly.LoadFrom(Build($"CaptureLazy_{configuration}_Plain", source, configuration)).GetType("Shape")!;
        var path = Build($"CaptureLazy_{configuration}_Woven", source, configuration);

        WovenIl.Weave(new AssertionWeaver(), path);

        Assert.Equal(Read(plain, "Sync"), Read(Assembly.LoadFrom(path).GetType("Shape")!, "Sync"));
    }
}
