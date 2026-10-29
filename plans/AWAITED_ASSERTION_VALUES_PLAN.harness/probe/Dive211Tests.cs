using System.Reflection;
using System.Text;
using Kronikol.AssertionTracking;
using Kronikol.Tracking;
using Microsoft.CodeAnalysis;
using Mono.Cecil;

namespace Kronikol.Tests.AssertionTracking;

// Probe for plans/AWAITED_ASSERTION_VALUES_PLAN.md's audit (not a fact to keep): every shape below, compiled as Debug,
// as Release the way xUnit v3, TUnit, MSTest and NUnit projects build it (/debug- /debug:portable /optimize+, no
// debug-plus) and as Release the way xUnit v2 projects build it (/debug+, debug-plus), woven by this tree's weaver and
// run beside an unwoven copy. Writes $KRONIKOL_211_DIVE (a file path).
public class Dive211Tests
{
    private const string Common = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Kronikol.Tracking;
        using Shouldly;
        using FluentAssertions;

        [assembly: TrackAssertions]

        namespace Dive;

        public sealed class Request { public string Id { get; set; } = ""; public override string ToString() => "Request(" + Id + ")"; }
        public sealed class Marker { private readonly string _text; public Marker(string text) { _text = text; } public override string ToString() => _text; }

        public sealed class Client
        {
            public async Task<string> CallAsync(Request request) { await Task.Yield(); throw new InvalidOperationException("expected " + request.Id); }
            public string Get(Request request) => throw new InvalidOperationException("expected " + request.Id);
            public static async Task<string> StaticCallAsync(Request request) { await Task.Yield(); throw new InvalidOperationException("expected " + request.Id); }
        }

        public class Sync
        {
            private readonly Client _client = new();
            private readonly Marker _marker = new("WRONG OBJECT");

            public void Throw_capturing_a_local()
            {
                var request = new Request { Id = "a1" };
                var e = Should.Throw<InvalidOperationException>(() => _client.Get(request));
                e.Message.ShouldBe("expected a1");
            }

            public void ShouldBe_argument_captured_by_nothing()
            {
                var expected = "a1";
                "a1".ShouldBe(expected);
            }

            public void ShouldContain_predicate()
            {
                var expected = "a1";
                var items = new List<string> { "a1", "b2" };
                items.ShouldContain(x => x == expected);
            }

            public void Fa_Contain_predicate()
            {
                var expected = "a1";
                var items = new List<string> { "a1", "b2" };
                items.Should().Contain(x => x == expected);
            }

            public void ThrowAsync_GetResult()
            {
                var request = new Request { Id = "a1" };
                Should.ThrowAsync<InvalidOperationException>(() => _client.CallAsync(request)).GetAwaiter().GetResult();
            }

            public void Fa_ThrowAsync_GetResult_WithMessage_local()
            {
                var message = "expected a1";
                Func<Task> act = () => _client.CallAsync(new Request { Id = "a1" });
                act.Should().ThrowAsync<InvalidOperationException>().WithMessage(message).GetAwaiter().GetResult();
            }

            public void Fa_Invoking_capturing_a_local()
            {
                var request = new Request { Id = "a1" };
                _client.Invoking(c => c.Get(request)).Should().Throw<InvalidOperationException>();
            }

            public void Sibling_blocks_used_once()
            {
                { var first = "one"; "one".ShouldBe(first); }
                { var second = "two"; "two".ShouldBe(second); }
            }

            public void Sibling_blocks_used_twice()
            {
                { var first = "one"; "one".ShouldBe(first); GC.KeepAlive(first); }
                { var second = "two"; "two".ShouldBe(second); GC.KeepAlive(second); }
            }
        }

        public class Async
        {
            private readonly Client _client = new();

            public async Task Issue_assigned()
            {
                var request = new Request { Id = "a1" };
                var exception = await Should.ThrowAsync<InvalidOperationException>(async () => await _client.CallAsync(request));
                exception.Message.ShouldBe("expected a1");
            }

            public async Task Issue_unassigned()
            {
                var request = new Request { Id = "a1" };
                await Should.ThrowAsync<InvalidOperationException>(() => _client.CallAsync(request));
            }

