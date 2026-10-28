using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using Kronikol.AssertionTracking;
using Kronikol.Tracking;
using Mono.Cecil;
using Mono.Cecil.Cil;
using static Kronikol.Tests.AssertionTracking.TestAssemblyBuilder;

namespace Kronikol.Tests.AssertionTracking;

/// <summary>
/// #212 (AWAITED_ASSERTION_LABELS_PLAN section 6): the statement shapes the plan's probes measured, each a file of its
/// own under <c>Fixtures/AwaitedShapes/</c>, so a new shape is one file. A file's header names the builds it runs in,
/// the notes each of its entry methods draws, in order, and the statements the weave leaves as written on purpose, with
/// the reason it gives. Every shape is built in each of its builds, woven, and run beside an unwoven build of the same
/// source; one header serves every build, so the same notes in Debug and in Release is part of each fact.
/// </summary>
public class AwaitedAssertionShapeTests
{
    private static readonly string ShapesDir = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "AwaitedShapes"));

    private static readonly Lazy<IReadOnlyDictionary<string, Shape>> Shapes = new(ReadShapes);

    private static readonly ConcurrentDictionary<(string Id, BuildConfiguration Configuration), Lazy<Builds>> Built = new();

    public static TheoryData<string, BuildConfiguration> ShapeBuilds()
    {
        var data = new TheoryData<string, BuildConfiguration>();
        foreach (var shape in Shapes.Value.Values.OrderBy(s => s.Id, StringComparer.Ordinal))
            foreach (var configuration in shape.Builds)
                data.Add(shape.Id, configuration);
        return data;
    }

    [Theory]
    [MemberData(nameof(ShapeBuilds))]
    public void Each_entry_draws_the_notes_its_shape_names(string id, BuildConfiguration configuration)
    {
        var shape = Shapes.Value[id];
        var builds = BuildsOf(shape, configuration);

        foreach (var (entry, expected) in shape.Entries)
        {
            var (thrown, notes) = Run(builds.Woven, shape.TypeName, entry);

            Assert.IsNotType<InvalidProgramException>(thrown);
            var drawn = notes.Select(Text).ToArray();
            Assert.True(expected.SequenceEqual(drawn),
                $"{id}, {configuration}, {entry}: expected [{string.Join(" | ", expected)}], drawn [{string.Join(" | ", drawn)}]");

            // The statement that threw is the last one drawn, and its note carries the failure the test saw.
            if (thrown != null && notes.Length > 0 && !notes[^1].Passed)
                Assert.Equal(Flat(thrown.Message), Flat(notes[^1].Message));
        }
    }

    [Theory]
    [MemberData(nameof(ShapeBuilds))]
    public void Each_entry_ends_as_it_does_unwoven(string id, BuildConfiguration configuration)
    {
        var shape = Shapes.Value[id];
        var builds = BuildsOf(shape, configuration);

        foreach (var (entry, _) in shape.Entries)
        {
            var (expected, _) = Run(builds.Plain, shape.TypeName, entry);
            var (actual, _) = Run(builds.Woven, shape.TypeName, entry);
            // Shouldly writes its message from the code of the failing frame's line. The message to keep is the Debug
            // build's: an optimised frame can map to a line the statement does not start on, and Shouldly then falls
            // back to "Task should…", where the weave, which puts the statement's sequence point at its start, reads
            // the statement (ShouldlyWeaveTests.A_woven_Shouldly_message_equals_the_unwoven_one).
            var (message, _) = Run(BuildsOf(shape, BuildConfiguration.Debug).Plain, shape.TypeName, entry);

            Assert.Equal(expected?.GetType(), actual?.GetType());
            Assert.Equal(message?.Message, actual?.Message);
        }
    }

    [Theory]
    [MemberData(nameof(ShapeBuilds))]
    public void The_weave_leaves_as_written_only_what_the_shape_names(string id, BuildConfiguration configuration)
    {
        var shape = Shapes.Value[id];
        var unwoven = BuildsOf(shape, configuration).Result.Unwoven;

        Assert.True(unwoven.Count == shape.Unwoven.Count,
            $"{id}, {configuration}: expected {shape.Unwoven.Count} left as written, got [{string.Join("; ", unwoven)}]");
        foreach (var (method, reason) in shape.Unwoven)
            Assert.Single(unwoven, u => (u.Contains($"<{method}>d__") || u.Contains($".{method}, line ")) &&
                                        u.EndsWith(": " + reason, StringComparison.Ordinal));
    }

    /// <summary>
    /// Fact 7 of the plan: every assertion call in the code is in a statement the weave wrapped, which reports its
    /// line to <c>Track</c>, or in one it names as left unwoven. Nothing is skipped in silence (F8).
    /// </summary>
    [Theory]
    [MemberData(nameof(ShapeBuilds))]
    public void Every_assertion_call_is_woven_or_listed(string id, BuildConfiguration configuration)
    {
        var shape = Shapes.Value[id];
        var builds = BuildsOf(shape, configuration);
        using var module = ModuleDefinition.ReadModule(builds.Woven, new ReaderParameters
        {
            ReadSymbols = true,
            AssemblyResolver = Resolver(builds.Woven),
        });

        var missing = new List<string>();
        foreach (var method in module.GetTypes().Where(t => !IsInOwnShouldlyMethodsType(t))
                     .SelectMany(t => t.Methods).Where(m => m.HasBody && m.DebugInformation.HasSequencePoints))
        {
            var tracked = method.Body.Instructions
                .Where(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference { DeclaringType.Name: "Track" } mr &&
                            mr.Name.StartsWith("Assertion", StringComparison.Ordinal))
                .Select(i => LineArgument(i.Previous))
                .ToHashSet();
            foreach (var call in method.Body.Instructions.Where(i =>
                         (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) &&
                         i.Operand is MethodReference mr && AssertionWeaver.IsAssertionEntryPoint(mr)))
            {
                var line = VisibleLine(method, call);
                var listed = builds.Result.Unwoven.Any(u =>
                    u.StartsWith($"{method.DeclaringType.FullName}.{method.Name}, line {line}:", StringComparison.Ordinal));
                if (!tracked.Contains(line) && !listed)
                    missing.Add($"{method.DeclaringType.FullName}.{method.Name}, line {line}: {((MethodReference)call.Operand).Name}");
            }
        }

        Assert.True(missing.Count == 0, $"{id}, {configuration}: assertion calls neither woven nor listed:\n  " +
                                        string.Join("\n  ", missing));
    }

    /// <summary>
    /// The shapes whose IL the probes compared across compilers, built as a consumer's project builds them: by
    /// <c>dotnet build</c> with SDKs 8, 9 and 10 (Roslyn 4.11, 4.14 and 5.6), in Debug, in plain Release and in Release
    /// with debug-plus (<c>DebugSymbols=true</c>, as xUnit v2's props set it), each build in a project directory of
    /// its own.
    /// </summary>
    public static TheoryData<string, string, string> SdkBuilds()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var sdk in new[] { "8.0", "9.0", "10.0" })
            foreach (var configuration in new[] { BuildConfiguration.Debug, BuildConfiguration.Release, BuildConfiguration.ReleaseDebugPlus })
                foreach (var id in new[] { "R212", "S2", "S4", "S7", "S8", "S16a", "S17a" })
                    if (Shapes.Value[id].Builds.Contains(configuration))
                        data.Add(sdk, configuration.ToString(), id);
        return data;
    }

    [Theory]
    [MemberData(nameof(SdkBuilds))]
    public void Each_sdk_builds_a_shape_that_draws_the_notes_it_names(string sdk, string configuration, string id)
    {
        if (!IsSdkAvailable(sdk))
            Assert.Skip($".NET {sdk} SDK not installed");
        var shape = Shapes.Value[id];

        var path = BuildWithSdk($"AwaitedSdk{sdk.Replace(".0", "")}_{id}_{configuration}", shape.Source, sdk + ".0",
            "net" + sdk, configuration == "Debug" ? "Debug" : "Release",
            properties: configuration == "ReleaseDebugPlus" ? new Dictionary<string, string> { ["DebugSymbols"] = "true" } : null);
        var result = WovenIl.Weave(new AssertionWeaver(), path);

        Assert.True(result.Unwoven.Count == shape.Unwoven.Count, string.Join("; ", result.Unwoven));
        foreach (var (entry, expected) in shape.Entries)
        {
            var (thrown, notes) = Run(path, shape.TypeName, entry);

            Assert.IsNotType<InvalidProgramException>(thrown);
            var drawn = notes.Select(Text).ToArray();
            Assert.True(expected.SequenceEqual(drawn),
                $"{id}, SDK {sdk} {configuration}, {entry}: expected [{string.Join(" | ", expected)}], drawn [{string.Join(" | ", drawn)}]");
        }
    }

    /// <summary>
    /// An awaited assertion whose merge point the weave cannot find (here the IsCompleted test's brtrue made to come
    /// later) is listed with its reason, not wrapped as a synchronous statement, whose pass would be recorded when the
    /// task is made, before the assertion has run (4.1 a).
    /// </summary>
    [Fact]
    public void An_await_whose_merge_point_is_not_found_is_listed_not_woven_as_synchronous()
    {
        var shape = Shapes.Value["S1"];
        var path = Build("Awaited_NoMergePoint", shape.Source, BuildConfiguration.Debug);
        using (var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadWrite = true, ReadSymbols = true }))
        {
            var moveNext = assembly.MainModule.GetTypes().Single(t => t.Name.StartsWith("<Run>d__", StringComparison.Ordinal))
                .Methods.Single(m => m.Name == "MoveNext");
            var isCompleted = moveNext.Body.Instructions.Last(i => i.Operand is MethodReference { Name: "get_IsCompleted" });
            var il = moveNext.Body.GetILProcessor();
            il.InsertAfter(isCompleted, il.Create(OpCodes.Nop));
            assembly.Write(new WriterParameters { WriteSymbols = true });
        }

        var result = WovenIl.Weave(new AssertionWeaver(), path);

        var line = File.ReadAllLines(Path.ChangeExtension(path, ".cs"))
            .Select((text, index) => (text, index))
            .Single(l => l.text.TrimStart().StartsWith("await Should.ThrowAsync", StringComparison.Ordinal)).index + 1;
        Assert.Contains(result.Unwoven, u => u.Contains("<Run>d__") &&
                                             u.EndsWith($", line {line}: the merge point of an await in the statement could not be found", StringComparison.Ordinal));
        var (_, notes) = Run(path, shape.TypeName, "AssertionFails");
        Assert.Empty(notes);
    }

    private static string Text(WovenRun.Note note) => (note.Passed ? "✓ " : "✗ ") + note.Label;

    // A note's body is wrapped for the diagram: compare its words.
    private static string Flat(string? text) =>
        string.Concat((text ?? "").Where(c => !char.IsWhiteSpace(c) && c != (char)0x200B));

    private static Builds BuildsOf(Shape shape, BuildConfiguration configuration) =>
        Built.GetOrAdd((shape.Id, configuration), key => new Lazy<Builds>(() =>
        {
            var name = $"Awaited_{key.Id}_{key.Configuration}";
            var plain = Build(name + "_plain", shape.Source, key.Configuration, shape.References);
            var woven = Build(name, shape.Source, key.Configuration, shape.References);
            return new Builds(plain, woven, WovenIl.Weave(new AssertionWeaver(), woven));
        })).Value;

    /// <summary>Runs an entry in a load context of its own, so a woven and an unwoven build of one source can both
    /// run, and reads back the notes it drew.</summary>
    private static (Exception? Thrown, WovenRun.Note[] Notes) Run(string assemblyPath, string typeName, string methodName)
    {
        var context = new AssemblyLoadContext($"{Path.GetFileNameWithoutExtension(assemblyPath)}_{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            var type = context.LoadFromAssemblyPath(assemblyPath).GetType(typeName, throwOnError: true)!;
            var method = type.GetMethod(methodName)!;
            var instance = method.IsStatic ? null : Activator.CreateInstance(type);
            var testId = $"{methodName}_{Guid.NewGuid():N}";
            Exception? thrown;
            using (TestIdentityScope.Begin(testId, testId))
            {
                thrown = Record.Exception(() =>
                {
                    if (method.Invoke(instance, null) is Task task)
                        task.GetAwaiter().GetResult();
                });
            }
            if (thrown is TargetInvocationException { InnerException: { } inner })
                thrown = inner;
            return (thrown, WovenRun.NotesFor(testId));
        }
        finally
        {
            context.Unload();
        }
    }

    private static int LineArgument(Instruction? load) => load?.OpCode.Code switch
    {
        Code.Ldc_I4 => (int)load.Operand,
        Code.Ldc_I4_S => (sbyte)load.Operand,
        _ => -1,
    };

    // The line of the visible sequence point an instruction runs under: the statement it belongs to, also for code
    // after an await's resumption, which the compiler puts under a hidden point.
    private static int VisibleLine(MethodDefinition method, Instruction instruction)
    {
        var line = -1;
        foreach (var point in method.DebugInformation.SequencePoints)
        {
            if (point.Offset > instruction.Offset)
                break;
            if (!point.IsHidden)
                line = point.StartLine;
        }
        return line;
    }

    private static bool IsInOwnShouldlyMethodsType(TypeDefinition type)
    {
        for (var t = type; t != null; t = t.DeclaringType)
            if (t.CustomAttributes.Any(a => a.AttributeType.FullName == "Shouldly.ShouldlyMethodsAttribute"))
                return true;
        return false;
    }

    private static DefaultAssemblyResolver Resolver(string assemblyPath)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(assemblyPath)!);
        resolver.AddSearchDirectory(AppContext.BaseDirectory);
        return resolver;
    }

    private sealed record Builds(string Plain, string Woven, WeaveResult Result);

    /// <param name="Entries">Each entry method, and the notes it draws: "✓ label" or "✗ label", in order.</param>
    /// <param name="Unwoven">Each statement left as written on purpose: the method it is in, and the reason.</param>
    private sealed record Shape(
        string Id,
        string Source,
        string TypeName,
        IReadOnlyList<BuildConfiguration> Builds,
        IReadOnlyList<string> References,
        IReadOnlyList<(string Entry, string[] Notes)> Entries,
        IReadOnlyList<(string Method, string Reason)> Unwoven);

    private static IReadOnlyDictionary<string, Shape> ReadShapes()
    {
        var shared = File.ReadAllText(Path.Combine(ShapesDir, "Shared.cs"));
        var shapes = new Dictionary<string, Shape>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(ShapesDir, "*.cs").Where(p => Path.GetFileName(p) != "Shared.cs"))
        {
            var text = File.ReadAllText(path).ReplaceLineEndings("\n");
            var id = Path.GetFileNameWithoutExtension(path);
            var header = text.Split('\n').Skip(1).TakeWhile(l => l.StartsWith("// ", StringComparison.Ordinal))
                .Select(l => l[3..]).ToList();

            string? Value(string key) => header.FirstOrDefault(l => l.StartsWith(key + ": ", StringComparison.Ordinal))?[(key.Length + 2)..];

            var builds = (Value("Builds") ?? throw new InvalidOperationException($"{id}: no Builds line"))
                .Split(',', StringSplitOptions.TrimEntries).Select(Enum.Parse<BuildConfiguration>).ToList();
            var references = Value("References") == "TUnit.Assertions"
                ? new[] { typeof(TUnit.Assertions.Assert).Assembly.Location }
                : [];
            var unwoven = header.Where(l => l.StartsWith("Unwoven: ", StringComparison.Ordinal))
                .Select(l => l["Unwoven: ".Length..].Split(": ", 2))
                .Select(p => (p[0], p[1]))
                .ToList();
            var keys = new[] { "Builds", "References", "Type", "Shared", "Unwoven" };
            var entries = header
                .Select(l => l.Split(": ", 2))
                .Where(p => p.Length == 2 && !keys.Contains(p[0]) && p[0].All(c => char.IsLetterOrDigit(c) || c == '_'))
                .Select(p => (p[0], p[1] == "(none)" ? [] : p[1].Split(" | ")))
                .ToList();

            var source = Value("Shared") == "none" ? text : text + "\n" + shared;
            shapes[id] = new Shape(id, source, Value("Type") ?? "Probe", builds, references, entries, unwoven);
        }
        return shapes;
    }
}
