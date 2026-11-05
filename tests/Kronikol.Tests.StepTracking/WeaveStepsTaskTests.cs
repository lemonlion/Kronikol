using System.Collections;
using Kronikol.StepTracking;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Kronikol.Tests.StepTracking;

/// <summary><c>WeaveStepsTask</c> itself, driven through a build engine that records what it logs, at what importance and with what code.</summary>
public class WeaveStepsTaskTests
{
    private sealed class RecordingBuildEngine : IBuildEngine
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
    }

    private static (bool Succeeded, RecordingBuildEngine Engine) RunTask(string assemblyPath)
    {
        var engine = new RecordingBuildEngine();
        var task = new WeaveStepsTask
        {
            BuildEngine = engine,
            AssemblyPath = assemblyPath,
            References = [new TaskItem(typeof(Kronikol.Tracking.Track).Assembly.Location)],
        };
        var succeeded = task.Execute();
        return (succeeded, engine);
    }

    [Fact]
    public void The_task_names_the_first_method_by_document_and_line()
    {
        // The first by document and line, not the first the weave came to: a type listed earlier sits further down this file.
        const string source = """
            using System.Collections.Generic;
            using Kronikol.Tracking;

            [assembly: TrackSteps]

            public class Earlier
            {
                [Step] public IEnumerable<int> Later_in_the_file() { yield return 1; }
            }

            public class Steps
            {
                public void Nothing() { }
            }
            """;
        var path = TestAssemblyBuilder.Build("TaskNamesFirst", source);

        var (succeeded, engine) = RunTask(path);

        Assert.True(succeeded, string.Join("\n", engine.Errors.Select(e => e.Message)));
        var warning = Assert.Single(engine.Warnings);
        Assert.Equal("KRONIKOL004", warning.Code);
        Assert.Matches(@"^Kronikol\.StepTracking: 1 step method\(s\) were left as written, so they run without a step\. "
            + @"First: \S*TaskNamesFirst\.cs, line 8, in Earlier\.Later_in_the_file: it is an iterator, whose body runs while its caller enumerates it, after the method has returned\. "
            + @"The build lists each one at normal verbosity \(-v:n\)\.$", warning.Message);
        Assert.Contains(engine.Messages, m => m.Importance == MessageImportance.Normal && m.Message == "Kronikol.StepTracking: Left 1 step method(s) unwoven; they run as written, without a step:");
    }

    [Fact]
    public void Two_refused_methods_are_named_by_their_order_in_the_file_and_not_by_the_weave()
    {
        const string source = """
            using System.Collections.Generic;
            using Kronikol.Tracking;

            [assembly: TrackSteps]

            public class Zed
            {
                [Step] public IEnumerable<int> Comes_first() { yield return 1; }
                [Step] public IEnumerable<int> Comes_second() { yield return 1; }
            }
            """;
        var path = TestAssemblyBuilder.Build("TaskNamesOrder", source);

        var (_, engine) = RunTask(path);

        Assert.Contains("line 8, in Zed.Comes_first", Assert.Single(engine.Warnings).Message);
    }

    [Fact]
    public void A_step_it_can_weave_says_nothing_of_being_left()
    {
        const string source = """
            using Kronikol.Tracking;

            [assembly: TrackSteps]

            public class Steps
            {
                [Step] public int A_value() { return 7; }
            }
            """;
        var path = TestAssemblyBuilder.Build("TaskWeaves", source);

        var (succeeded, engine) = RunTask(path);

        Assert.True(succeeded);
        Assert.Empty(engine.Warnings);
        Assert.DoesNotContain(engine.Messages, m => (m.Message ?? "").Contains("unwoven", StringComparison.Ordinal));
    }
}

/// <summary>
/// F17: <c>Cecil</c>'s resolver searches <c>.</c> and <c>bin</c> first, and classifying a method asks it about the type the method returns. A stale copy
/// of an assembly in the current directory must not decide a shape. In a collection of its own, as it moves the current directory.
/// </summary>
[Collection(nameof(StaleAssemblyCollection))]
public class StaleAssemblyTests
{
    [Fact]
    public void A_stale_assembly_in_the_current_directory_does_not_decide_a_shape()
    {
        var root = Path.Combine(Path.GetTempPath(), "KRONIKOL_StepStale", Guid.NewGuid().ToString("N")[..8]);
        var references = Path.Combine(root, "references");
        var elsewhere = Path.Combine(root, "elsewhere");
        var build = Path.Combine(root, "build");
        foreach (var directory in new[] { references, elsewhere, build })
            Directory.CreateDirectory(directory);

        // The real Dep's Thing is a plain value. A copy left in the current directory says it can be awaited, which would have the method refused.
        Compile("Dep", "public class Thing { }", Path.Combine(references, "Dep.dll"), []);
        Compile("Dep", "public class Thing { public System.Runtime.CompilerServices.TaskAwaiter GetAwaiter() => default; }", Path.Combine(elsewhere, "Dep.dll"), []);
        var assembly = Path.Combine(build, "Stale.dll");
        Compile("Stale", """
            using System;
            [assembly: Kronikol.Tracking.TrackSteps]
            namespace Kronikol.Tracking
            {
                [AttributeUsage(AttributeTargets.Assembly)] internal sealed class TrackStepsAttribute : Attribute { }
                [AttributeUsage(AttributeTargets.Method)] internal sealed class StepAttribute : Attribute { }
            }
            public class Steps
            {
                [Kronikol.Tracking.Step] public Thing M() { return new Thing(); }
            }
            """, assembly, [Path.Combine(references, "Dep.dll")]);

        var before = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(elsewhere);
        try
        {
            var result = new StepWeaver(null, [references, Path.GetDirectoryName(typeof(Kronikol.Tracking.Track).Assembly.Location)!]).Weave(assembly, Path.ChangeExtension(assembly, ".pdb"));

            Assert.Empty(result.UnwovenSteps);
            Assert.Equal(1, result.WeavedCount);
        }
        finally
        {
            Directory.SetCurrentDirectory(before);
        }
    }

    private static void Compile(string name, string source, string path, IReadOnlyList<string> references)
    {
        var platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(p => string.Equals(Path.GetDirectoryName(p), Path.GetDirectoryName(typeof(object).Assembly.Location), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(p).StartsWith("System", StringComparison.Ordinal) && !Path.GetFileName(p).Contains("Native", StringComparison.Ordinal))
            .Append(typeof(Kronikol.Tracking.Track).Assembly.Location)
            .Concat(references);
        var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source)],
            platform.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var pe = File.Create(path);
        using var pdb = File.Create(Path.ChangeExtension(path, ".pdb"));
        var emitted = compilation.Emit(pe, pdb, options: new Microsoft.CodeAnalysis.Emit.EmitOptions(debugInformationFormat: Microsoft.CodeAnalysis.Emit.DebugInformationFormat.PortablePdb));
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
    }
}

[CollectionDefinition(nameof(StaleAssemblyCollection), DisableParallelization = true)]
public class StaleAssemblyCollection;
