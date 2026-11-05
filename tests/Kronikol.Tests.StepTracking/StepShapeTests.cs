using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace Kronikol.Tests.StepTracking;

/// <summary>
/// The method shapes of STEP_METHOD_SHAPES_PLAN section 9.1, each in Debug, in Release and in Release with <c>/debug+</c>: the
/// weaver writes IL past the point where anything checks it, and until #226 its only tests were a <c>void</c> and an
/// <c>async Task</c>. A shape names what it should do, and <see cref="ShapeRunner"/> says how it is asked.
/// </summary>
public class StepShapeTests
{
    public static TheoryData<string, string> Rows()
    {
        var data = new TheoryData<string, string>();
        foreach (var (shape, configuration) in ShapeRuns.Rows)
            data.Add(shape.Id, configuration);
        return data;
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task A_method_shape_does_what_it_should(string id, string configuration)
    {
        var outcome = await ShapeRuns.OutcomeOf(id, configuration);

        // The id again: a long one is cut short in the row's name.
        Assert.True(outcome.Flags.Count == 0, $"{outcome.Id}, {outcome.Config}, {outcome.Primary} ({string.Join("+", outcome.Flags)}): {outcome.Detail}");
    }

    /// <summary>
    /// F19, named on its own because a reader looks for it: on a reused MSBuild node an assembly the weave leaves open stops the next
    /// build copying it. Each shape references an assembly its weaver has to open: a base that a SkipIf walks into, a type on the stack
    /// around an assertion, and a type a step returns.
    /// </summary>
    [Theory]
    [InlineData("Z03c-ivt-internal-base", "Release")]
    [InlineData("Z75-assertion-over-a-referenced-type-steps-first", "Release")]
    [InlineData("Z76-step-returns-a-referenced-type", "Debug")]
    [InlineData("Z76-step-returns-a-referenced-type", "Release")]
    public async Task A_referenced_assembly_is_free_after_the_weave(string id, string configuration)
    {
        Assert.True(ShapeRuns.NeedsAQuietProcess(Shapes.All.Single(s => s.Id == id)), "the shape references an assembly of its own");

        var outcome = await ShapeRuns.OutcomeOf(id, configuration);

        Assert.DoesNotContain("LOCKED", outcome.Flags);
    }

    /// <summary>F4, named on its own because a reader looks for it: a bypass's own <c>ret</c> was rewritten with the method's, so it completed twice and closed the step it was in.</summary>
    [Theory]
    [InlineData("H11-nested-bypass-void")]
    [InlineData("H11-nested-bypass-task")]
    [InlineData("H11-nested-bypass-task-int")]
    [InlineData("H11-nested-bypass-value")]
    public async Task A_bypassed_sub_step_leaves_its_parent_open(string id)
    {
        foreach (var configuration in ShapeRunner.Configurations)
        {
            var outcome = await ShapeRuns.OutcomeOf(id, configuration);

            Assert.DoesNotContain("TREE", outcome.Flags);
            Assert.DoesNotContain("OPEN", outcome.Flags);
            Assert.Contains("S:Bypassed", outcome.Tree);
            Assert.StartsWith("M:Passed[", outcome.Tree);
        }
    }

    /// <summary>Z136c: the first cut of the write for a PDB named for itself overwrote and deleted a file of the project's that bears its name and a .dll's.</summary>
    [Theory]
    [InlineData("Z136c-a-file-bearing-the-pdb-name-steps-first")]
    [InlineData("Z136c-a-file-bearing-the-pdb-name-assertions-first")]
    public async Task A_file_of_the_project_that_bears_the_name_of_its_PDB_is_left_alone(string id)
    {
        Assert.Contains("//PDB-BESIDE-A-FILE", Shapes.All.Single(s => s.Id == id).Source);

        foreach (var configuration in ShapeRunner.Configurations)
        {
            var outcome = await ShapeRuns.OutcomeOf(id, configuration);

            Assert.DoesNotContain("FILE-LOST", outcome.Flags);
            Assert.DoesNotContain("LEFT-BEHIND", outcome.Flags);
        }
    }

    /// <summary>
    /// Q8's limit, pinned. A method compiled for runtime async has no stub: StartStep runs in its own frame, and the runtime undoes an AsyncLocal
    /// write there when it returns. So a step moves the phase inside itself and not for its caller, where a state machine's moves it for both.
    /// </summary>
    [Theory]
    [InlineData("Z147-runtime-async-phase-after-a-suspending-step")]
    [InlineData("Z147-runtime-async-phase-after-a-step-that-does-not-suspend")]
    public async Task Under_runtime_async_a_step_moves_the_phase_inside_itself_and_not_for_its_caller(string id)
    {
        Assert.True(ShapeRuns.NeedsTheRuntimesSwitch(Shapes.All.Single(s => s.Id == id)));

        foreach (var configuration in ShapeRunner.Configurations)
        {
            var outcome = await ShapeRuns.OutcomeOf(id, configuration);

            Assert.True(outcome.Flags.Count == 0, $"{outcome.Primary}: {outcome.Detail}");
            Assert.Equal("inside Action, after Setup", outcome.Value);
        }
    }

    /// <summary>
    /// The differential, under the two weavers together: one body twice, with an assertion and with a plain identity in its place, and only the first
    /// is woven. It asks nothing of what a statement should do, so a shape that reads differently fails by what it returned, whatever it draws.
    /// A plain method under a step and a value step, as a real build weaves them.
    /// </summary>
    [Theory]
    [MemberData(nameof(DifferentialConditions))]
    public async Task The_weave_does_not_change_what_a_program_does(string condition, string configuration)
    {
        var rows = await ShapeRuns.DifferentialOutcomes(condition, configuration);

        Assert.True(rows.Count >= 62, $"{rows.Count} rows");
        var changed = rows.Where(o => o.Value != "same" || !string.IsNullOrEmpty(o.Threw)).Select(o => $"{o.Id}: {o.Primary}, read {o.Value}, threw {o.Threw}: {o.Detail}").ToList();
        Assert.True(changed.Count == 0, $"{condition}, {configuration}:\n  " + string.Join("\n  ", changed));
    }

    public static TheoryData<string, string> DifferentialConditions()
    {
        var data = new TheoryData<string, string>();
        foreach (var condition in Shapes.Differential.Select(s => s.Id.Split('-')[1]).Distinct())
            foreach (var configuration in new[] { "Debug", "Release" })
                data.Add(condition, configuration);
        return data;
    }

    [Fact]
    public void Every_shape_in_the_catalogue_is_run()
    {
        var ids = Shapes.All.Select(s => s.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());

        // A list that lost a family, or a row's condition that took a shape out, would pass every row that is left.
        Assert.True(ids.Count >= 635, $"{ids.Count} shapes");
        Assert.Equal(
            ["A", "B", "C", "D", "E", "F", "G", "H", "I", "Z"],
            Shapes.All.Select(s => s.Family[..1]).Distinct().Order(StringComparer.Ordinal));
        var windowsOnly = OperatingSystem.IsWindows() ? 0 : ShapeRuns.WindowsOnly.Length;
        Assert.Equal((ids.Count - windowsOnly) * ShapeRunner.Configurations.Length, ShapeRuns.Rows.Count);

        // Each kind of reading has shapes to read it, or it has nothing to catch.
        Assert.True(Shapes.All.Count(s => s.Refused) >= 20, "shapes that should be left as written");
        Assert.True(Shapes.All.Count(s => s.Assertions && s.AssertionsFirst) >= 20, "shapes the assertion weaver reads first");
        Assert.True(Shapes.All.Count(s => s.Assertions && !s.AssertionsFirst) >= 20, "shapes the step weaver reads first");
        Assert.True(Shapes.All.Count(s => s.Throws != null) >= 10, "shapes that should throw");
        Assert.True(Shapes.All.Count(ShapeRuns.NeedsTheRuntimesSwitch) >= 10, "shapes under runtime async");
    }
}

/// <summary>
/// The whole catalogue runs once, at the first row that asks, on as many threads as the machine spares: a theory's rows run one
/// at a time. Each row then reads its own outcome and fails by itself.
/// </summary>
internal static class ShapeRuns
{
    public const string ResultsVariable = "KRONIKOL_STEP_SHAPE_RESULTS";
    public const string KindVariable = "KRONIKOL_STEP_SHAPE_KIND";

