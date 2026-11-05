using System.Diagnostics;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Kronikol.Tracking;

namespace Kronikol.Tests.StepTracking;

/// <summary>
/// The two weavers' props and targets run by a real <c>dotnet build</c> (STEP_METHOD_SHAPES_PLAN section 9.2): what a project's
/// settings do to a weave, and what the build says of what it left as written. The fixture imports the targets as a package reference
/// would, with each task loaded from this test's build of it, and is loaded into this process to be run, so that it draws into the
/// collector the fact reads.
/// </summary>
public class StepBuildTests
{
    private static readonly string Tfm = $"net{Environment.Version.Major}.0";

    private static readonly string Sources = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src"));
    private static readonly string StepBuild = Path.Combine(Sources, "Kronikol.StepTracking", "build");
    private static readonly string AssertionBuild = Path.Combine(Sources, "Kronikol.AssertionTracking", "build");

    private static readonly string Root = Path.Combine(Path.GetTempPath(), "KRONIKOL_StepBuilds", Guid.NewGuid().ToString("N")[..8]);

    // The tasks as MSBuild loads them from a package's tasks/ folder: the assembly and Mono.Cecil beside it.
    private static readonly Lazy<string> TaskDirectory = new(() =>
    {
        var directory = Path.Combine(Root, "tasks");
        Directory.CreateDirectory(directory);
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll")
                     .Where(f => Path.GetFileName(f) is var n && (n is "Kronikol.StepTracking.dll" or "Kronikol.AssertionTracking.dll" || n.StartsWith("Mono.Cecil", StringComparison.Ordinal))))
            File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
        return directory + Path.DirectorySeparatorChar;
    });

    private sealed record FixtureBuild(string Directory, string Assembly, string Output, int ExitCode);

    private enum Weavers { Steps, Assertions, StepsThenAssertions, AssertionsThenSteps }

    /// <summary>Builds <paramref name="source"/> as the project <c>{name}</c>, each weaver's targets imported as <paramref name="weavers"/> says, and <paramref name="extraProject"/> placed before the SDK's targets.</summary>
    private static FixtureBuild Build(string name, string source, Weavers weavers = Weavers.Steps, IReadOnlyList<string>? arguments = null, string extraProject = "",
        string configuration = "Debug", string? parent = null, string? assemblyName = null, bool mustBuild = true)
    {
        var directory = Path.Combine(parent ?? Root, name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Directory.Build.props"), "<Project />");
        File.WriteAllText(Path.Combine(directory, "Directory.Build.targets"), "<Project />");
        File.WriteAllText(Path.Combine(directory, "Steps.cs"), source, new System.Text.UTF8Encoding(false));
        var withSteps = weavers != Weavers.Assertions;
        var withAssertions = weavers != Weavers.Steps;
        var targets = weavers == Weavers.AssertionsThenSteps
            ? new[] { (AssertionBuild, "Kronikol.AssertionTracking"), (StepBuild, "Kronikol.StepTracking") }
            : new[] { (withSteps ? StepBuild : null, "Kronikol.StepTracking"), (withAssertions ? AssertionBuild : null, "Kronikol.AssertionTracking") };
        string Imports(string extension) => string.Join("\n", targets.Where(t => t.Item1 != null).Select(t => $"""  <Import Project="{Path.Combine(t.Item1!, t.Item2 + extension)}" />"""));
        File.WriteAllText(Path.Combine(directory, "Fixture.csproj"), $"""
            <Project>
              <Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />
            {Imports(".props")}
              <PropertyGroup>
                <TargetFramework>{Tfm}</TargetFramework>
                <AssemblyName>{assemblyName ?? "StepBuild" + name}</AssemblyName>
                <ImplicitUsings>enable</ImplicitUsings>
                <IsPackable>false</IsPackable>
                <Nullable>disable</Nullable>
                <NoWarn>$(NoWarn);CS1998;CS8602;CS8604</NoWarn>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Shouldly" Version="4.3.0" />
                <Reference Include="Kronikol" HintPath="{typeof(Track).Assembly.Location}" />
              </ItemGroup>
            {extraProject}
              <Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk" />
            {Imports(".targets")}
            </Project>
            """);

        var (output, exitCode) = Run(directory, ["build", "Fixture.csproj", "-c", configuration, "-nodeReuse:false",
            $"-p:_StepTrackingTaskDir={TaskDirectory.Value}", $"-p:_AssertionTrackingTaskDir={TaskDirectory.Value}", .. arguments ?? ["-v:m", "-tl:off"]]);
        if (mustBuild)
            Assert.True(exitCode == 0, $"dotnet build failed:\n{output}");
        return new FixtureBuild(directory, Path.Combine(directory, "bin", configuration, Tfm, (assemblyName ?? "StepBuild" + name) + ".dll"), output, exitCode);
    }

    /// <summary>Builds the same project again, as an edit and another <c>dotnet build</c> do, with nothing cleaned.</summary>
    private static FixtureBuild BuildAgain(FixtureBuild first, string name, Weavers weavers, IReadOnlyList<string> arguments, string? assemblyName = null)
    {
        var source = Path.Combine(first.Directory, "Steps.cs");
        return Build(name, File.ReadAllText(source) + "\npublic class ChangedSince { }\n", weavers, arguments, assemblyName: assemblyName ?? "StepBuild" + name);
    }

    private static (string Output, int ExitCode) Run(string directory, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        // The test host's own MSBuild settings would point the inner build at the outer SDK's pieces.
        foreach (var variable in new[] { "MSBuildSDKsPath", "MSBUILD_EXE_PATH", "MSBuildExtensionsPath" })
            start.Environment.Remove(variable);

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(TimeSpan.FromMinutes(5)), $"dotnet {string.Join(' ', arguments)} did not finish");
        return (stdout.Result + stderr.Result, process.ExitCode);
    }

    private sealed record Ran(object? Value, Exception? Thrown, string Tree);

    /// <summary>Calls a method of the fixture's <c>Steps</c> under a test identity of its own, and reads what it drew.</summary>
    private static async Task<Ran> Call(string assemblyPath, string method, params object?[] arguments)
    {
        var type = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath).GetType("Steps")!;
        var testId = $"{method}_{Guid.NewGuid():N}";
        object? value = null;
        Exception? thrown = null;
        using (TestIdentityScope.Begin(testId, testId))
        {
            try
            {
                var result = type.GetMethod(method)!.Invoke(Activator.CreateInstance(type), arguments);
                if (result is Task task)
                {
                    await task;
                    value = task.GetType().GetProperty("Result")?.GetValue(task);
                }
                else
                {
                    value = result;
                }
            }
            catch (TargetInvocationException e)
            {
                thrown = e.InnerException;
            }
            catch (Exception e)
            {
                thrown = e;
            }
        }
        return new Ran(value, thrown, string.Join(",", StepCollector.GetSteps(testId).Select(ShapeRunner.Render)));
    }

    private static string[] Warnings(string code, string output)
    {
        // The terminal logger colours its lines and moves the cursor between them: read the text without its escapes.
        var text = Regex.Replace(output, @"\x1b\[[0-9;?]*[A-Za-z]|\x1b\][^\x1b\a]*(\x1b\\|\a)", "");
        return Regex.Matches(text, $@"warning\s+{code}:\s+[^\r\n]*").Select(m => m.Value.TrimEnd()).Distinct().ToArray();
    }

    private const string Declarations = """
        using Kronikol.Tracking;

        [assembly: TrackSteps]

        """;

    private const string OnlySteps = "[assembly: TrackSteps]\n";
    private const string StepsAndAssertions = "[assembly: TrackSteps]\n[assembly: TrackAssertions]\n";

    private const string AssertionsWithNoStep = """
        using Kronikol.Tracking;
        using Shouldly;

        [assembly: TrackAssertions]

        public class Steps
        {
            public int With_an_assertion(string s) { s.ShouldBe("a"); return 8; }
        }
        """;

    private const string AStepThatThrows = """
        public class Steps
        {
            [Step] public int A_value() { return 7; }
            [Step] public int With_an_assertion(string s) { s.ShouldBe("a"); return 8; }
            [Step]
            public int Throws() { throw new InvalidOperationException("x"); }
        }
        """;

    private static string Fixture(string assemblyAttributes) => "using Kronikol.Tracking;\nusing Shouldly;\n\n" + assemblyAttributes + "\n" + AStepThatThrows;

    private static async Task AssertItsStepsAreDrawn(string assembly, bool withLineNumbers)
    {
        var value = await Call(assembly, "A_value");
        Assert.Equal(7, value.Value);
        Assert.Equal("A value:Passed", value.Tree);

        var thrower = await Call(assembly, "Throws");
        Assert.IsType<InvalidOperationException>(thrower.Thrown);
        Assert.Equal("Throws:Failed", thrower.Tree);
        if (withLineNumbers)
            Assert.Contains("Steps.cs:line", thrower.Thrown!.StackTrace);
        else
            Assert.DoesNotContain(":line", thrower.Thrown!.StackTrace);
    }

    // ---- what the build says ----

    private const string TwoIterators = Declarations + """
        public class Steps
        {
            public bool Off => true;
            [Step] public IEnumerable<int> An_iterator() { yield return 1; }
            [Step(SkipIf = nameof(Off))] public IEnumerable<int> Another() { yield return 2; }
        }
        """;

    private const string FirstOfTwo = "First: \\S*Steps\\.cs, line 7, in Steps\\.An_iterator: it is an iterator, whose body runs while its caller enumerates it, after the method has returned\\.";

    [Theory]
    [InlineData("off")]
    [InlineData("on")]
    public void A_build_warns_once_for_the_methods_it_left_as_written(string terminalLogger)
    {
        // The console logger, as a CI build or any build whose output is redirected uses it, and the terminal logger, dotnet's default
        // in an interactive terminal since .NET 9, which hides a message below detailed verbosity and shows a warning.
        var build = Build($"WarnsOnce{terminalLogger}", TwoIterators, arguments: ["-v:m", $"-tl:{terminalLogger}"]);

        var warning = Assert.Single(Warnings("KRONIKOL004", build.Output));
        Assert.Matches(new Regex($@"Kronikol\.StepTracking: 2 step method\(s\) were left as written, so they\s+run without a step\.\s+{FirstOfTwo.Replace(" ", @"\s+")}"), warning);
    }

    [Fact]
    public void NoWarn_silences_it()
    {
        var build = Build("WarnsNot", TwoIterators, extraProject: "<PropertyGroup><NoWarn>$(NoWarn);KRONIKOL004</NoWarn></PropertyGroup>");

        Assert.Empty(Warnings("KRONIKOL004", build.Output));
        Assert.DoesNotContain("KRONIKOL004", build.Output);
    }

    [Fact]
    public void A_build_at_normal_verbosity_lists_each_one()
    {
        var build = Build("WarnsAndLists", TwoIterators, arguments: ["-v:n", "-tl:off"]);

        Assert.Contains("Kronikol.StepTracking: Left 2 step method(s) unwoven; they run as written, without a step:", build.Output);
        Assert.Matches(new Regex(@"Steps\.cs, line 7, in Steps\.An_iterator: it is an iterator"), build.Output);
        Assert.Matches(new Regex(@"Steps\.cs, line 8, in Steps\.Another: it is an iterator.*, and its SkipIf is not read, so the body runs whatever that says"), build.Output);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "1 of them carries a SkipIf that is not read, so its body runs whatever it says.")]
    [InlineData(2, "2 of them carry a SkipIf that is not read, so their bodies run whatever it says.")]
    public void The_warning_counts_the_refused_steps_whose_SkipIf_is_not_read(int withSkipIf, string says)
    {
        // A clause on the first refused method would say nothing of the rest, so the first one here never has one.
        var source = Declarations + "public class Steps\n{\n    public bool Off => true;\n    [Step] public IEnumerable<int> First() { yield return 1; }\n"
            + string.Concat(Enumerable.Range(0, withSkipIf).Select(i => $"    [Step(SkipIf = nameof(Off))] public IEnumerable<int> Second{i}() {{ yield return 2; }}\n"))
            + "}\n";

        var warning = Assert.Single(Warnings("KRONIKOL004", Build($"Counts{withSkipIf}", source).Output));

        Assert.Contains($"{withSkipIf + 1} step method(s) were left as written", warning);
        if (says.Length == 0)
            Assert.DoesNotContain("SkipIf", warning);
        else
            Assert.Contains(says, warning);
    }

    [Fact]
    public void A_SkipIf_it_cannot_use_warns_with_a_code_a_project_can_silence()
    {
        const string source = Declarations + """
            public class Steps
            {
                [Step(SkipIf = "NotThere")] public int A_value() { return 7; }
            }
            """;

        var build = Build("SkipIfWarns", source);
        var warning = Assert.Single(Warnings("KRONIKOL005", build.Output));
        Assert.Contains("NotThere", warning);

        var quiet = Build("SkipIfQuiet", source, extraProject: "<PropertyGroup><NoWarn>$(NoWarn);KRONIKOL005</NoWarn></PropertyGroup>");
        Assert.DoesNotContain("KRONIKOL005", quiet.Output);
    }

    [Fact]
    public void A_step_attribute_with_no_body_warns_nothing_and_is_listed_at_normal_verbosity()
    {
        // A warning would name code that runs as it should, and fail a build under -warnaserror that passes on 5.28.2. What runs is the
        // override or the implementation, each with an attribute of its own or none.
        const string source = Declarations + """
            public abstract class Base { [Step] public abstract int An_override(); }
            public interface IShape { [Step] int An_implementation(); }
            public class Steps : Base, IShape
            {
                [Step] public override int An_override() { return 1; }
                [Step] public int An_implementation() { return 2; }
            }
            """;

        var quiet = Build("NoBody", source, arguments: ["-v:m", "-tl:off", "-warnaserror"]);
        Assert.DoesNotContain("KRONIKOL00", quiet.Output);

        var listed = Build("NoBodyListed", source, arguments: ["-v:n", "-tl:off"]);
        Assert.Contains("Kronikol.StepTracking: A step attribute did nothing: ", listed.Output);
        Assert.Contains("Base.An_override: it has no body to bracket", listed.Output);
        Assert.Contains("IShape.An_implementation: it has no body to bracket", listed.Output);
    }

    // ---- the weavers together ----

    [Theory]
    [InlineData(Weavers.StepsThenAssertions)]
    [InlineData(Weavers.AssertionsThenSteps)]
    public async Task The_steps_and_assertions_weavers_run_in_either_order(object weavers)
    {
        // A real build runs the steps' first. On 5.28.2 a step that returned a value left the stack broken, so the assertion weaver gave up,
        // and the call threw.
        var build = Build($"Order{weavers}", Fixture(StepsAndAssertions), (Weavers)weavers);

        Assert.DoesNotContain("KRONIKOL003", build.Output);
        var run = await Call(build.Assembly, "With_an_assertion", "a");
        Assert.Equal(8, run.Value);
        Assert.Equal("With an assertion(s=a):Passed[~assert:Passed]", run.Tree);
    }

    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public async Task A_library_that_names_no_core_type_keeps_the_note_of_a_task_it_hands_back(string configuration)
    {
        // The compiler keeps no reference to a core no type names, and the step weaver, which runs first, adds one with no version, which the
        // assertion weaver read as a core older than Track.AssertionTask (F25).
        const string source = """
            using System.Threading.Tasks;
            using Kronikol.Tracking;
            using Shouldly;

            [assembly: TrackSteps]
            [assembly: TrackAssertions]

            public class Steps
            {
                [Step] public Task<ArithmeticException> Hands_it_back() => Should.ThrowAsync<ArithmeticException>(() => Task.FromException(new ArithmeticException("x")));
                [Step] public Task<ArithmeticException> Hands_back_one_that_fails() => Should.ThrowAsync<ArithmeticException>(() => Task.CompletedTask);
            }
            """;

        var build = Build($"NoCoreType{configuration}", source, Weavers.StepsThenAssertions, configuration: configuration);

        var passes = await Call(build.Assembly, "Hands_it_back");
        Assert.Null(passes.Thrown);
        Assert.Equal("Hands it back:Passed[~assert:Passed]", passes.Tree);
        var fails = await Call(build.Assembly, "Hands_back_one_that_fails");
        Assert.NotNull(fails.Thrown);
        Assert.Equal("Hands back one that fails:Failed[~assert:Failed]", fails.Tree);
    }

    // ---- a project's symbols ----

    [Fact]
    public async Task A_project_that_embeds_its_symbols_is_woven()
    {
        var build = Build("Embedded", Fixture(OnlySteps), arguments: ["-v:m", "-tl:off", "-p:DebugType=embedded"]);

        await AssertItsStepsAreDrawn(build.Assembly, withLineNumbers: true);
        AssertEmbedsItsSymbols(build.Assembly);
    }

    [Fact]
    public async Task A_project_with_no_symbols_is_woven()
    {
        var build = Build("NoSymbols", Fixture(OnlySteps), arguments: ["-v:m", "-tl:off", "-p:DebugType=none"]);

        await AssertItsStepsAreDrawn(build.Assembly, withLineNumbers: false);
    }

    private static void AssertEmbedsItsSymbols(string assembly)
    {
        using var reader = new PEReader(new MemoryStream(File.ReadAllBytes(assembly)));
        Assert.Contains(reader.ReadDebugDirectory(), entry => entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
    }

    [Theory]
    [InlineData(Weavers.Steps)]
    [InlineData(Weavers.StepsThenAssertions)]
    public async Task A_project_with_its_PdbFile_elsewhere_keeps_its_symbols(object weavers)
    {
        // Named as the build would name it, in a folder of its own: nothing was woven on 5.28.2, and nothing but the PdbFile says where the symbols are.
        var name = $"Elsewhere{weavers}";
        Directory.CreateDirectory(Path.Combine(Root, name, "symbols"));
        var build = Build(name, Fixture((Weavers)weavers == Weavers.Steps ? OnlySteps : StepsAndAssertions), (Weavers)weavers,
            arguments: ["-v:m", "-tl:off", $"-p:PdbFile=symbols/StepBuild{name}.pdb"]);

        await AssertItsStepsAreDrawn(build.Assembly, withLineNumbers: true);
        if ((Weavers)weavers != Weavers.Steps)
            Assert.Equal("With an assertion(s=a):Passed[~assert:Passed]", (await Call(build.Assembly, "With_an_assertion", "a")).Tree);
    }

    private const string Faults = """
        using Kronikol.Tracking;
        using Shouldly;

        [assembly: TrackSteps]
        [assembly: TrackAssertions]

        public class Steps
        {
            public int Seen;

            [Step]
            public void Out_of_range(int[] a)
            {
                Seen++;
                var x = a[5];
                Seen += x;
            }

            [Step]
            public int Overflows(int a)
            {
                Seen++;
                var x = checked(a + int.MaxValue);
                Seen += x;
                return Seen;
            }

            [Step]
            public void In_a_try(string s)
            {
                var t = "";
                try
                {
                    t += s.ShouldNotBeNull();
                }
                finally
                {
                    Seen++;
                }
                Seen += t.Length;
            }

            [Step]
            public void In_a_finally(string s)
            {
                var t = "";
                try
                {
                    t += "a";
                }
                finally
                {
                    t += s.ShouldNotBeNull();
                }
                Seen += t.Length;
            }
        }
        """;

    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public async Task A_fault_in_a_step_keeps_the_file_and_line_a_plain_build_gives_it(string configuration)
    {
        // Optimised code has no offset for a fault the JIT raises out of line, an array's bounds or an overflow, and the runtime reads it as the method's start. The compiler has a point
        // there. The weave's first instructions had none, so the frame had no file and no line at all, and Shouldly, which reads the source through them, lost the text of its message.
        // A plain build of the same source, with blank lines for the attributes so that no line moves, is what each should read.
        var woven = Build($"Faults{configuration}", Faults, Weavers.StepsThenAssertions, configuration: configuration);
        var plain = Build($"FaultsPlain{configuration}", Faults.Replace("[assembly: TrackSteps]", "").Replace("[assembly: TrackAssertions]", ""), Weavers.Steps, configuration: configuration, assemblyName: $"StepBuildFaultsPlain{configuration}");

        // A build that is not optimised has its offsets, so a fault reads its own line, and only a finally still reads the method's start. It gets no point (see
        // A_step_counts_under_coverage_as_a_plain_build_does), so a finally there has no file and no line, as it never has had, and is not compared.
        var shapes = new (string, object?[])[] { ("Out_of_range", [new int[2]]), ("Overflows", [1]), ("In_a_try", [null]), ("In_a_finally", [null]) };
        foreach (var (method, arguments) in shapes.Where(shape => configuration == "Release" || shape.Item1 != "In_a_finally"))
        {
            var ran = await Call(woven.Assembly, method, arguments);
            var expected = await Call(plain.Assembly, method, arguments);

            Assert.Equal(expected.Thrown!.GetType(), ran.Thrown!.GetType());
            Assert.Equal(expected.Thrown.Message, ran.Thrown.Message);
            var line = Regex.Match(expected.Thrown.StackTrace!, @$"Steps\.{method}\(.*Steps\.cs:line \d+").Value;
            Assert.NotEqual("", line);
            Assert.Equal(Path.GetFileName(line), Path.GetFileName(Regex.Match(ran.Thrown.StackTrace!, @$"Steps\.{method}\(.*Steps\.cs:line \d+").Value));
        }
    }

    private const string Counted = """
        using Kronikol.Tracking;

        [assembly: TrackSteps]

        public class Steps
        {
            public bool Off => true;
            public int Seen;

            [Step]
            public void Plain(bool b)
            {
                if (b)
                    Seen++;
                else
                    Seen--;
            }

            [Step]
            public int Expression(int a) => a + Seen;

            [Step]
            public Task Handed_back(bool b)
            {
                if (b)
                    return Task.Delay(1);
                return Task.CompletedTask;
            }

            [Step]
            public async Task Awaited()
            {
                await Task.Yield();
                Seen++;
            }

            [Step]
            public void Loops()
            {
                do
                {
                    Seen++;
                } while (Seen < 3);
            }

            [Step(SkipIf = nameof(Off))]
            public void Bypassed()
            {
                Seen++;
            }

            [Step(SkipIf = nameof(Off))]
            public int Bypassed_with_a_value()
            {
                return Seen;
            }
        }
        """;

    private static Dictionary<string, List<string>> VisiblePoints(string assemblyPath)
    {
        using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(assemblyPath, new Mono.Cecil.ReaderParameters { ReadSymbols = true });
        return assembly.MainModule.GetType("Steps").Methods.Where(m => m.HasBody && m.DebugInformation.HasSequencePoints).ToDictionary(
            m => m.Name,
            m => m.DebugInformation.SequencePoints.Where(p => !p.IsHidden).Select(p => $"{p.StartLine}:{p.StartColumn}-{p.EndLine}:{p.EndColumn}").ToList());
    }

    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public void A_step_counts_under_coverage_as_a_plain_build_does(string configuration)
    {
        // A coverage tool counts a hit at every visible point, so a point the weave adds is a line run once more, or a line run at all. A build that is not optimised gets
        // none, nor does a method with a bypass, whose first line would read as run when the bypass went past it. An optimised build pays one hit on the first line for a
        // fault's file and line: the plain lines stay, each with its own count, and no line changes between covered and not.
        var woven = VisiblePoints(Build($"Counted{configuration}", Counted, configuration: configuration).Assembly);
        var plain = VisiblePoints(Build($"CountedPlain{configuration}", Counted.Replace("[assembly: TrackSteps]", ""), configuration: configuration).Assembly);

        Assert.Equal(plain.Keys.Order(), woven.Keys.Order());
        foreach (var (method, points) in plain)
        {
            var addsOne = configuration == "Release" && !method.StartsWith("Bypassed") && method is not "Awaited" && !method.StartsWith("get_");
            Assert.Equal(addsOne ? [points[0], .. points] : points, woven[method]);
        }
    }

    [Fact]
    public void A_project_with_only_assertions_and_its_PdbFile_elsewhere_is_woven()
    {
        const string name = "ElsewhereAssertionsOnly";
        Directory.CreateDirectory(Path.Combine(Root, name, "symbols"));
        var build = Build(name, AssertionsWithNoStep, Weavers.Assertions, arguments: ["-v:n", "-tl:off", $"-p:PdbFile=symbols/StepBuild{name}.pdb"]);

        Assert.Contains("Kronikol.AssertionTracking: Instrumented 1 assertion(s)", build.Output);
    }

    [Theory]
    [InlineData(Weavers.Steps, "custom.pdb")]
    [InlineData(Weavers.Steps, "custom")]
    [InlineData(Weavers.Steps, "custom.sym")]
    [InlineData(Weavers.StepsThenAssertions, "custom.pdb")]
    [InlineData(Weavers.StepsThenAssertions, "custom")]
    [InlineData(Weavers.StepsThenAssertions, "custom.sym")]
    public async Task A_project_with_a_PdbFile_of_its_own_keeps_its_line_numbers(object weavers, string file)
    {
        // The runtime finds a PDB by the name in the assembly, and the build writes this one's extension as .pdb whatever it was given.
        // With one weaver as well as both, since the last to write hides what the first left.
        var kind = (Weavers)weavers;
        var name = $"OwnName{kind}" + file.Replace('.', '_');
        Directory.CreateDirectory(Path.Combine(Root, name, "symbols"));
        var build = Build(name, Fixture(kind == Weavers.Steps ? OnlySteps : StepsAndAssertions), kind, arguments: ["-v:m", "-tl:off", $"-p:PdbFile=symbols/{file}"]);

        await AssertItsStepsAreDrawn(build.Assembly, withLineNumbers: true);
        if (kind != Weavers.Steps)
            Assert.Equal("With an assertion(s=a):Passed[~assert:Passed]", (await Call(build.Assembly, "With_an_assertion", "a")).Tree);
    }

    [Fact]
    public async Task A_project_with_a_TargetName_of_its_own_keeps_its_line_numbers()
    {
        // The compiler names the assembly after the file it writes, so neither weaver has anything to lose here (F28).
        const string renamed = "StepBuildRenamedTarget";
        var build = Build("TargetName", Fixture(StepsAndAssertions), Weavers.StepsThenAssertions, arguments: ["-v:m", "-tl:off", $"-p:TargetName={renamed}"]);

        await AssertItsStepsAreDrawn(Path.Combine(Path.GetDirectoryName(build.Assembly)!, renamed + ".dll"), withLineNumbers: true);
    }

    [Theory]
    [InlineData(Weavers.Steps)]
    [InlineData(Weavers.Assertions)]
    [InlineData(Weavers.StepsThenAssertions)]
    public void A_PdbFile_under_a_path_map_leaves_two_clones_the_same(object weavers)
    {
        // The name of a PDB file the weave writes itself is what the assembly records, and a build with mapped paths must not carry this machine's folder in it.
        // With one weaver as well as both, since the last to write hides what the first left.
        var kind = (Weavers)weavers;
        var source = kind == Weavers.Assertions ? AssertionsWithNoStep : Fixture(kind == Weavers.Steps ? OnlySteps : StepsAndAssertions);
        var parent = Path.Combine(Root, $"Clones{kind}");
        var clones = new[] { "CloneA", "CloneB" }.Select(clone =>
        {
            var directory = Path.Combine(parent, clone);
            Directory.CreateDirectory(Path.Combine(directory, "symbols"));
            return Build(clone, source, kind, parent: parent, configuration: "Release", assemblyName: "StepBuildTwoClones",
                arguments: ["-v:m", "-tl:off", "-p:PdbFile=symbols/custom.pdb", $"-p:PathMap={directory}{Path.DirectorySeparatorChar}=/_/"]);
        }).ToList();

        var first = File.ReadAllBytes(clones[0].Assembly);
        Assert.Equal(first, File.ReadAllBytes(clones[1].Assembly));
        Assert.DoesNotContain("CloneA", System.Text.Encoding.Latin1.GetString(first));
        Assert.DoesNotContain("KRONIKOL003", clones[0].Output);
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(clones[0].Assembly)!, "custom.pdb")), "the build copied the PDB beside the assembly");
    }

    [Theory]
    [InlineData(Weavers.Steps, "none")]
    [InlineData(Weavers.Steps, "embedded")]
    [InlineData(Weavers.Assertions, "none")]
    [InlineData(Weavers.Assertions, "embedded")]
    [InlineData(Weavers.StepsThenAssertions, "none")]
    [InlineData(Weavers.StepsThenAssertions, "embedded")]
    public async Task A_project_whose_DebugType_changed_builds_without_a_clean(object weavers, string debugType)
    {
        // The old PDB stays in obj until a clean, and it is not the new assembly's. To none, it failed the build with a stack trace. To embedded,
        // the build passed, the symbols went to the old file, and the assembly embedded none.
        var kind = (Weavers)weavers;
        var name = $"Changed{kind}{debugType}";
        var source = kind == Weavers.Assertions ? AssertionsWithNoStep : Fixture(kind == Weavers.Steps ? OnlySteps : StepsAndAssertions);
        var first = Build(name, source, kind);
        Assert.Equal(0, first.ExitCode);

        var second = BuildAgain(first, name, kind, ["-v:m", "-tl:off", $"-p:DebugType={debugType}"]);

        Assert.Equal(0, second.ExitCode);
        Assert.DoesNotContain("SymbolsNotMatching", second.Output);
        if (kind != Weavers.Assertions)
            await AssertItsStepsAreDrawn(second.Assembly, withLineNumbers: debugType == "embedded");
        if (debugType == "embedded")
            AssertEmbedsItsSymbols(second.Assembly);
    }
}
