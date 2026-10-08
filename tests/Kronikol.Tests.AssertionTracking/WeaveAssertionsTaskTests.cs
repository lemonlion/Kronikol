using System.Collections;
using Kronikol.AssertionTracking;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Kronikol.Tests.AssertionTracking;

/// <summary>
/// <c>WeaveAssertionsTask</c> itself, driven through a build engine that records what it logs, at what
/// importance and with what code: the gate the <c>.targets</c> file runs and every build passes through.
/// No test ran it before (SHOULDLY_ASSERTIONS_PLAN F19), and it skipped a project whose symbols are embedded
/// in the assembly (<c>DebugType=embedded</c>) as silently as one with none (F4).
/// </summary>
public class WeaveAssertionsTaskTests
{
    private const string Source = """
        using FluentAssertions;
        using Kronikol.Tracking;

        [assembly: TrackAssertions]

        public class Tests
        {
            public void Method()
            {
                var total = 3;
                total.Should().Be(3);
            }

            public void Failing()
            {
                var total = 3;
                total.Should().Be(4);
            }
        }
        """;

    internal sealed class RecordingBuildEngine : IBuildEngine
    {
        public List<BuildMessageEventArgs> Messages { get; } = [];
        public List<BuildWarningEventArgs> Warnings { get; } = [];
        public List<BuildErrorEventArgs> Errors { get; } = [];

        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => "Fixture.csproj";

        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => true;
        public void LogCustomEvent(CustomBuildEventArgs e) { }
        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);
        public void LogMessageEvent(BuildMessageEventArgs e) => Messages.Add(e);
        public void LogWarningEvent(BuildWarningEventArgs e) => Warnings.Add(e);