    /// <summary><c>__arglist</c> is Windows' alone: a case the shape cannot apply to elsewhere.</summary>
    public static readonly string[] WindowsOnly = ["F16-arglist"];

    private static readonly string Work = Path.Combine(Path.GetTempPath(), "KRONIKOL_StepShapes", Guid.NewGuid().ToString("N")[..8]);

    public static IReadOnlyList<(Shape Shape, string Configuration)> Rows { get; } =
        Shapes.All
            .Where(shape => OperatingSystem.IsWindows() || !WindowsOnly.Contains(shape.Id))
            .SelectMany(shape => ShapeRunner.Configurations.Select(configuration => (shape, configuration)))
            .ToList();

    private static readonly Lazy<Task<ConcurrentDictionary<(string, string), ShapeOutcome>>> Outcomes = new(() => Task.Run(RunAll));

    public static bool NeedsTheRuntimesSwitch(Shape shape) => shape.Source.Contains("//RUNTIME-ASYNC");

    /// <summary>
    /// A shape with an assembly of its own to hold: whether the weave let it go shows only where nothing else is allocating, as a
    /// collection anywhere in the process finalizes a handle left open and hides the fault (F19).
    /// </summary>
    public static bool NeedsAQuietProcess(Shape shape) => shape.Source.Contains("//BASE:") && !NeedsTheRuntimesSwitch(shape);