            public async Task Using_block()
            {
                using (var gate = new System.IO.MemoryStream())
                {
                    var request = new Request { Id = "a1" };
                    await Should.ThrowAsync<InvalidOperationException>(() => _client.CallAsync(request));
                }
            }

            public async Task Try_finally()
            {
                var request = new Request { Id = "a1" };
                try
                {
                    await Should.ThrowAsync<InvalidOperationException>(() => _client.CallAsync(request));
                }
                finally
                {
                    request.Id.ShouldBe("a1");
                }
            }

            public async Task Try_catch_block()
            {
                try
                {
                    var request = new Request { Id = "a1" };
                    await Should.ThrowAsync<InvalidOperationException>(() => _client.CallAsync(request));
                }
                catch (ArgumentException)
                {
                }
            }

            public async Task Foreach_after_await()
            {
                await Task.Yield();
                foreach (var id in new List<string> { "a1" })
                {
                    var request = new Request { Id = id };
                    await Should.ThrowAsync<InvalidOperationException>(() => _client.CallAsync(request));
                }
            }

            public async Task ConfigureAwait_false()
            {
                var request = new Request { Id = "a1" };
                await Should.ThrowAsync<InvalidOperationException>(() => _client.CallAsync(request)).ConfigureAwait(false);
            }

            public async Task Two_statements_one_closure()
            {
                var request = new Request { Id = "a1" };
                await Should.ThrowAsync<InvalidOperationException>(() => _client.CallAsync(request));
                await Should.ThrowAsync<InvalidOperationException>(() => _client.CallAsync(request));
            }

            public async ValueTask ValueTask_method()
            {
                var request = new Request { Id = "a1" };
                await Should.ThrowAsync<InvalidOperationException>(() => _client.CallAsync(request));
            }

            public async Task Async_lambda()
            {
                Func<Task> check = async () =>
                {
                    var request = new Request { Id = "a1" };
                    await Should.ThrowAsync<InvalidOperationException>(() => _client.CallAsync(request));
                };
                await check();
            }

            public async Task Local_function()
            {
                await Check(new Request { Id = "a1" });

                async Task Check(Request request) =>
                    await Should.ThrowAsync<InvalidOperationException>(() => _client.CallAsync(request));
            }

            public async Task Fa_Awaiting_capturing_a_local()
            {
                var request = new Request { Id = "a1" };
                await _client.Awaiting(c => c.CallAsync(request)).Should().ThrowAsync<InvalidOperationException>();
            }

            public async Task Fa_WithMessage_local()
            {
                var message = "expected a1";
                Func<Task> act = () => _client.CallAsync(new Request { Id = "a1" });
                await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(message);
            }

            public async Task Fa_WithMessage_closure()
            {
                var request = new Request { Id = "a1" };
                Func<Task> act = () => _client.CallAsync(request);
                await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("expected " + request.Id);
            }
        }

        public class NoThis
        {
            public async Task Static_callee()
            {
                var request = new Request { Id = "a1" };
                await Should.ThrowAsync<InvalidOperationException>(() => Client.StaticCallAsync(request));
            }

            public void Sync_static_callee()
            {
                var request = new Request { Id = "a1" };
                Should.Throw<InvalidOperationException>(() => new Client().Get(request));
            }
        }

        public abstract class Base<T>
        {
            protected readonly Client _client = new();

            public async Task In_generic_base()
            {
                var request = new Request { Id = "a1" };
                await Should.ThrowAsync<InvalidOperationException>(() => _client.CallAsync(request));
            }

            public void Sync_in_generic_base()
            {
                var request = new Request { Id = "a1" };
                Should.Throw<InvalidOperationException>(() => _client.Get(request));
            }
        }

        public class Derived : Base<int> { }
        """;

    private const string TUnitSource = """
        using System;
        using System.Threading.Tasks;
        using Kronikol.Tracking;
        using TUnit.Assertions;
        using TUnit.Assertions.Extensions;

        [assembly: TrackAssertions]

        namespace DiveTUnit;

        public sealed class Request { public string Id { get; set; } = ""; }