        /// <summary>What <c>dotnet build</c> prints at its default verbosity: warnings, errors and messages of Normal or High importance.</summary>
        public IEnumerable<string> Visible() =>
            Messages.Where(m => m.Importance != MessageImportance.Low).Select(m => m.Message ?? "")
                .Concat(Warnings.Select(w => w.Message ?? ""))
                .Concat(Errors.Select(e => e.Message ?? ""));
    }

    internal static (bool Succeeded, RecordingBuildEngine Engine) RunTask(string assemblyPath)
    {
        var engine = new RecordingBuildEngine();
        var references = new[]
            {
                typeof(FluentAssertions.AssertionExtensions).Assembly.Location,
                typeof(AwesomeAssertions.AssertionExtensions).Assembly.Location,
                typeof(Kronikol.Tracking.Track).Assembly.Location,
                typeof(object).Assembly.Location
            }
            .Select(p => (ITaskItem)new TaskItem(p))
            .ToArray();
        var task = new WeaveAssertionsTask { BuildEngine = engine, AssemblyPath = assemblyPath, References = references };
        var unwoven = File.ReadAllBytes(assemblyPath);
        var succeeded = task.Execute();
        if (succeeded)
            WovenIl.AssertNoNewErrors(unwoven, assemblyPath);
        return (succeeded, engine);
    }

    [Fact]
    public void The_task_weaves_an_assembly_whose_pdb_is_embedded()
    {
        var path = TestAssemblyBuilder.Build("TaskEmbeddedPdb", Source, OptimizationLevel.Debug, DebugInformationFormat.Embedded);
        Assert.False(File.Exists(Path.ChangeExtension(path, ".pdb")));

        var (succeeded, engine) = RunTask(path);

        Assert.True(succeeded, string.Join("\n", engine.Errors.Select(e => e.Message)));
        Assert.Contains(engine.Visible(), m => m.StartsWith("Kronikol.AssertionTracking: Instrumented 2 assertion(s)", StringComparison.Ordinal));

        // The woven assembly keeps its symbols where they were, so a stack trace still has line numbers.
        Assert.False(File.Exists(Path.ChangeExtension(path, ".pdb")));
        using (var module = ModuleDefinition.ReadModule(path, new ReaderParameters { ReadSymbols = true, SymbolReaderProvider = new EmbeddedPortablePdbReaderProvider() }))
            Assert.True(module.HasSymbols);

        var (thrown, notes) = WovenRun.Run(path, "Tests", "Failing");
        Assert.NotNull(thrown);
        var note = Assert.Single(notes);
        Assert.False(note.Passed);
        Assert.Equal("Total should be 4", note.Label);
        Assert.Equal("TaskEmbeddedPdb.cs:L17", note.Source);
        Assert.Contains("TaskEmbeddedPdb.cs:line 17", thrown!.StackTrace);
    }

    [Fact]
    public void The_task_weaves_a_portable_pdb_as_before()
    {
        var path = TestAssemblyBuilder.Build("TaskPortablePdb", Source);

        var (succeeded, engine) = RunTask(path);

        Assert.True(succeeded, string.Join("\n", engine.Errors.Select(e => e.Message)));
        Assert.Contains(engine.Visible(), m => m.StartsWith("Kronikol.AssertionTracking: Instrumented 2 assertion(s)", StringComparison.Ordinal));

        var (thrown, notes) = WovenRun.Run(path, "Tests", "Failing");
        Assert.NotNull(thrown);
        var note = Assert.Single(notes);
        Assert.Equal("Total should be 4", note.Label);
        Assert.Equal("TaskPortablePdb.cs:L17", note.Source);
        Assert.Contains("TaskPortablePdb.cs:line 17", thrown!.StackTrace);
    }

    [Fact]
    public void The_task_logs_a_method_it_leaves_unwoven()
    {
        // The weave noted a method it could not read and nothing ever printed the note (plan section 8, item 3).
        var path = TestAssemblyBuilder.Build("TaskStrippedMethod", Source);
        using (var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadWrite = true, ReadSymbols = true }))
        {
            assembly.MainModule.GetType("Tests").Methods.Single(m => m.Name == "Failing").DebugInformation.SequencePoints.Clear();
            assembly.Write(new WriterParameters { WriteSymbols = true });
        }

        var (succeeded, engine) = RunTask(path);

        Assert.True(succeeded, string.Join("\n", engine.Errors.Select(e => e.Message)));
        Assert.Contains(engine.Visible(), m => m.StartsWith("Kronikol.AssertionTracking: Instrumented 1 assertion(s)", StringComparison.Ordinal));
        var note = Assert.Single(engine.Messages, m => m.Message?.Contains("no sequence points", StringComparison.Ordinal) == true);
        Assert.Equal("AssertionTracking: Tests.Failing calls an assertion but has no sequence points, so it is left unwoven", note.Message);
    }

    [Fact]
    public void The_task_lists_a_statement_it_leaves_unwoven_where_a_build_shows_it()
    {
        // A statement the stack analysis cannot account for runs as written, without a note; the build says so at
        // its default verbosity, with the reason, and the rest of the assembly is woven. Two values on one path to
        // Method's Should() call and none on the other: the depths cannot be reconciled.
        var path = TestAssemblyBuilder.Build("TaskUnwovenStatement", Source);
        using (var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadWrite = true, ReadSymbols = true }))
        {
            var method = assembly.MainModule.GetType("Tests").Methods.Single(m => m.Name == "Method");
            var il = method.Body.GetILProcessor();
            var should = method.Body.Instructions.First(i => i.Operand is MethodReference { Name: "Should" });
            var first = method.Body.Instructions[0];
            il.InsertBefore(first, il.Create(OpCodes.Ldc_I4_1));
            il.InsertBefore(first, il.Create(OpCodes.Ldc_I4_1));
            il.InsertBefore(first, il.Create(OpCodes.Ldc_I4_0));
            il.InsertBefore(first, il.Create(OpCodes.Brtrue, should));
            il.InsertBefore(first, il.Create(OpCodes.Pop));
            il.InsertBefore(first, il.Create(OpCodes.Pop));
            assembly.Write(new WriterParameters { WriteSymbols = true });
        }

        var (succeeded, engine) = RunTask(path);

        Assert.True(succeeded, string.Join("\n", engine.Errors.Select(e => e.Message)));
        var visible = engine.Visible().ToList();
        Assert.Contains(visible, m => m.StartsWith("Kronikol.AssertionTracking: Instrumented 1 assertion(s)", StringComparison.Ordinal));
        Assert.Contains("Kronikol.AssertionTracking: Left 1 assertion statement(s) unwoven; they run as written, without a note:", visible);
        Assert.Contains(visible, m => m.StartsWith("  Tests.Method, line ", StringComparison.Ordinal) &&
                                      m.EndsWith(": the method's evaluation stack could not be followed", StringComparison.Ordinal));
        Assert.Empty(engine.Warnings);
    }

    [Fact]
    public void The_task_leaves_an_assembly_without_symbols_unwoven()
    {
        // DebugType=none: without sequence points there is no statement to wrap.
        var path = TestAssemblyBuilder.Build("TaskNoSymbols", Source, OptimizationLevel.Debug, symbols: null);
        var before = File.ReadAllBytes(path);

        var (succeeded, engine) = RunTask(path);

        Assert.True(succeeded, string.Join("\n", engine.Errors.Select(e => e.Message)));
        Assert.Empty(engine.Errors);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    // #144: the attribute declared and nothing instrumented said so only at detailed verbosity.
    private static string Warning(RecordingBuildEngine engine)
    {
        var warning = Assert.Single(engine.Warnings);
        Assert.Equal("KRONIKOL001", warning.Code);
        Assert.StartsWith("Kronikol.AssertionTracking: [assembly: TrackAssertions] is declared, but no assertion was instrumented: ", warning.Message);
        return warning.Message!;
    }

    [Fact]
    public void The_task_warns_when_the_attribute_finds_no_supported_library()
    {
        var path = TestAssemblyBuilder.Build("TaskNoLibrary", """
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public void Method()
                {
                    if (1 + 1 != 2)
                        throw new System.Exception("arithmetic");
                }
            }
            """);

        var (succeeded, engine) = RunTask(path);

        Assert.True(succeeded);
        var message = Warning(engine);
        Assert.Contains("FluentAssertions, AwesomeAssertions, Shouldly or TUnit.Assertions", message);
        Assert.Contains("Track.That(() => ...)", message);
    }

    [Fact]
    public void The_task_warns_when_the_build_has_no_symbols()
    {
        var path = TestAssemblyBuilder.Build("TaskNoSymbolsWarns", Source, OptimizationLevel.Debug, symbols: null);

        var (succeeded, engine) = RunTask(path);

        Assert.True(succeeded);
        Assert.Contains("<DebugType>none</DebugType>", Warning(engine));
    }

    [Fact]
    public void The_task_warns_when_no_statement_calls_an_assertion()
    {
        var path = TestAssemblyBuilder.Build("TaskNoStatement", """
            using FluentAssertions.Execution;
            using Kronikol.Tracking;

            [assembly: TrackAssertions]

            public class Tests
            {
                public void Method()
                {
                    using var scope = new AssertionScope();
                }
            }
            """);

        var (succeeded, engine) = RunTask(path);

        Assert.True(succeeded);
        Assert.Contains("no statement in the assembly calls an assertion", Warning(engine));
    }

    [Fact]
    public void No_warning_without_the_attribute()
    {
        var path = TestAssemblyBuilder.Build("TaskNoAttribute", """
            using FluentAssertions;

            public class Tests
            {
                public void Method() => 3.Should().Be(3);
            }
            """);

        var (succeeded, engine) = RunTask(path);

        Assert.True(succeeded);
        Assert.Empty(engine.Warnings);
    }

    [Fact]
    public void No_warning_when_something_is_woven()
    {
        var path = TestAssemblyBuilder.Build("TaskWovenNoWarning", Source);

        var (succeeded, engine) = RunTask(path);

        Assert.True(succeeded);
        Assert.Empty(engine.Warnings);
    }
}