    public static async Task<ShapeOutcome> OutcomeOf(string id, string configuration) =>
        (await Outcomes.Value)[(id, configuration)];

    private static readonly Lazy<Task<ConcurrentDictionary<(string, string), ShapeOutcome>>> Differential = new(() => Task.Run(async () =>
    {
        Directory.CreateDirectory(Work);
        var outcomes = new ConcurrentDictionary<(string, string), ShapeOutcome>();
        await RunRows(Shapes.Differential.SelectMany(shape => new[] { (shape, "Debug"), (shape, "Release") }), outcomes);
        return outcomes;
    }));

    public static async Task<IReadOnlyList<ShapeOutcome>> DifferentialOutcomes(string condition, string configuration) =>
        (await Differential.Value).Values.Where(o => o.Config == configuration && o.Id.Split('-')[1] == condition).ToList();

    private static async Task<ConcurrentDictionary<(string, string), ShapeOutcome>> RunAll()
    {
        Directory.CreateDirectory(Work);
        var outcomes = new ConcurrentDictionary<(string, string), ShapeOutcome>();
        var here = RunRows(Rows.Where(row => !NeedsTheRuntimesSwitch(row.Shape) && !NeedsAQuietProcess(row.Shape)), outcomes);
        var underTheSwitch = RunInAProcessOfTheirOwn(TheSwitch, outcomes);
        var quiet = RunInAProcessOfTheirOwn(Quiet, outcomes);
        await Task.WhenAll(here, underTheSwitch, quiet);
        return outcomes;
    }

    private const string TheSwitch = "the runtime's switch";
    private const string Quiet = "quiet";

