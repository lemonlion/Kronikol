using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using ILVerify;
using Kronikol.AssertionTracking;
using Kronikol.Reports;
using Kronikol.StepTracking;
using Kronikol.Tracking;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Task = System.Threading.Tasks.Task;

namespace Kronikol.Tests.StepTracking;

/// <summary>
/// Runs one shape in one build (STEP_METHOD_SHAPES_PLAN section 9.1), in this order, which is the point. It compiles the shape,
/// verifies it as compiled, weaves it and verifies it again: a by-ref regression then fails a fact, where running it would
/// kill the test host with 0xC0000005 and every other result. Then it asks the JIT about each step method, which sees a path no
/// run takes, and only then runs the shape's driver and compares what it returned, what it threw, the step tree, the time
/// inside the step and any step left open with what the shape says should happen.
/// </summary>
internal static class ShapeRunner
{
    /// <summary>The way a shape is built: Debug, Release, and Release with <c>/debug+</c>, as xUnit v2's projects build.</summary>
    public static readonly string[] Configurations = ["Debug", "Release", "ReleasePlus"];

    private static readonly string[] Severity =
        ["COMPILE", "WEAVE-THROW", "LOCKED", "BAD-IL", "JIT", "RUN-IPE", "LOAD", "RUN-OTHER", "VALUE", "THROWS", "OPEN", "TREE", "EARLY", "NOT-REFUSED", "WRONGLY-REFUSED", "ASSERTION-LOST", "SYMBOLS-LOST", "FILE-LOST", "LEFT-BEHIND", "PDB-PATH", "WARNING"];

    // Each compile shares these, and so Roslyn's reading of them: most of what a compile costs.
    private static readonly Lazy<IReadOnlyList<MetadataReference>> References = new(() =>
    {
        var framework = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(path => string.Equals(Path.GetDirectoryName(path), framework, StringComparison.OrdinalIgnoreCase) && IsManaged(path))
            .Append(typeof(Track).Assembly.Location)
            .Append(typeof(Shouldly.ShouldAssertException).Assembly.Location)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
    });

    private static readonly ConcurrentDictionary<string, PEReader> SharedPeReaders = new(StringComparer.OrdinalIgnoreCase);