        public sealed class Client
        {
            public async Task<string> CallAsync(Request request) { await Task.Yield(); throw new InvalidOperationException("expected " + request.Id); }
        }

        public class Shapes
        {
            private readonly Client _client = new();

            public async Task IsEqualTo_local()
            {
                var expected = "a1";
                await Assert.That("a1").IsEqualTo(expected);
            }

            public async Task Throws_capturing_delegate()
            {
                var request = new Request { Id = "a1" };
                await Assert.That(() => _client.CallAsync(request)).Throws<InvalidOperationException>();
            }

            public static async Task<string> DelayedAsync(string text) { await Task.Yield(); return text; }

            public async Task IsEqualTo_local_after_suspension()
            {
                var expected = "a1";
                await Assert.That(async () => await DelayedAsync("a1")).IsEqualTo(expected);
            }

            public async Task IsEqualTo_closure_after_suspension()
            {
                var request = new Request { Id = "a1" };
                await Assert.That(async () => await DelayedAsync(request.Id)).IsEqualTo(request.Id);
            }
        }
        """;

    private const string CrashSource = """
        using System;
        using Kronikol.Tracking;
        using Shouldly;

        [assembly: TrackAssertions]

        namespace DiveCrash;

        public sealed class Request { public string Id { get; set; } = ""; }
        public sealed class Client { public string Get(Request request) => throw new InvalidOperationException("expected " + request.Id); }

        public class ValueField
        {
            private readonly long _number = 0x0000_1234_5678_9ABC;

            public long Number => _number;