    public static Task RunRows(IEnumerable<(Shape Shape, string Configuration)> rows, ConcurrentDictionary<(string, string), ShapeOutcome> outcomes, int parallel = 0) =>
        Parallel.ForEachAsync(rows, new ParallelOptions { MaxDegreeOfParallelism = parallel > 0 ? parallel : Math.Clamp(Environment.ProcessorCount / 2, 2, 8) }, async (row, _) =>
        {
            ShapeOutcome outcome;
            try
            {
                outcome = await Task.Run(() => ShapeRunner.Run(row.Shape, row.Configuration, Work)).WaitAsync(TimeSpan.FromSeconds(120));
            }
            catch (Exception ex)
            {
                outcome = new ShapeOutcome { Id = row.Shape.Id, Config = row.Configuration, Primary = "HARNESS", Flags = ["HARNESS"], Detail = ex.ToString() };
            }
            outcomes[(row.Shape.Id, row.Configuration)] = outcome;
        });

    /// <summary>
    /// Roslyn's runtime-async feature needs a switch the runtime reads as it starts on net10.0, so those rows run under this test
    /// assembly's own runner in a process that has it. ILVerify says little about such a method, since it does not read
    /// <c>MethodImplAttributes.Async</c>, so a regression there can crash with it silent, and only that process goes.
    /// </summary>
    private static async Task RunInAProcessOfTheirOwn(string kind, ConcurrentDictionary<(string, string), ShapeOutcome> outcomes)
    {
        var results = Path.Combine(Work, kind + ".json");
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Kronikol.Tests.StepTracking" + (OperatingSystem.IsWindows() ? ".exe" : "")))
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-class");
        start.ArgumentList.Add(typeof(ShapesInAProcessOfTheirOwn).FullName!);
        start.ArgumentList.Add("-noColor");
        if (kind == TheSwitch)
            start.Environment["DOTNET_RuntimeAsync"] = "1";
        start.Environment[ResultsVariable] = results;
        start.Environment[KindVariable] = kind;

        string detail;
        try
        {
            using var child = Process.Start(start)!;
            var stdout = child.StandardOutput.ReadToEndAsync();
            var stderr = child.StandardError.ReadToEndAsync();
            using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            await child.WaitForExitAsync(limit.Token);
            detail = $"exit {child.ExitCode}: {await stdout}{await stderr}";
        }
        catch (Exception ex)
        {
            detail = ex.ToString();
        }

        var read = File.Exists(results)
            ? JsonSerializer.Deserialize<List<ShapeOutcome>>(File.ReadAllText(results))!
            : [];
        foreach (var outcome in read)
            outcomes[(outcome.Id, outcome.Config)] = outcome;
        foreach (var (shape, configuration) in RowsOf(kind))
            outcomes.TryAdd((shape.Id, configuration), new ShapeOutcome { Id = shape.Id, Config = configuration, Primary = "CRASH", Flags = ["CRASH"], Detail = $"no result from the process for {kind}: {detail}" });
    }

    private static IEnumerable<(Shape Shape, string Configuration)> RowsOf(string kind) =>
        Rows.Where(row => kind == TheSwitch ? NeedsTheRuntimesSwitch(row.Shape) : NeedsAQuietProcess(row.Shape));

    public static async Task RunInThisProcess(string results, string kind)
    {
        var outcomes = new ConcurrentDictionary<(string, string), ShapeOutcome>();
        Directory.CreateDirectory(Work);
        await RunRows(RowsOf(kind), outcomes, parallel: kind == Quiet ? 1 : 0);
        File.WriteAllText(results, JsonSerializer.Serialize(outcomes.Values.ToList()));
    }
}

/// <summary>What <see cref="ShapeRuns"/> starts in a process of its own. Anywhere else it has nothing to do.</summary>
public class ShapesInAProcessOfTheirOwn
{
    [Fact]
    public async Task Runs_the_rows_it_was_started_for()
    {
        if (Environment.GetEnvironmentVariable(ShapeRuns.ResultsVariable) is { } results)
            await ShapeRuns.RunInThisProcess(results, Environment.GetEnvironmentVariable(ShapeRuns.KindVariable)!);
    }
}