    private static bool IsManaged(string path)
    {
        try
        {
            using var reader = new PEReader(File.OpenRead(path));
            return reader.HasMetadata;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    public static async Task<ShapeOutcome> Run(Shape shape, string config, string work)
    {
        var outcome = new ShapeOutcome { Id = shape.Id, Config = config };
        await Probe(shape, config, work, outcome);
        outcome.Primary = Severity.FirstOrDefault(outcome.Flags.Contains) ?? (outcome.Flags.Count > 0 ? outcome.Flags[0] : "OK");
        return outcome;
    }

    private static async Task Probe(Shape shape, string config, string work, ShapeOutcome outcome)
    {
        var directory = Path.Combine(work, config);
        Directory.CreateDirectory(directory);
        var name = $"S_{new string(shape.Id.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray())}_{config}";
        var path = Path.Combine(directory, name + ".dll");
        var pdb = shape.Source.Contains("//PDB-NAMED") ? Path.Combine(directory, "custom-" + name + ".pdb")
            : shape.Source.Contains("//PDB-CASE") ? Path.Combine(directory, name.ToUpperInvariant() + ".pdb")
            : Path.ChangeExtension(path, ".pdb");

        var compile = Compile(name, shape, config.StartsWith("Release", StringComparison.Ordinal) ? OptimizationLevel.Release : OptimizationLevel.Debug, path, pdb, debugPlus: config == "ReleasePlus");
        if (compile is not null)
        {
            outcome.Flags.Add("COMPILE");
            outcome.Detail = compile;
            return;
        }

        // A file of the project's that bears the PDB's name, and is nothing to do with this assembly.
        var neighbour = shape.Source.Contains("//PDB-BESIDE-A-FILE") ? Path.ChangeExtension(pdb, ".dll") : null;
        if (neighbour != null)
            File.WriteAllText(neighbour, "a file of the project's");

        // What a project whose DebugType changed has in obj: the PDB of an earlier build, which is not this assembly's.
        if (shape.Source.Contains("//STALE-PDB"))
        {
            var earlier = Path.Combine(directory, "earlier");
            Directory.CreateDirectory(earlier);
            var earlierPath = Path.Combine(earlier, name + ".dll");
            var stale = Compile(name, shape, config.StartsWith("Release", StringComparison.Ordinal) ? OptimizationLevel.Release : OptimizationLevel.Debug, earlierPath, Path.ChangeExtension(earlierPath, ".pdb"), debugPlus: config == "ReleasePlus", appended: "\npublic class ChangedSince { }");
            if (stale is not null)
            {
                outcome.Flags.Add("COMPILE");
                outcome.Detail = stale;
                return;
            }
            File.Copy(Path.ChangeExtension(earlierPath, ".pdb"), pdb, overwrite: true);
        }

        if (shape.TailCall)
            AddTailPrefix(path, pdb);
        if (shape.BranchToRet)
            BranchToLastRet(path, pdb);

        var before = Verify(path);

        var engine = new FakeEngine();
        var log = new TaskLoggingHelper(new NamedTask { BuildEngine = engine });
        try
        {
            if (shape.Assertions)
            {
                // Both orders: a real build's order is the targets' import order.
                if (shape.AssertionsFirst)
                    NoteAssertions(new AssertionWeaver().Weave(path, pdb), outcome);
                ReadWeave(new StepWeaver(log).Weave(path, pdb), outcome);
                if (!shape.AssertionsFirst)
                    NoteAssertions(new AssertionWeaver().Weave(path, pdb), outcome);
            }
            else
            {
                // A reference something else has open for writing, as a parallel build sharing an output folder can.
                using var held = shape.Source.Contains("//HOLD-BASE")
                    ? new FileStream(Path.Combine(Path.GetDirectoryName(path)!, "FriendBase_" + Path.GetFileNameWithoutExtension(path) + ".dll"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)
                    : null;
                ReadWeave(new StepWeaver(log).Weave(path, pdb), outcome);
            }
        }
        catch (Exception ex)
        {
            outcome.Flags.Add("WEAVE-THROW");
            outcome.Detail = $"weaver threw {ex.GetType().Name}: {ex.Message}";
            return;
        }

        outcome.Warnings = string.Join(" | ", engine.Warnings);

        // The name the assembly gives its symbols is the one the runtime looks for.
        if (neighbour != null && (!File.Exists(neighbour) || File.ReadAllText(neighbour) != "a file of the project's"))
            outcome.Flags.Add("FILE-LOST");
        if (File.Exists(Path.ChangeExtension(pdb, ".kronikol-woven")))
            outcome.Flags.Add("LEFT-BEHIND");

        // A symbols file of its own name is named by that alone, exactly: a path would carry this machine's folder into a build with mapped paths, and another case finds nothing on Linux.
        if (shape.Source.Contains("//PDB-NAMED") || shape.Source.Contains("//PDB-CASE"))
        {
            using var written = new PEReader(new MemoryStream(File.ReadAllBytes(path)));
            var named = written.ReadDebugDirectory().Where(e => e.Type == DebugDirectoryEntryType.CodeView)
                .Select(e => written.ReadCodeViewDebugDirectoryData(e).Path).FirstOrDefault();
            if (named != Path.GetFileName(pdb))
            {
                outcome.Flags.Add("PDB-PATH");
                outcome.Detail = Append(outcome.Detail, $"the assembly names its symbols '{named}'");
            }
        }

        // Symbols the compiler embedded go back where they were, or the run has no line numbers.
        if (shape.Source.Contains("//EMBEDDED"))
        {
            using var woven = new PEReader(new MemoryStream(File.ReadAllBytes(path)));
            if (!woven.ReadDebugDirectory().Any(entry => entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb))
            {
                outcome.Flags.Add("SYMBOLS-LOST");
                outcome.Detail = Append(outcome.Detail, "the assembly no longer embeds its symbols");
            }
        }

        // An assembly the weave leaves open stays locked on a reused MSBuild node, and the next build cannot copy it.
        // A scanner can hold a new file for a moment, so it counts only if it stays held, as MSBuild's copy retries before it gives up.
        var friend = Path.Combine(Path.GetDirectoryName(path)!, "FriendBase_" + Path.GetFileNameWithoutExtension(path) + ".dll");
        if (File.Exists(friend))
        {
            var held = true;
            for (var attempt = 0; held && attempt < 50; attempt++)
            {
                try
                {
                    using var exclusive = File.Open(friend, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    held = false;
                }
                catch (IOException)
                {
                    await Task.Delay(100);
                }
            }

            if (held)
                outcome.Flags.Add("LOCKED");

            AssemblyLoadContext.Default.LoadFromAssemblyPath(friend);
        }

        // ILVerify before anything runs. An error the shape brings with it is allowed by name, and nothing else may be added by the weave.
        var after = Verify(path);
        var added = after.Except(before).ToList();
        outcome.IlVerify = string.Join(" | ", added);
        var badIl = added.Where(error => !shape.AllowIl.Any(allowed => error.EndsWith(allowed, StringComparison.Ordinal))).ToList();
        if (badIl.Count > 0)
        {
            outcome.Flags.Add("BAD-IL");
            outcome.Detail = Append(outcome.Detail, "the weave added: " + string.Join(", ", badIl));
            // A by-ref or a pointer in the wrong place kills the process rather than failing a fact, so nothing that does not verify is run.
            return;
        }

        Assembly assembly;
        try
        {
            assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        }
        catch (Exception ex)
        {
            outcome.Flags.Add("LOAD");
            outcome.Detail = $"{ex.GetType().Name}: {ex.Message}";
            return;
        }

        // JIT every step method as it stands, whichever path a run would take.
        foreach (var method in StepMethods(assembly))
        {
            try
            {
                RuntimeHelpers.PrepareMethod(method.MethodHandle);
            }
            catch (Exception ex) when (ex is InvalidProgramException or BadImageFormatException or TypeLoadException or MissingMethodException or MissingFieldException or MethodAccessException or FieldAccessException or System.Security.VerificationException)
            {
                outcome.Flags.Add("JIT");
                outcome.Detail = Append(outcome.Detail, $"JIT {method.Name}: {ex.GetType().Name}: {ex.Message}");
            }
            catch (ArgumentException)
            {
                // an open generic: the run below instantiates it
            }
        }

        var testId = $"{name}-{Guid.NewGuid():N}";
        using (TestIdentityScope.Begin(testId, testId))
        {
            object? value = null;
            Exception? thrown = null;
            try
            {
                var run = assembly.GetType("Driver")!.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
                var result = run.Invoke(null, null);
                if (result is Task task)
                {
                    var finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(30)));
                    if (finished != task)
                    {
                        outcome.Flags.Add("RUN-OTHER");
                        outcome.Detail = Append(outcome.Detail, "driver's task did not finish in 30 s");
                        return;
                    }
                    await task;
                    value = task.GetType().GetProperty("Result")?.GetValue(task);
                }
                else
                {
                    value = result;
                }
            }
            catch (TargetInvocationException ex)
            {
                thrown = ex.InnerException;
            }
            catch (Exception ex)
            {
                thrown = ex;
            }

            outcome.Value = value?.ToString();
            outcome.Threw = thrown?.GetType().Name;

            // A step that closes after the driver returns (an async void body) gets its moment.
            await Task.Delay(shape.SettleMs);

            var steps = StepCollector.GetSteps(testId);
            outcome.Tree = string.Join(",", steps.Select(Render));
            outcome.TopMs = (int)(steps.FirstOrDefault(s => s.Text == "M")?.Duration?.TotalMilliseconds ?? -1);
            outcome.Open = StepCollector.HasActiveStep(testId);

            if (thrown is InvalidProgramException)
            {
                outcome.Flags.Add("RUN-IPE");
                outcome.Detail = Append(outcome.Detail, "InvalidProgramException at the call: " + thrown.StackTrace?.Split('\n').FirstOrDefault()?.Trim());
            }
            else if (shape.Throws is null && thrown is not null)
            {
                outcome.Flags.Add("RUN-OTHER");
                outcome.Detail = Append(outcome.Detail, $"threw {thrown.GetType().Name}: {thrown.Message}");
            }
            else if (shape.Throws is not null && thrown?.GetType().Name != shape.Throws)
            {
                outcome.Flags.Add("THROWS");
                outcome.Detail = Append(outcome.Detail, $"expected {shape.Throws}, threw {thrown?.GetType().Name ?? "nothing"}");
            }

            if (thrown is not InvalidProgramException)
            {
                if (shape.Value is not null && outcome.Value != shape.Value)
                {
                    outcome.Flags.Add("VALUE");
                    outcome.Detail = Append(outcome.Detail, $"value '{outcome.Value}', expected '{shape.Value}'");
                }

                if (shape.Tree is not null && outcome.Tree != shape.Tree)
                {
                    outcome.Flags.Add("TREE");
                    outcome.Detail = Append(outcome.Detail, $"tree '{outcome.Tree}', expected '{shape.Tree}'");
                }

                if (outcome.Open)
                {
                    outcome.Flags.Add("OPEN");
                    outcome.Detail = Append(outcome.Detail, "a step was left open");
                }

                if (shape.MinMs > 0 && outcome.TopMs < shape.MinMs && !shape.Refused)
                {
                    outcome.Flags.Add("EARLY");
                    outcome.Detail = Append(outcome.Detail, $"step closed after {outcome.TopMs} ms, its work took at least {shape.MinMs}");
                }
            }

            // Listed as left as written where it should not be: the build would warn about a method that runs as it should.
            if (!shape.Refused && !string.IsNullOrEmpty(outcome.Unwoven))
            {
                outcome.Flags.Add("WRONGLY-REFUSED");
                outcome.Detail = Append(outcome.Detail, $"listed as left as written: {outcome.Unwoven}");
            }

            // An assertion that ran with no note and no word from the build.
            if (shape.Assertions && string.IsNullOrEmpty(shape.Tree) && !(outcome.Tree ?? "").Contains("~assert") && !(outcome.Detail ?? "").Contains("ASSERTION-UNWOVEN") && !(outcome.Tree ?? "").Contains("Bypassed") && string.IsNullOrEmpty(outcome.Threw))
            {
                outcome.Flags.Add("ASSERTION-LOST");
                outcome.Detail = Append(outcome.Detail, "an assertion ran with no note, and the build said nothing");
            }

            if (shape.Refused && string.IsNullOrEmpty(outcome.Unwoven))
            {
                outcome.Flags.Add("NOT-REFUSED");
                outcome.Detail = Append(outcome.Detail, "the weave did not list this method as left unwoven");
            }

            if (shape.NoWarning && !string.IsNullOrEmpty(outcome.Warnings))
            {
                outcome.Flags.Add("WARNING");
                outcome.Detail = Append(outcome.Detail, $"expected no build warning, got '{outcome.Warnings}'");
            }
            if (shape.ExpectWarning is not null && !(outcome.Warnings + " " + outcome.Unwoven).Contains(shape.ExpectWarning, StringComparison.OrdinalIgnoreCase))
            {
                outcome.Flags.Add("WARNING");
                outcome.Detail = Append(outcome.Detail, $"expected a build warning containing '{shape.ExpectWarning}', got '{outcome.Warnings}'");
            }
        }
    }

    // What F# writes for a call in tail position, which C# does not: `tail. call X; ret`.
    private static void AddTailPrefix(string path, string pdb) => Rewrite(path, pdb, method =>
    {
        var il = method.Body.GetILProcessor();
        var call = method.Body.Instructions.FirstOrDefault(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Call && i.Next?.OpCode == Mono.Cecil.Cil.OpCodes.Ret);
        if (call != null)
            il.InsertBefore(call, il.Create(Mono.Cecil.Cil.OpCodes.Tail));
    });

    // Roslyn copies a ret rather than branch to one with a value on the stack. F# and an IL rewriter need not: this is their shape.
    private static void BranchToLastRet(string path, string pdb) => Rewrite(path, pdb, method =>
    {
        var rets = method.Body.Instructions.Where(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ret).ToList();
        foreach (var ret in rets.Take(rets.Count - 1))
        {
            ret.OpCode = Mono.Cecil.Cil.OpCodes.Br;
            ret.Operand = rets[^1];
        }
    });

    private static void Rewrite(string path, string pdb, Action<Mono.Cecil.MethodDefinition> change)
    {
        byte[] output;
        byte[] symbols;
        using (var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(new MemoryStream(File.ReadAllBytes(path)), new Mono.Cecil.ReaderParameters { ReadSymbols = true, SymbolStream = new MemoryStream(File.ReadAllBytes(pdb)) }))
        {
            foreach (var type in assembly.MainModule.Types)
            foreach (var method in type.Methods.Where(m => m.HasBody && m.Name == "M"))
                change(method);
            using var a = new MemoryStream();
            using var b = new MemoryStream();
            assembly.Write(a, new Mono.Cecil.WriterParameters { WriteSymbols = true, SymbolWriterProvider = new Mono.Cecil.Cil.PortablePdbWriterProvider(), SymbolStream = b });
            output = a.ToArray();
            symbols = b.ToArray();
        }
        File.WriteAllBytes(path, output);
        File.WriteAllBytes(pdb, symbols);
    }

    private static void NoteAssertions(Kronikol.AssertionTracking.WeaveResult result, ShapeOutcome outcome)
    {
        var text = string.Join(" | ", result.UnwovenStatements);
        if (text.Length > 0)
            outcome.Detail = Append(outcome.Detail, "ASSERTION-UNWOVEN: " + text);
    }

    private static string Append(string? detail, string more) => string.IsNullOrEmpty(detail) ? more : detail + "; " + more;

    private static void ReadWeave(Kronikol.StepTracking.WeaveResult result, ShapeOutcome outcome)
    {
        outcome.Weaved = result.WeavedCount;
        outcome.Unwoven = string.Join(" | ", result.UnwovenSteps);
    }

    public static string Render(ScenarioStep step)
    {
        var text = new StringBuilder(step.IsAssertion ? "~" : "");
        text.Append(step.IsAssertion ? "assert" : step.Text);
        if (step.Parameters is { Length: > 0 } parameters)
            text.Append('(').Append(string.Join(",", parameters.Select(p => $"{p.Name}={p.InlineValue?.Value}"))).Append(')');
        text.Append(':').Append(step.Status);
        if (step.SubSteps is { Length: > 0 } children)
            text.Append('[').Append(string.Join(",", children.Select(Render))).Append(']');
        return text.ToString();
    }

    private static IEnumerable<MethodBase> StepMethods(Assembly assembly)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsGenericTypeDefinition)
                continue;
            foreach (var method in type.GetMethods(all))
            {
                if (method.IsAbstract || method.IsGenericMethodDefinition)
                    continue;
                if (method.CustomAttributes.Any(a => a.AttributeType.Name.EndsWith("StepAttribute", StringComparison.Ordinal)))
                    yield return method;
            }
        }
    }

    // ---- compiling ----

    private static string? Compile(string name, Shape shape, OptimizationLevel optimization, string path, string pdb, bool debugPlus = false, string appended = "")
    {
        var parse = new CSharpParseOptions(LanguageVersion.Latest);
        if (shape.Source.Contains("//RUNTIME-ASYNC"))
            parse = parse.WithFeatures([new KeyValuePair<string, string>("runtime-async", "on")]);
        var trees = new[]
        {
            CSharpSyntaxTree.ParseText(shape.Source.Replace("BASENAME_CONFIG", "FriendBase_" + name) + appended, parse, path: Path.ChangeExtension(path, ".cs"), encoding: Encoding.UTF8),
            CSharpSyntaxTree.ParseText(Shapes.Attributes, parse),
        };
        if (shape.Source.Contains("//IACT"))
            trees = trees.Append(CSharpSyntaxTree.ParseText($$"""
                [assembly: System.Runtime.CompilerServices.IgnoresAccessChecksTo("FriendBase_{{name}}")]
                namespace System.Runtime.CompilerServices { [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)] public sealed class IgnoresAccessChecksToAttribute : System.Attribute { public IgnoresAccessChecksToAttribute(string n) { } } }
                """, parse)).ToArray();
        File.WriteAllText(Path.ChangeExtension(path, ".cs"), shape.Source, Encoding.UTF8);

        var references = References.Value.ToList();
        var baseMatch = System.Text.RegularExpressions.Regex.Match(shape.Source, "//BASE:(.*)");
        if (baseMatch.Success)
        {
            var baseName = "FriendBase_" + name;
            var baseSource = baseMatch.Groups[1].Value.Replace("S_Z03c_ivt_internal_base_CONFIG", name).Replace("LOWERNAME_CONFIG", name.ToLowerInvariant());
            var basePath = Path.Combine(Path.GetDirectoryName(path)!, baseName + ".dll");
            var baseCompilation = CSharpCompilation.Create(baseName, [CSharpSyntaxTree.ParseText(baseSource, parse)], references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
            var baseEmit = baseCompilation.Emit(basePath);
            if (!baseEmit.Success)
                return "BASE: " + string.Join("; ", baseEmit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            references.Add(MetadataReference.CreateFromFile(basePath));
        }

        var compilation = CSharpCompilation.Create(name, trees, references,
            (debugPlus
                ? CSharpCommandLineParser.Default.Parse(["/debug+", "/debug:portable", "/optimize+", "/target:library", "/unsafe", "/nullable:disable"], Path.GetDirectoryName(path)!, null).CompilationOptions
                : new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, optimizationLevel: optimization, nullableContextOptions: NullableContextOptions.Disable)).WithSpecificDiagnosticOptions([new KeyValuePair<string, ReportDiagnostic>("SYSLIB5007", ReportDiagnostic.Suppress)]));

        using (var pe = File.Create(path))
        using (var symbols = File.Create(pdb))
        {
            // The earlier build of //STALE-PDB is the portable one, and only this build's symbols are embedded.
            var embedded = shape.Source.Contains("//EMBEDDED") && appended.Length == 0;
            var emitted = embedded
                ? compilation.Emit(pe, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.Embedded))
                : compilation.Emit(pe, symbols, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
            if (!emitted.Success)
                return string.Join("; ", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()));
        }
        return null;
    }

    // ---- verifying ----

    private static List<string> Verify(string path)
    {
        var verifier = new Verifier(new Resolver(path), new VerifierOptions { IncludeMetadataTokensInErrorMessages = false });
        verifier.SetSystemModuleName(new AssemblyNameInfo("System.Private.CoreLib"));
        var found = new List<string>();
        using var pe = new PEReader(ImmutableArray.Create(File.ReadAllBytes(path)));
        var metadata = pe.GetMetadataReader();
        foreach (var result in verifier.Verify(pe))
        {
            var method = result.Method.IsNil ? "?" : metadata.GetString(metadata.GetMethodDefinition(result.Method).Name);
            found.Add($"{method}: {result.Code}");
        }
        return found.Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
    }

    private sealed class Resolver(string self) : IResolver
    {
        private readonly Dictionary<string, PEReader?> _own = new(StringComparer.OrdinalIgnoreCase);

        public PEReader? ResolveAssembly(AssemblyNameInfo assemblyName)
        {
            var simple = assemblyName.Name!;
            if (_own.TryGetValue(simple, out var known))
                return known;
            if (string.Equals(simple, Path.GetFileNameWithoutExtension(self), StringComparison.OrdinalIgnoreCase))
                return _own[simple] = new PEReader(ImmutableArray.Create(File.ReadAllBytes(self)));
            var beside = Path.Combine(Path.GetDirectoryName(self)!, simple + ".dll");
            if (File.Exists(beside))
                return _own[simple] = new PEReader(ImmutableArray.Create(File.ReadAllBytes(beside)));

            var file = simple switch
            {
                "Kronikol" => typeof(Track).Assembly.Location,
                "Shouldly" => typeof(Shouldly.ShouldAssertException).Assembly.Location,
                _ => ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                    .FirstOrDefault(p => string.Equals(Path.GetFileNameWithoutExtension(p), simple, StringComparison.OrdinalIgnoreCase) && File.Exists(p)),
            };
            return _own[simple] = file is null ? null : SharedPeReaders.GetOrAdd(file, f => new PEReader(ImmutableArray.Create(File.ReadAllBytes(f))));
        }

        public PEReader? ResolveModule(AssemblyNameInfo referencingAssembly, string fileName) => null;
    }

    private sealed class NamedTask : Microsoft.Build.Utilities.Task
    {
        public override bool Execute() => true;
    }

    private sealed class FakeEngine : IBuildEngine
    {
        public List<string> Warnings { get; } = [];
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => "";
        public bool BuildProjectFile(string projectFileName, string[] targetNames, System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs) => true;
        public void LogCustomEvent(CustomBuildEventArgs e) { }
        public void LogErrorEvent(BuildErrorEventArgs e) => Warnings.Add("ERROR " + e.Code + " " + e.Message);
        public void LogMessageEvent(BuildMessageEventArgs e) { }
        public void LogWarningEvent(BuildWarningEventArgs e) => Warnings.Add((e.Code is { Length: > 0 } c ? c + " " : "") + e.Message);
    }
}

