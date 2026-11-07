using System.Reflection;
using System.Text;
using Kronikol.AssertionTracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.AssertionTracking;

// Scratch check of the #213 prototype's shapes; not part of the patch.
public class Proto213ScratchTests
{
    private const string Source = """
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

    // PROTO213_OUT names the report (probe/run-all.sh sets it to results/scratch-runs-<label>.txt); else the temp folder.
    private static readonly string Out = Environment.GetEnvironmentVariable("PROTO213_OUT") ?? Path.Combine(
        Path.GetTempPath(), "proto213-scratch-" + (Environment.GetEnvironmentVariable("PROTO213_LABEL") ?? "run") + ".txt");

    private static readonly object Gate = new();

    // The weaver's entry, by reflection, as the probe calls it, so WovenIlVerifiesTests' scan for a weave that ILVerify
    // never reads does not flag this file while it sits in the test project.
    private static readonly MethodInfo WeaveMethod =
        typeof(AssertionWeaver).GetMethods().First(m => m.Name == "Weave" && m.GetParameters().Length == 2);

    public static TheoryData<string> Builds() => new() { "Debug", "Release", "ReleaseDebugPlus" };

    [Theory]
    [MemberData(nameof(Builds))]
    public void Shapes(string build)
    {
        var configuration = Enum.Parse<TestAssemblyBuilder.BuildConfiguration>(build);
        var path = TestAssemblyBuilder.Build($"Proto213_{build}", Source, configuration);
        var unwoven = File.ReadAllBytes(path);
        var result = (WeaveResult)WeaveMethod.Invoke(new AssertionWeaver(), new object?[] { path, Path.ChangeExtension(path, ".pdb") })!;
        var errors = WovenIl.NewErrors(unwoven, File.ReadAllBytes(path), path);

        var file = Out.Replace(".txt", "-" + build + ".txt");
        File.Delete(file);
        var report = new StringBuilder();
        void Flush() { File.AppendAllText(file, report.ToString()); report.Clear(); }
        report.AppendLine($"== {build}: woven {result.WeavedCount}, ILVerify errors added {errors.Count}");
        foreach (var e in errors)
            report.AppendLine("   IL: " + e);
        foreach (var u in result.Unwoven)
            report.AppendLine("   unwoven: " + u);

        var assembly = Assembly.LoadFrom(path);
        var harness = assembly.GetType("Harness")!;
        object MakeHarness(string name) => Activator.CreateInstance(harness, name)!;
        object NewPcReal() => Activator.CreateInstance(assembly.GetType("Pc")!, MakeHarness("first"), MakeHarness("second"), MakeHarness("third"))!;

        var cases = new (string Type, string Method, Func<object?> Instance, object?[]? Args)[]
        {
            ("Pc", "Async_lambda_this_only", NewPcReal, null),
            ("Pc", "Async_lambda_with_local", NewPcReal, null),
            ("Pc", "Async_statement_names_parameter", NewPcReal, null),
            ("Pc", "Sync_lambda_and_statement", NewPcReal, null),
            ("Pc", "Sync_expression_tree", NewPcReal, null),
            ("Pc", "Async_expression_tree", NewPcReal, null),
            ("Receivers", "Lambda_reads_another_objects_field", () => Activator.CreateInstance(assembly.GetType("Receivers")!), null),
            ("Receivers", "Async_another_objects_field", () => Activator.CreateInstance(assembly.GetType("Receivers")!), null),
            ("Receivers", "Static_parameters_field", () => null, [Activator.CreateInstance(assembly.GetType("Other")!)]),
            ("Receivers", "Expression_tree_static_field", () => Activator.CreateInstance(assembly.GetType("Receivers")!), null),
            ("Derived", "Sync_generic_base_field", () => Activator.CreateInstance(assembly.GetType("Derived")!), null),
            ("Derived", "Async_generic_base_field", () => Activator.CreateInstance(assembly.GetType("Derived")!), null),
            ("Derived", "Sync_lambda_generic_base_field", () => Activator.CreateInstance(assembly.GetType("Derived")!), null),
            ("Statics", "Other_class_static", () => Activator.CreateInstance(assembly.GetType("Statics")!), null),
            ("Statics", "Async_own_static", () => Activator.CreateInstance(assembly.GetType("Statics")!), null),
            ("Gen`1", "Async_generic_lambda", () => Activator.CreateInstance(assembly.GetType("Gen`1")!.MakeGenericType(typeof(string)), "v"), null),
            ("Gen`1", "Sync_generic_expression", () => Activator.CreateInstance(assembly.GetType("Gen`1")!.MakeGenericType(typeof(string)), "v"), null),
            ("InLambdas", "Assertion_inside_lambda", () => Activator.CreateInstance(assembly.GetType("InLambdas")!), null),
            ("InLambdas", "Nested_lambda_inside_lambda", () => Activator.CreateInstance(assembly.GetType("InLambdas")!), null),
        };

        var skip = (Environment.GetEnvironmentVariable("PROTO213_SKIP") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var (typeName, methodName, instance, args) in cases)
        {
            if (skip.Contains(typeName))
                continue;
            report.AppendLine($"-- {typeName}.{methodName}: starting");
            Flush();
            var target = instance();
            var method = (target?.GetType() ?? assembly.GetType(typeName)!).GetMethod(methodName)!;
            var testId = $"{methodName}_{Guid.NewGuid():N}";
            Exception? thrown;
            using (TestIdentityScope.Begin(testId, testId))
            {
                thrown = Record.Exception(() =>
                {
                    if (method.Invoke(target, args) is Task task)
                        task.GetAwaiter().GetResult();
                });
            }
            if (thrown is TargetInvocationException { InnerException: { } inner })
                thrown = inner;
            report.AppendLine($"-- {typeName}.{methodName}: {(thrown == null ? "ran" : "THREW " + thrown.GetType().Name + ": " + thrown.Message.Split('\n')[0])}");
            foreach (var note in WovenRun.NotesFor(testId))
                report.AppendLine($"     {(note.Passed ? "pass" : "FAIL")} | {note.Label}");
            Flush();
        }
    }
}
