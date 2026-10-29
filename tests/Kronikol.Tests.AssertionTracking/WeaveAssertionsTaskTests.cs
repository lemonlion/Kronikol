using System.Collections;
using System.Text;
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

    internal static (bool Succeeded, RecordingBuildEngine Engine) RunTask(string assemblyPath, Action<WeaveAssertionsTask>? configure = null)
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
        configure?.Invoke(task);
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
        // One warning, which every logger shows, names it; the list is at normal importance, where a build asked for
        // more shows it (#212).
        var warning = Assert.Single(engine.Warnings);
        Assert.Equal("KRONIKOL003", warning.Code);
        Assert.Matches(@"^Kronikol\.AssertionTracking: 1 assertion statement\(s\) were left as written, so they run without a note\. "
            + @"First: \S+, line \d+, in Tests\.Method: the method's evaluation stack could not be followed\. "
            + @"The build lists each one at normal verbosity \(-v:n\)\.$", warning.Message);
        Assert.All(engine.Messages.Where(m => (m.Message ?? "").Contains("unwoven", StringComparison.Ordinal) || (m.Message ?? "").StartsWith("  Tests.", StringComparison.Ordinal)),
            m => Assert.Equal(MessageImportance.Normal, m.Importance));
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

    // #165: a build with deterministic source paths names each document /_/…; the task hands the weave the build's map.
    private const string Mapped = """
        using FluentAssertions;
        using Kronikol.Tracking;

        [assembly: TrackAssertions]

        public static class Cases
        {
            public static void Statements()
            {
                var name = "café";
                name.Should().Be("café");
            }
        }
        """;

    private static string WithSeparator(string directory) =>
        directory.EndsWith(Path.DirectorySeparatorChar) ? directory : directory + Path.DirectorySeparatorChar;

    private static string Label(string path)
    {
        var (thrown, notes) = WovenRun.Run(path, "Cases", "Statements");
        Assert.Null(thrown);
        return Assert.Single(notes).Label;
    }

    // WEAVER_SOURCE_PATHS_PLAN fact 3: the map in @(SourceRoot) alone, each root's MappedPath, with no PathMap.
    [Fact]
    public void The_task_reads_a_mapped_document_through_the_source_roots()
    {
        var root = TestAssemblyBuilder.NewSourceRoot("TaskSourceRoots");
        var path = TestAssemblyBuilder.BuildMapped("TaskSourceRoots",
            [TestAssemblyBuilder.FixtureFile.Utf8(Path.Combine(root, "tests", "Cases.cs"), Mapped)], [new(WithSeparator(root), "/_/")]);

        var (succeeded, engine) = RunTask(path, task => task.SourceRoots =
            [new TaskItem(WithSeparator(root), new Dictionary<string, string> { ["MappedPath"] = "/_/" })]);

        Assert.True(succeeded, string.Join("\n", engine.Errors.Select(e => e.Message)));
        Assert.Equal("Name should be \"café\"", Label(path));
        Assert.Empty(engine.Warnings);
    }

    // WEAVER_SOURCE_PATHS_PLAN fact 4: a project's own PathMap, as the user writes it (no trailing separator), and no
    // deterministic source paths: the source roots' MappedPath is the root itself.
    [Fact]
    public void The_task_reads_a_document_a_projects_own_PathMap_named()
    {
        var root = TestAssemblyBuilder.NewSourceRoot("TaskPathMap");
        var path = TestAssemblyBuilder.BuildMapped("TaskPathMap",
            [TestAssemblyBuilder.FixtureFile.Utf8(Path.Combine(root, "Scenarios", "Cases.cs"), Mapped)], [new(WithSeparator(root), "/src/")]);

        var (succeeded, engine) = RunTask(path, task =>
        {
            task.PathMap = root.TrimEnd(Path.DirectorySeparatorChar) + "=/src/";
            task.SourceRoots = [new TaskItem(WithSeparator(root), new Dictionary<string, string> { ["MappedPath"] = WithSeparator(root) })];
        });

        Assert.True(succeeded, string.Join("\n", engine.Errors.Select(e => e.Message)));
        Assert.Equal("Name should be \"café\"", Label(path));
        Assert.Empty(engine.Warnings);
    }

    // WEAVER_SOURCE_PATHS_PLAN fact 18: the task as the targets run it in a CI build: PathMap with the SDK's trailing
    // comma, the source roots with their MappedPath, and the build's CodePage. Each mapped document is listed at low
    // importance with the file it was read from.
    [Fact]
    public void The_task_takes_the_builds_PathMap_source_roots_and_code_page()
    {
        var root = TestAssemblyBuilder.NewSourceRoot("TaskCiBuild");
        var codePage = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;
        var file = Path.Combine(root, "tests", "Cases.cs");
        var path = TestAssemblyBuilder.BuildMapped("TaskCiBuild",
            [TestAssemblyBuilder.FixtureFile.Utf8(file, Mapped)], [new(WithSeparator(root), "/_/")], codePage: codePage);

        var (succeeded, engine) = RunTask(path, task =>
        {
            task.PathMap = WithSeparator(root).Replace(",", ",,").Replace("=", "==") + "=/_/,";
            task.SourceRoots = [new TaskItem(WithSeparator(root), new Dictionary<string, string> { ["MappedPath"] = "/_/" })];
            task.CodePage = "1252";
        });

        Assert.True(succeeded, string.Join("\n", engine.Errors.Select(e => e.Message)));
        Assert.Equal("Name should be \"cafÃ©\"", Label(path));
        Assert.Contains(engine.Messages, m => m.Importance == MessageImportance.Low
            && m.Message == $"AssertionTracking: Read the source of /_/tests/Cases.cs from {file}");
        Assert.DoesNotContain(engine.Visible(), m => m.Contains("/_/tests/Cases.cs", StringComparison.Ordinal));
        Assert.Empty(engine.Warnings);
    }

    // WEAVER_SOURCE_PATHS_PLAN R2: a woven statement whose source no candidate reaches is labelled by its line, shows no
    // values, and its pragma comments were not read. The build says so once, as KRONIKOL002, a warning a project can
    // silence with <NoWarn>, naming the first such file and every path tried, and lists each file where a build shows it.
    [Fact]
    public void The_task_warns_once_when_a_statements_source_cannot_be_read()
    {
        var root = TestAssemblyBuilder.NewSourceRoot("TaskUnread");
        var file = Path.Combine(root, "tests", "Cases.cs");
        var path = TestAssemblyBuilder.BuildMapped("TaskUnread",
            [TestAssemblyBuilder.FixtureFile.Utf8(file, Mapped) with { OnDisk = false }], [new(WithSeparator(root), "/_/")]);

        var (succeeded, engine) = RunTask(path, task => task.SourceRoots =
            [new TaskItem(WithSeparator(root), new Dictionary<string, string> { ["MappedPath"] = "/_/" })]);

        Assert.True(succeeded, string.Join("\n", engine.Errors.Select(e => e.Message)));
        Assert.Equal("Assertion at line 11", Label(path));
        var warning = Assert.Single(engine.Warnings);
        Assert.Equal("KRONIKOL002", warning.Code);
        Assert.Equal("Kronikol.AssertionTracking: the source of 1 assertion statement(s) in 1 file(s) could not be read, so "
            + "they are labelled by line number, show no values, and their pragma comments were not read. First: "
            + $"/_/tests/Cases.cs; tried {file}, /_/tests/Cases.cs. Setting <EmbedAllSources>true</EmbedAllSources> puts the "
            + "source in the symbols, where the weave finds it without the file.", warning.Message);
        Assert.Contains("Kronikol.AssertionTracking: Could not read the source of 1 file(s); their statements are labelled by line number:",
            engine.Visible());
        Assert.Contains($"  /_/tests/Cases.cs: 1 statement(s); tried {file}, /_/tests/Cases.cs", engine.Visible());
    }

    // R2: the warning counts every statement and file it could not read and names the first; the listing names each,
    // and a file the weave read is in neither.
    [Fact]
    public void The_warning_counts_the_statements_and_files_it_could_not_read()
    {
        var root = TestAssemblyBuilder.NewSourceRoot("TaskUnreadCounts");
        var first = Path.Combine(root, "tests", "First.cs");
        var read = Path.Combine(root, "tests", "Read.cs");
        var last = Path.Combine(root, "tests", "Last.cs");
        var path = TestAssemblyBuilder.BuildMapped("TaskUnreadCounts",
            [
                TestAssemblyBuilder.FixtureFile.Utf8(first, """
                    using FluentAssertions;
                    using Kronikol.Tracking;

                    [assembly: TrackAssertions]

                    public static class First
                    {
                        public static void Statements()
                        {
                            var total = 3;
                            total.Should().Be(3);
                            total.Should().BePositive();
                        }
                    }
                    """) with { OnDisk = false },
                TestAssemblyBuilder.FixtureFile.Utf8(read, """
                    using FluentAssertions;

                    public static class Read
                    {
                        public static void Statements()
                        {
                            var total = 3;
                            total.Should().Be(3);
                        }
                    }
                    """),
                TestAssemblyBuilder.FixtureFile.Utf8(last, """
                    using FluentAssertions;

                    public static class Last
                    {
                        public static void Statements()
                        {
                            var total = 3;
                            total.Should().Be(3);
                        }
                    }
                    """) with { OnDisk = false },
            ], [new(WithSeparator(root), "/_/")]);

        var (succeeded, engine) = RunTask(path, task => task.SourceRoots =
            [new TaskItem(WithSeparator(root), new Dictionary<string, string> { ["MappedPath"] = "/_/" })]);

        Assert.True(succeeded, string.Join("\n", engine.Errors.Select(e => e.Message)));
        var warning = Assert.Single(engine.Warnings);
        Assert.StartsWith("Kronikol.AssertionTracking: the source of 3 assertion statement(s) in 2 file(s) could not be read,",
            warning.Message);
        Assert.Contains($"First: /_/tests/First.cs; tried {first}, /_/tests/First.cs.", warning.Message);
        Assert.Contains("Kronikol.AssertionTracking: Could not read the source of 2 file(s); their statements are labelled by line number:",
            engine.Visible());
        Assert.Contains($"  /_/tests/First.cs: 2 statement(s); tried {first}, /_/tests/First.cs", engine.Visible());
        Assert.Contains($"  /_/tests/Last.cs: 1 statement(s); tried {last}, /_/tests/Last.cs", engine.Visible());
        Assert.DoesNotContain(engine.Visible(), m => m.Contains("Read.cs", StringComparison.Ordinal));
    }

    // R2 audit: the warning says its statements are labelled by line number, which is true only of the statements the
    // weave wraps. One the stack analysis leaves unwoven runs as written, without a note, and is listed under
    // KRONIKOL001's line; 4.21.0 counted it among the unread ones, and a file holding nothing else was named too.
    [Fact]
    public void The_warning_counts_only_the_statements_the_weave_wrapped()
    {
        var root = TestAssemblyBuilder.NewSourceRoot("TaskUnreadUnwoven");
        var cases = Path.Combine(root, "tests", "Cases.cs");
        var other = Path.Combine(root, "tests", "Other.cs");
        var path = TestAssemblyBuilder.BuildMapped("TaskUnreadUnwoven",
            [
                TestAssemblyBuilder.FixtureFile.Utf8(cases, """
                    using FluentAssertions;
                    using Kronikol.Tracking;

                    [assembly: TrackAssertions]

                    public static class Cases
                    {
                        public static void Woven()
                        {
                            var total = 3;
                            total.Should().Be(3);
                        }

                        public static void Method()
                        {
                            var total = 3;
                            total.Should().Be(3);
                        }
                    }
                    """) with { OnDisk = false },
                TestAssemblyBuilder.FixtureFile.Utf8(other, """
                    using FluentAssertions;

                    public static class Other
                    {
                        public static void Method()
                        {
                            var total = 3;
                            total.Should().Be(3);
                        }
                    }
                    """) with { OnDisk = false },
            ], [new(WithSeparator(root), "/_/")]);
        using (var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadWrite = true, ReadSymbols = true }))
        {
            // Two values on one path to the Should() call and none on the other, as in the unwoven statement's fact.
            foreach (var type in new[] { "Cases", "Other" })
            {
                var method = assembly.MainModule.GetType(type).Methods.Single(m => m.Name == "Method");
                var il = method.Body.GetILProcessor();
                var should = method.Body.Instructions.First(i => i.Operand is MethodReference { Name: "Should" });
                var start = method.Body.Instructions[0];
                il.InsertBefore(start, il.Create(OpCodes.Ldc_I4_1));
                il.InsertBefore(start, il.Create(OpCodes.Ldc_I4_1));
                il.InsertBefore(start, il.Create(OpCodes.Ldc_I4_0));
                il.InsertBefore(start, il.Create(OpCodes.Brtrue, should));
                il.InsertBefore(start, il.Create(OpCodes.Pop));
                il.InsertBefore(start, il.Create(OpCodes.Pop));
            }
            assembly.Write(new WriterParameters { WriteSymbols = true });
        }

        var (succeeded, engine) = RunTask(path, task => task.SourceRoots =
            [new TaskItem(WithSeparator(root), new Dictionary<string, string> { ["MappedPath"] = "/_/" })]);

        Assert.True(succeeded, string.Join("\n", engine.Errors.Select(e => e.Message)));
        var visible = engine.Visible().ToList();
        Assert.Contains("Kronikol.AssertionTracking: Left 2 assertion statement(s) unwoven; they run as written, without a note:", visible);
        // The two statements left unwoven are KRONIKOL003's, not this warning's.
        Assert.Single(engine.Warnings, w => w.Code == "KRONIKOL003");
        var warning = Assert.Single(engine.Warnings, w => w.Code == "KRONIKOL002");
        Assert.StartsWith("Kronikol.AssertionTracking: the source of 1 assertion statement(s) in 1 file(s) could not be read,",
            warning.Message);
        Assert.Contains($"First: /_/tests/Cases.cs; tried {cases}, /_/tests/Cases.cs.", warning.Message);
        Assert.Contains("Kronikol.AssertionTracking: Could not read the source of 1 file(s); their statements are labelled by line number:",
            visible);
        Assert.Contains($"  /_/tests/Cases.cs: 1 statement(s); tried {cases}, /_/tests/Cases.cs", visible);
        Assert.DoesNotContain(visible, m => m.Contains("Other.cs", StringComparison.Ordinal));
    }
}