            public void Throw_capturing_a_local()
            {
                var request = new Request { Id = "a1" };
                Should.Throw<InvalidOperationException>(() => new Client().Get(request));
            }
        }
        """;

    [Fact]
    public void EnvCheck()
    {
        var output = Environment.GetEnvironmentVariable("KRONIKOL_211_ENVCHECK");
        if (string.IsNullOrEmpty(output))
            return;
        var optimization = Environment.GetEnvironmentVariable("KRONIKOL_211_ENVCHECK_DEBUG") == "1" ? OptimizationLevel.Debug : OptimizationLevel.Release;
        var path = TestAssemblyBuilder.Build($"Dive211_EnvCheck_{Guid.NewGuid():N}", Common, optimization);
        using var module = ModuleDefinition.ReadModule(path);
        var sb = new StringBuilder();
        sb.AppendLine($"{optimization}, KRONIKOL_DEBUGPLUS={Environment.GetEnvironmentVariable("KRONIKOL_DEBUGPLUS")}, KRONIKOL_DEBUGPLUS_DEBUG={Environment.GetEnvironmentVariable("KRONIKOL_DEBUGPLUS_DEBUG")}");
        foreach (var type in module.GetTypes().OrderBy(t => t.FullName))
            foreach (var method in type.Methods.Where(m => m.HasBody))
                sb.AppendLine($"{type.FullName}::{method.Name} [{string.Join(", ", method.Body.Variables.Select(v => v.VariableType.Name))}] {method.Body.Instructions.Count} instructions");
        File.WriteAllText(output, sb.ToString());
    }

    [Fact]
    public void Crash()
    {
        var mode = Environment.GetEnvironmentVariable("KRONIKOL_211_DIVE_CRASH");
        if (string.IsNullOrEmpty(mode))
            return;
        TestAssemblyBuilder.DebugPlusRelease = mode == "ReleaseDebugPlus";
        string path;
        try
        {
            var source = mode == "ReleaseZero" ? CrashSource.Replace("0x0000_1234_5678_9ABC", "0") : CrashSource;
            path = TestAssemblyBuilder.Build($"Dive211_Crash_{mode}", source,
                mode == "Debug" ? OptimizationLevel.Debug : OptimizationLevel.Release);
        }
        finally
        {
            TestAssemblyBuilder.DebugPlusRelease = false;
        }
        var weave = mode == "ReleaseUnwoven" ? new WeaveResult() : new AssertionWeaver().Weave(path, Path.ChangeExtension(path, ".pdb"));
        Console.WriteLine($"Dive211 crash probe [{mode}]: WeavedCount={weave.WeavedCount}; running");
        var (thrown, notes) = Run(path, "DiveCrash.ValueField", "Throw_capturing_a_local");
        Console.WriteLine($"Dive211 crash probe [{mode}]: threw {Describe(thrown)}; notes {string.Join(" / ", notes.Select(n => n.Label))}");
        Assert.Fail($"[{mode}] the run came back: threw {Describe(thrown)}; {notes.Length} notes");
    }

    private static readonly (string Type, string Method)[] CommonEntries =
    [
        ("Dive.Sync", "Throw_capturing_a_local"), ("Dive.Sync", "ShouldBe_argument_captured_by_nothing"),
        ("Dive.Sync", "ShouldContain_predicate"), ("Dive.Sync", "Fa_Contain_predicate"),
        ("Dive.Sync", "ThrowAsync_GetResult"), ("Dive.Sync", "Fa_ThrowAsync_GetResult_WithMessage_local"),
        ("Dive.Sync", "Fa_Invoking_capturing_a_local"), ("Dive.Async", "Fa_Awaiting_capturing_a_local"),
        ("Dive.Sync", "Sibling_blocks_used_once"), ("Dive.Sync", "Sibling_blocks_used_twice"),
        ("Dive.Async", "Issue_assigned"), ("Dive.Async", "Issue_unassigned"), ("Dive.Async", "Using_block"),
        ("Dive.Async", "Try_finally"), ("Dive.Async", "Try_catch_block"), ("Dive.Async", "Foreach_after_await"),
        ("Dive.Async", "ConfigureAwait_false"), ("Dive.Async", "Two_statements_one_closure"),
        ("Dive.Async", "ValueTask_method"), ("Dive.Async", "Async_lambda"), ("Dive.Async", "Local_function"),
        ("Dive.Async", "Fa_WithMessage_local"), ("Dive.Async", "Fa_WithMessage_closure"),
        ("Dive.NoThis", "Static_callee"), ("Dive.NoThis", "Sync_static_callee"),
        ("Dive.Derived", "In_generic_base"), ("Dive.Derived", "Sync_in_generic_base"),
    ];

    private static readonly (string Type, string Method)[] TUnitEntries =
        [("DiveTUnit.Shapes", "IsEqualTo_local"), ("DiveTUnit.Shapes", "Throws_capturing_delegate"),
         ("DiveTUnit.Shapes", "IsEqualTo_local_after_suspension"), ("DiveTUnit.Shapes", "IsEqualTo_closure_after_suspension")];

    private static readonly MethodInfo FindStatements =
        typeof(AssertionWeaver).GetMethod("FindAssertionStatements", BindingFlags.NonPublic | BindingFlags.Instance)!;

    [Fact]
    public void Dive()
    {
        var output = Environment.GetEnvironmentVariable("KRONIKOL_211_DIVE");
        if (string.IsNullOrEmpty(output))
            return;

        var sb = new StringBuilder();
        sb.AppendLine($"# Dive211: weaver {typeof(AssertionWeaver).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}, runtime {Environment.Version}");
        foreach (var mode in new[] { "Debug", "Release", "ReleaseDebugPlus" })
        {
            Measure("Common", Common, CommonEntries, mode, [], sb);
            Measure("TUnit", TUnitSource, TUnitEntries, mode, [typeof(TUnit.Assertions.Assert).Assembly.Location], sb);
        }
        File.WriteAllText(output, sb.ToString());
    }

    private static void Measure(string group, string source, (string Type, string Method)[] entries, string mode,
        string[] extra, StringBuilder sb)
    {
        var optimization = mode == "Debug" ? OptimizationLevel.Debug : OptimizationLevel.Release;
        TestAssemblyBuilder.DebugPlusRelease = mode == "ReleaseDebugPlus";
        string path, plain;
        try
        {
            path = TestAssemblyBuilder.Build($"Dive211_{group}_{mode}", source, optimization,
                Microsoft.CodeAnalysis.Emit.DebugInformationFormat.PortablePdb, extra);
            plain = TestAssemblyBuilder.Build($"Dive211_{group}_{mode}_plain", source, optimization,
                Microsoft.CodeAnalysis.Emit.DebugInformationFormat.PortablePdb, extra);
        }
        finally
        {
            TestAssemblyBuilder.DebugPlusRelease = false;
        }

        sb.AppendLine();
        sb.AppendLine($"## {group} [{mode}]");
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(AppContext.BaseDirectory);
        resolver.AddSearchDirectory(Path.GetDirectoryName(path)!);
        foreach (var e in extra)
            resolver.AddSearchDirectory(Path.GetDirectoryName(e)!);
        using (var module = ModuleDefinition.ReadModule(path, new ReaderParameters { ReadSymbols = true, AssemblyResolver = resolver }))
        {
            var weaver = new AssertionWeaver();
            foreach (var type in module.GetTypes())
            {
                foreach (var method in type.Methods.Where(m => m.HasBody))
                {
                    var statements = (List<AssertionStatement>)FindStatements.Invoke(weaver, [method, new WeaveResult()])!;
                    foreach (var st in statements)
                    {
                        var locals = string.Join(", ", method.Body.Variables.Select(v => $"V_{v.Index} {v.VariableType.Name}"));
                        sb.AppendLine($"  stmt {type.Name}::{method.Name} L{st.SequencePoint.StartLine} awaited={st.IsAwaited} \"{st.SourceText}\"");
                        sb.AppendLine($"    locals [{locals}]");
                        foreach (var cv in st.CapturedVariables)
                            sb.AppendLine($"    captured '{cv.Name}': local={cv.LocalIndex} closureLocal={cv.ClosureLocalIndex} closureField={cv.ClosureField?.FullName} stateField={cv.StateField?.FullName} param={cv.ParameterIndex}");
                        if (st.CapturedVariables.Count == 0)
                            sb.AppendLine("    captured: none");
                    }
                }
            }
        }

        var before = File.ReadAllBytes(path);
        var weave = new AssertionWeaver().Weave(path, Path.ChangeExtension(path, ".pdb"));
        var errors = WovenIl.NewErrors(before, File.ReadAllBytes(path), path);
        sb.AppendLine($"  weave: WeavedCount={weave.WeavedCount}; ILVerify errors the weave added: {errors.Count}");
        foreach (var error in errors)
            sb.AppendLine($"    {error}");
        foreach (var u in weave.Unwoven)
            sb.AppendLine($"    unwoven: {u}");

        foreach (var (typeName, methodName) in entries)
        {
            var (thrown, notes) = Run(path, typeName, methodName);
            var (plainThrown, _) = Run(plain, typeName, methodName);
            sb.AppendLine($"  run {typeName}.{methodName}: woven threw {Describe(thrown)}; unwoven threw {Describe(plainThrown)}");
            foreach (var n in notes)
                sb.AppendLine($"    note {(n.Passed ? "✓" : "✗")} {n.Label}{(n.Message is { } m ? " | " + m.ReplaceLineEndings(" / ") : "")}");
            if (notes.Length == 0)
                sb.AppendLine("    no note");
        }
    }

    private static (Exception? Thrown, WovenRun.Note[] Notes) Run(string assemblyPath, string typeName, string methodName)
    {
        var type = Assembly.LoadFrom(assemblyPath).GetType(typeName)!;
        var method = type.GetMethod(methodName)!;
        var instance = method.IsStatic ? null : Activator.CreateInstance(type);
        var testId = $"{methodName}_{Guid.NewGuid():N}";
        Exception? thrown;
        using (TestIdentityScope.Begin(testId, testId))
        {
            thrown = Record.Exception(() =>
            {
                switch (method.Invoke(instance, null))
                {
                    case Task task:
                        task.GetAwaiter().GetResult();
                        break;
                    case ValueTask valueTask:
                        valueTask.AsTask().GetAwaiter().GetResult();
                        break;
                }
            });
        }
        if (thrown is TargetInvocationException { InnerException: { } inner })
            thrown = inner;
        return (thrown, WovenRun.NotesFor(testId));
    }

    private static string Describe(Exception? e) =>
        e == null ? "nothing" : $"{e.GetType().Name}: {e.Message.ReplaceLineEndings(" / ")}";
}