/// <summary>What one shape did in one build. Serialized when a shape ran in a process of its own.</summary>
public sealed class ShapeOutcome
{
    public string Id { get; set; } = "";
    public string Config { get; set; } = "";
    public string Primary { get; set; } = "";
    public List<string> Flags { get; set; } = [];
    public string? Value { get; set; }
    public string? Threw { get; set; }
    public string? Tree { get; set; }
    public int TopMs { get; set; } = -1;
    public bool Open { get; set; }
    public int Weaved { get; set; }
    public string? Unwoven { get; set; }
    public string? IlVerify { get; set; }
    public string? Warnings { get; set; }
    public string? Detail { get; set; }
}

/// <summary>A method shape and what it should do: its source, its driver's value, what it should throw, the step tree it should draw.</summary>
internal sealed class Shape
{
    public required string Id { get; init; }
    public required string Family { get; init; }
    public required string Source { get; init; }
    public string? Value { get; init; }
    public string? Throws { get; init; }
    public string? Tree { get; init; }
    public int MinMs { get; init; }
    public bool Refused { get; init; }
    public string? ExpectWarning { get; init; }
    public bool NoWarning { get; init; }
    public bool Assertions { get; init; }
    public bool AssertionsFirst { get; init; }
    public int SettleMs { get; init; }
    public bool TailCall { get; init; }
    public bool BranchToRet { get; init; }

    // ILVerify errors that come with the shape and not the weave: it refuses a byref or a by-ref-like value returned from
    // a local, which is what the C# compiler's own Debug build writes for such a method, and it never verifies a pointer.
    public string[] AllowIl { get; init; } = [];
}
