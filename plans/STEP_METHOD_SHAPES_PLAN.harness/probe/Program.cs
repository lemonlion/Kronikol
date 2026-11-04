// The shape probe of plans/STEP_METHOD_SHAPES_PLAN.md.
//
// dotnet run -c Release --project probe.csproj -- all <label> <out dir> [id filter]
//
// For each shape in each build configuration, in a child process of its own (a shape can kill the process):
//   1. compile it with Roslyn, 2. IL-verify it as compiled, 3. weave it with this worktree's weaver,
//   4. IL-verify it as woven and keep the errors weaving added, 5. JIT each step method (RuntimeHelpers.PrepareMethod),
//   6. run its driver and compare the value, the exception, the step tree, the time inside the step and any step left
//      open with what the shape says should happen.
// The result is one tab-separated line per shape and configuration, and a summary by family and outcome.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using ILVerify;
using Kronikol.AssertionTracking;
using Kronikol.Reports;
using Kronikol.StepTracking;
using Kronikol.Tracking;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Task = System.Threading.Tasks.Task;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace StepShapes;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length >= 4 && args[0] == "one")
            return await One(args[1], args[2], args[3]);
        if (args.Length >= 3 && args[0] == "all")
            return await All(args[1], args[2], args.Length > 3 ? args[3] : null);
        if (args.Length >= 1 && args[0] == "list")
        {
            foreach (var shape in Shapes.All)
                Console.WriteLine($"{shape.Id}\t{shape.Family}");
            return 0;
        }
        Console.Error.WriteLine("usage: all <label> <out dir> [id prefix filter] | one <id> <Debug|Release> <work dir> | list");
        return 2;
    }

    // ---- parent: one child per shape and configuration ----

    private static async Task<int> All(string label, string outDir, string? filter)
    {
        Directory.CreateDirectory(outDir);
        var work = Path.Combine(Path.GetTempPath(), "kronikol-226-probe", "run", label);
        if (Directory.Exists(work))
            Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);

        var shapes = Shapes.All.Where(s => filter is null || s.Id.StartsWith(filter, StringComparison.Ordinal)).ToList();
        var duplicates = Shapes.All.GroupBy(s => s.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
            throw new InvalidOperationException("Duplicate shape ids: " + string.Join(", ", duplicates));

        var self = Environment.ProcessPath!;
        var selfArgs = Path.GetFileName(self).StartsWith("dotnet", StringComparison.OrdinalIgnoreCase)
            ? $"\"{Assembly.GetEntryAssembly()!.Location}\" "
            : "";
        var results = new ConcurrentBag<Outcome>();
        var clock = Stopwatch.StartNew();
        // PROBE_CONFIGS=ReleasePlus compiles as a project whose DebugSymbols is true does in Release, /debug+, which xUnit v2 turns on.
        var configs_ = (Environment.GetEnvironmentVariable("PROBE_CONFIGS") ?? "Debug,Release").Split(',');
        var jobs = shapes.SelectMany(s => configs_.Select(c => (Shape: s, Config: c))).ToList();
        await Parallel.ForEachAsync(jobs, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (job, ct) =>
        {
            results.Add(await RunChild(self, selfArgs, job.Shape, job.Config, work));
        });

        var ordered = results.OrderBy(r => r.Id, StringComparer.Ordinal).ThenBy(r => r.Config, StringComparer.Ordinal).ToList();
        var lines = new List<string> { "id\tconfig\tfamily\toutcome\tall\tvalue\tthrew\ttree\tms\topen\tweaved\tunwoven\tilverify\twarnings\tdetail" };
        lines.AddRange(ordered.Select(r => string.Join('\t', r.Id, r.Config, r.Family, r.Primary, string.Join('+', r.Flags),
            Clean(r.Value), Clean(r.Threw), Clean(r.Tree), r.TopMs, r.Open, r.Weaved, Clean(r.Unwoven), Clean(r.IlVerify), Clean(r.Warnings), Clean(r.Detail))));
        File.WriteAllLines(Path.Combine(outDir, $"matrix-{label}.tsv"), lines, new UTF8Encoding(false));

        var summary = new StringBuilder();
        summary.AppendLine($"label: {label}");
        summary.AppendLine($"weaver: {typeof(StepWeaver).Assembly.Location}");
        summary.AppendLine($"runtime: {Environment.Version}");
        summary.AppendLine($"shapes: {shapes.Count}, runs: {ordered.Count}, seconds: {clock.Elapsed.TotalSeconds:F0}");
        summary.AppendLine();
        summary.AppendLine("by outcome:");
        foreach (var group in ordered.GroupBy(r => r.Primary).OrderByDescending(g => g.Count()))
            summary.AppendLine($"  {group.Key,-12} {group.Count()}");
        summary.AppendLine();
        summary.AppendLine("by family (runs ok / runs):");
        foreach (var family in ordered.GroupBy(r => r.Family).OrderBy(g => g.Key, StringComparer.Ordinal))
            summary.AppendLine($"  {family.Key,-34} {family.Count(r => r.Primary == "OK"),4} / {family.Count(),4}");
        summary.AppendLine();
        summary.AppendLine("not ok, one line per shape (the two configurations together where they agree):");
        foreach (var group in ordered.Where(r => r.Primary != "OK").GroupBy(r => r.Id))
        {
            var first = group.First();
            var configs = group.Count() == 2 ? "both   " : first.Config.PadRight(7);
            summary.AppendLine($"  {first.Id,-32} {configs} {first.Primary,-11} {Trim(first.Detail, 170)}");
        }
        File.WriteAllText(Path.Combine(outDir, $"summary-{label}.txt"), summary.ToString(), new UTF8Encoding(false));
        Console.WriteLine(summary);
        return 0;
    }

    private static string Clean(string? text) =>
        (text ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private static string Trim(string? text, int length)
    {
        var cleaned = Clean(text);
        return cleaned.Length <= length ? cleaned : cleaned[..length] + "...";
    }

    private static async Task<Outcome> RunChild(string self, string selfArgs, Shape shape, string config, string work)
    {
        var start = new ProcessStartInfo(self, $"{selfArgs}one \"{shape.Id}\" {config} \"{work}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // Roslyn's runtime-async feature needs the runtime's switch to run at all.
        if (shape.Source.Contains("//RUNTIME-ASYNC"))
            start.Environment["DOTNET_RuntimeAsync"] = "1";
        using var child = Process.Start(start)!;
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            await child.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            child.Kill(entireProcessTree: true);
            return new Outcome { Id = shape.Id, Config = config, Family = shape.Family, Primary = "HANG", Flags = ["HANG"], Detail = "no result in 90 s" };
        }

        var output = await stdout;
        var marker = output.LastIndexOf("<<<OUTCOME>>>", StringComparison.Ordinal);
        if (marker >= 0)
            return JsonSerializer.Deserialize<Outcome>(output[(marker + "<<<OUTCOME>>>".Length)..])!;

        // The process died before it could report: a fatal error in woven code.
        var error = await stderr;
        return new Outcome
        {
            Id = shape.Id, Config = config, Family = shape.Family, Primary = "CRASH", Flags = ["CRASH"],
            Detail = $"exit 0x{child.ExitCode:X8}: {Trim(error, 300)}",
        };
    }

    // ---- child: one shape in one configuration ----

    private static async Task<int> One(string id, string config, string work)
    {
        var shape = Shapes.All.Single(s => s.Id == id);
        var outcome = new Outcome { Id = id, Config = config, Family = shape.Family };
        try
        {
            await Probe(shape, config, work, outcome);
        }
        catch (Exception ex)
        {
            outcome.Flags.Add("PROBE");
            outcome.Detail = "probe failed: " + ex;
        }

        outcome.Primary = Severity.FirstOrDefault(outcome.Flags.Contains) ?? "OK";
        Console.WriteLine("<<<OUTCOME>>>" + JsonSerializer.Serialize(outcome));
        await Console.Out.FlushAsync();
        // No finalizers or background continuations of a broken shape get to change the verdict.
        Environment.Exit(0);
        return 0;
    }

    private static readonly string[] Severity =
        ["PROBE", "COMPILE", "WEAVE-THROW", "LOCKED", "JIT", "RUN-IPE", "LOAD", "RUN-OTHER", "VALUE", "THROWS", "OPEN", "TREE", "EARLY", "NOT-REFUSED", "WRONGLY-REFUSED", "ASSERTION-LOST", "SYMBOLS-LOST", "FILE-LOST", "LEFT-BEHIND", "PDB-PATH", "BAD-IL", "WARNING"];

    private static async Task Probe(Shape shape, string config, string work, Outcome outcome)
    {
        var directory = Path.Combine(work, config);
        Directory.CreateDirectory(directory);
        var name = $"S_{new string(shape.Id.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray())}_{config}";
        var path = Path.Combine(directory, name + ".dll");
        var pdb = shape.Source.Contains("//PDB-NAMED") ? Path.Combine(directory, "custom-" + name + ".pdb")
            : shape.Source.Contains("//PDB-CASE") ? Path.Combine(directory, name.ToUpperInvariant() + ".pdb")
            : Path.ChangeExtension(path, ".pdb");

        var compile = Compile(name, shape, config.StartsWith("Release") ? OptimizationLevel.Release : OptimizationLevel.Debug, path, pdb, debugPlus: config == "ReleasePlus");
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
            var stale = Compile(name, shape, config.StartsWith("Release") ? OptimizationLevel.Release : OptimizationLevel.Debug, earlierPath, Path.ChangeExtension(earlierPath, ".pdb"), debugPlus: config == "ReleasePlus", appended: "\npublic class ChangedSince { }");
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
                // Both orders: a real build's order is the targets' import order (the J probes read it).
                if (shape.AssertionsFirst)
                    NoteAssertions(new AssertionWeaver().Weave(path, pdb), outcome);
                var result = new StepWeaver(log).Weave(path, pdb);
                ReadWeave(result, outcome);
                if (!shape.AssertionsFirst)
                    NoteAssertions(new AssertionWeaver().Weave(path, pdb), outcome);
            }
            else
            {
                // A reference something else has open for writing, as a parallel build sharing an output folder can.
                using var held = shape.Source.Contains("//HOLD-BASE")
                    ? new FileStream(Path.Combine(Path.GetDirectoryName(path)!, "FriendBase_" + Path.GetFileNameWithoutExtension(path) + ".dll"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)
                    : null;
                var result = new StepWeaver(log).Weave(path, pdb);
                ReadWeave(result, outcome);
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
            using var written = new System.Reflection.PortableExecutable.PEReader(new MemoryStream(File.ReadAllBytes(path)));
            var named = written.ReadDebugDirectory().Where(e => e.Type == System.Reflection.PortableExecutable.DebugDirectoryEntryType.CodeView)
                .Select(e => written.ReadCodeViewDebugDirectoryData(e).Path).FirstOrDefault();
            if (named != Path.GetFileName(pdb))
            {
                outcome.Flags.Add("PDB-PATH");
                outcome.Detail += $"the assembly names its symbols '{named}'; ";
            }
        }

        if (shape.Source.Contains("//PDB-NAMED"))
        {
            using var named = new System.Reflection.PortableExecutable.PEReader(File.OpenRead(path));
            var view = named.ReadDebugDirectory().Where(entry => entry.Type == System.Reflection.PortableExecutable.DebugDirectoryEntryType.CodeView)
                .Select(entry => named.ReadCodeViewDebugDirectoryData(entry).Path).FirstOrDefault();
            if (Path.GetFileName(view) != Path.GetFileName(pdb))
            {
                outcome.Flags.Add("SYMBOLS-LOST");
                outcome.Detail += $"the assembly names its symbols {view}; ";
            }
        }

        // Symbols the compiler embedded go back where they were, or the run has no line numbers.
        if (shape.Source.Contains("//EMBEDDED"))
        {
            using var woven = new System.Reflection.PortableExecutable.PEReader(File.OpenRead(path));
            if (!woven.ReadDebugDirectory().Any(entry => entry.Type == System.Reflection.PortableExecutable.DebugDirectoryEntryType.EmbeddedPortablePdb))
            {
                outcome.Flags.Add("SYMBOLS-LOST");
                outcome.Detail += "the assembly no longer embeds its symbols; ";
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
                    Thread.Sleep(100);
                }
            }

            if (held)
                outcome.Flags.Add("LOCKED");

            System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(friend);
        }

        var after = Verify(path);
        var added = after.Except(before).ToList();
        outcome.IlVerify = string.Join(" | ", added);
        // Every woven assembly on main carries this one (the weaver's sentinel attribute: finding F9 in the plan), so it
        // is listed in its own column and does not mark every shape.
        var strict = Environment.GetEnvironmentVariable("STRICT_SENTINEL") == "1";
        if (added.Any(error => (strict || error != ".ctor: ThisUninitReturn") && !shape.AllowIl.Any(allowed => error.EndsWith(allowed, StringComparison.Ordinal))))
            outcome.Flags.Add("BAD-IL");

        Assembly assembly;
        try
        {
            assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
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
                outcome.Detail += $"listed as left as written: {outcome.Unwoven}; ";
            }

            // An assertion that ran with no note and no word from the build.
            if (shape.Assertions && string.IsNullOrEmpty(shape.Tree) && !(outcome.Tree ?? "").Contains("~assert") && !(outcome.Detail ?? "").Contains("ASSERTION-UNWOVEN") && !(outcome.Tree ?? "").Contains("Bypassed") && string.IsNullOrEmpty(outcome.Threw))
            {
                outcome.Flags.Add("ASSERTION-LOST");
                outcome.Detail += "an assertion ran with no note, and the build said nothing; ";
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
    // Roslyn copies a ret rather than branch to one with a value on the stack. F# and an IL rewriter need not: this is their shape.
    private static void BranchToLastRet(string path, string pdb)
    {
        byte[] output;
        byte[] symbols;
        using (var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(new MemoryStream(File.ReadAllBytes(path)), new Mono.Cecil.ReaderParameters { ReadSymbols = true, SymbolStream = new MemoryStream(File.ReadAllBytes(pdb)) }))
        {
            foreach (var type in assembly.MainModule.Types)
            foreach (var method in type.Methods.Where(m => m.HasBody && m.Name == "M"))
            {
                var rets = method.Body.Instructions.Where(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ret).ToList();
                foreach (var ret in rets.Take(rets.Count - 1))
                {
                    ret.OpCode = Mono.Cecil.Cil.OpCodes.Br;
                    ret.Operand = rets[^1];
                }
            }
            using var a = new MemoryStream();
            using var b = new MemoryStream();
            assembly.Write(a, new Mono.Cecil.WriterParameters { WriteSymbols = true, SymbolWriterProvider = new Mono.Cecil.Cil.PortablePdbWriterProvider(), SymbolStream = b });
            output = a.ToArray();
            symbols = b.ToArray();
        }
        File.WriteAllBytes(path, output);
        File.WriteAllBytes(pdb, symbols);
    }

    private static void AddTailPrefix(string path, string pdb)
    {
        byte[] output;
        byte[] symbols;
        using (var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(new MemoryStream(File.ReadAllBytes(path)), new Mono.Cecil.ReaderParameters { ReadSymbols = true, SymbolStream = new MemoryStream(File.ReadAllBytes(pdb)) }))
        {
            foreach (var type in assembly.MainModule.Types)
            foreach (var method in type.Methods.Where(m => m.HasBody && m.Name == "M"))
            {
                var il = method.Body.GetILProcessor();
                var call = method.Body.Instructions.FirstOrDefault(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Call && i.Next?.OpCode == Mono.Cecil.Cil.OpCodes.Ret);
                if (call != null)
                    il.InsertBefore(call, il.Create(Mono.Cecil.Cil.OpCodes.Tail));
            }
            using var a = new MemoryStream();
            using var b = new MemoryStream();
            assembly.Write(a, new Mono.Cecil.WriterParameters { WriteSymbols = true, SymbolWriterProvider = new Mono.Cecil.Cil.PortablePdbWriterProvider(), SymbolStream = b });
            output = a.ToArray();
            symbols = b.ToArray();
        }
        File.WriteAllBytes(path, output);
        File.WriteAllBytes(pdb, symbols);
    }

    private static void NoteAssertions(object result, Outcome outcome)
    {
        var list = result.GetType().GetProperty("UnwovenStatements")?.GetValue(result) as System.Collections.IEnumerable;
        if (list != null)
        {
            var text = string.Join(" | ", list.Cast<object>());
            if (text.Length > 0)
                outcome.Detail = Append(outcome.Detail, "ASSERTION-UNWOVEN: " + text);
        }
    }

    private static string Append(string? detail, string more) => string.IsNullOrEmpty(detail) ? more : detail + "; " + more;

    private static void ReadWeave(object result, Outcome outcome)
    {
        outcome.Weaved = (int)result.GetType().GetProperty("WeavedCount")!.GetValue(result)!;
        // The prototype's list of the methods it left as written; the weaver on main has no such list.
        if (result.GetType().GetProperty("UnwovenSteps")?.GetValue(result) is System.Collections.IEnumerable unwoven)
            outcome.Unwoven = string.Join(" | ", unwoven.Cast<object>());
    }

    private static string Render(ScenarioStep step)
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
            parse = parse.WithFeatures(new[] { new KeyValuePair<string, string>("runtime-async", "on") });
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

        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
            references.Add(file);
        references.Add(typeof(Track).Assembly.Location);
        references.Add(typeof(Shouldly.ShouldAssertException).Assembly.Location);
        var baseMatch = System.Text.RegularExpressions.Regex.Match(shape.Source, "//BASE:(.*)");
        if (baseMatch.Success)
        {
            var baseName = "FriendBase_" + name;
            var baseSource = baseMatch.Groups[1].Value.Replace("S_Z03c_ivt_internal_base_CONFIG", name).Replace("LOWERNAME_CONFIG", name.ToLowerInvariant());
            var basePath = Path.Combine(Path.GetDirectoryName(path)!, baseName + ".dll");
            var baseCompilation = CSharpCompilation.Create(baseName, new[] { CSharpSyntaxTree.ParseText(baseSource, parse) },
                references.Where(File.Exists).Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
            var baseEmit = baseCompilation.Emit(basePath);
            if (!baseEmit.Success)
                return "BASE: " + string.Join("; ", baseEmit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            references.Add(basePath);
        }

        var compilation = CSharpCompilation.Create(name, trees,
            references.Where(File.Exists).Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)),
            (debugPlus
                ? CSharpCommandLineParser.Default.Parse(new[] { "/debug+", "/debug:portable", "/optimize+", "/target:library", "/unsafe", "/nullable:disable" }, Path.GetDirectoryName(path)!, null).CompilationOptions
                : new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, optimizationLevel: optimization, nullableContextOptions: NullableContextOptions.Disable)).WithSpecificDiagnosticOptions(new[] { new KeyValuePair<string, ReportDiagnostic>("SYSLIB5007", ReportDiagnostic.Suppress) }));

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
        var resolver = new Resolver(path);
        var verifier = new Verifier(resolver, new VerifierOptions { IncludeMetadataTokensInErrorMessages = false });
        verifier.SetSystemModuleName(new AssemblyNameInfo("System.Private.CoreLib"));
        var found = new List<string>();
        using var pe = new PEReader(File.ReadAllBytes(path).ToImmutableBytes());
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
        private readonly Dictionary<string, PEReader?> _cache = new(StringComparer.OrdinalIgnoreCase);

        public PEReader? ResolveAssembly(AssemblyNameInfo assemblyName)
        {
            var simple = assemblyName.Name;
            if (_cache.TryGetValue(simple, out var known))
                return known;
            string? file = null;
            if (string.Equals(simple, Path.GetFileNameWithoutExtension(self), StringComparison.OrdinalIgnoreCase))
                file = self;
            else if (simple == "Kronikol")
                file = typeof(Track).Assembly.Location;
            else if (simple == "Shouldly")
                file = typeof(Shouldly.ShouldAssertException).Assembly.Location;
            else
                file = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                    .Concat(new[] { Path.Combine(Path.GetDirectoryName(self)!, simple + ".dll") })
                    .FirstOrDefault(p => string.Equals(Path.GetFileNameWithoutExtension(p), simple, StringComparison.OrdinalIgnoreCase) && File.Exists(p));
            var reader = file is null ? null : new PEReader(File.ReadAllBytes(file).ToImmutableBytes());
            _cache[simple] = reader;
            return reader;
        }

        public PEReader? ResolveModule(AssemblyNameInfo referencingAssembly, string fileName) => null;
    }

    private static System.Collections.Immutable.ImmutableArray<byte> ToImmutableBytes(this byte[] bytes) =>
        System.Collections.Immutable.ImmutableArray.Create(bytes);

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

public sealed class Outcome
{
    public string Id { get; set; } = "";
    public string Config { get; set; } = "";
    public string Family { get; set; } = "";
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

public sealed class Shape
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
