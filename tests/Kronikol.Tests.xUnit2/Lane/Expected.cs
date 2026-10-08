namespace Kronikol.Tests.xUnit2.Lane;

/// <summary>The child processes share the machine, the fixtures' build tree and their output folders, so the lane's
/// facts run one at a time, after the rest of the assembly.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FixtureLaneCollection
{
    public const string Name = "xUnit v2 fixture lane";
}

/// <summary>What a report should hold for the fixtures' tests, and the comparison every fixture fact starts from.</summary>
public static class Expected
{
    /// <summary>The fixture classes TestTrackingAttribute applies to (<c>Shared/Tracking.cs</c>); Untracked is the one it does not.</summary>
    public static readonly string[] TrackedClasses =
    [
        "Facts", "Theories", "NonSerializableRows", "DisplayNameDots", "CtorThrows", "InitThrows", "FixtureThrows",
        "UnresolvableArgument", "ConstructorCall",
    ];

    /// <summary>Every test in the fixtures, Untracked's three included.</summary>
    public const int TestCount = 24;

    public static bool IsTracked(TrxTest test) => TrackedClasses.Contains(test.ClassSimpleName);

    public static string Feature(TrxTest test) => ScenarioTitleResolver.FormatFeatureName(test.ClassSimpleName);

    public static string ScenarioName(TrxTest test) => ScenarioTitleResolver.FormatScenarioDisplayName(test.DisplayName);

    public static string Result(TrxTest test) => test.Outcome switch
    {
        "NotExecuted" => "Skipped",
        var outcome => outcome,
    };

    /// <summary>
    /// The runner's text of an exception's message without the type xUnit puts in front of any exception not its
    /// own (<c>System.InvalidOperationException : …</c>, xUnit's <c>ExceptionUtility</c>). A report carries the
    /// message as thrown, as Kronikol's other adapters do.
    /// </summary>
    public static string WithoutExceptionType(string line)
    {
        var separator = line.IndexOf(" : ", StringComparison.Ordinal);
        return separator > 0 && !line[..separator].Contains(' ') ? line[(separator + 3)..] : line;
    }

    public static string FirstLine(string? text) =>
        (text ?? "").Replace("\r", "").Split('\n')[0].Trim();

    /// <summary>
    /// Every tracked test is one scenario, under its own name, with its own verdict, duration, error and calls,
    /// and nothing else is a scenario. A call a tracked test made is in no other scenario and not in the
    /// background section.
    /// </summary>
    public static void EveryTrackedTestIsItsOwnScenario(FixtureRun run)
    {
        var trx = run.Trx ?? throw new Xunit.Sdk.XunitException($"No TRX.{Environment.NewLine}{run.Describe()}");
        Assert.True(trx.Tests.Count == TestCount, $"The run recorded {trx.Tests.Count} tests, not {TestCount}.{Environment.NewLine}{run.Describe()}");

        var report = run.Report;
        var tracked = trx.Tests.Where(IsTracked).ToArray();
        var problems = new List<string>();

        foreach (var test in tracked)
        {
            var named = report.Named(Feature(test), ScenarioName(test));
            if (named.Length != 1)
            {
                problems.Add($"{test}: {named.Length} scenarios named \"{ScenarioName(test)}\" in \"{Feature(test)}\"");
                continue;
            }

            var scenario = named[0];
            if (scenario.Result != Result(test))
                problems.Add($"{test}: reported {scenario.Result}");
            if (Math.Abs(scenario.DurationSeconds - test.Duration.TotalSeconds) > 0.005)
                problems.Add($"{test}: took {test.Duration.TotalSeconds:0.000} s, reported {scenario.DurationSeconds:0.000} s");
            if (!scenario.Calls.SequenceEqual(test.Calls))
                problems.Add($"{test}: made [{string.Join(", ", test.Calls)}], reported [{string.Join(", ", scenario.Calls)}]");
            if (Result(test) == "Failed" && FirstLine(scenario.ErrorMessage) != WithoutExceptionType(FirstLine(test.ErrorMessage)))
                problems.Add($"{test}: failed with \"{FirstLine(test.ErrorMessage)}\", reported \"{FirstLine(scenario.ErrorMessage)}\"");
            if (Result(test) != "Failed" && scenario.ErrorMessage is not null)
                problems.Add($"{test}: {Result(test)}, reported with an error \"{FirstLine(scenario.ErrorMessage)}\"");
        }

        if (report.Scenarios.Count != tracked.Length)
            problems.Add($"{tracked.Length} tracked tests, {report.Scenarios.Count} scenarios: {string.Join("; ", report.Scenarios)}");

        var trackedCalls = tracked.SelectMany(t => t.Calls).ToHashSet();
        foreach (var call in report.Background.Where(b => trackedCalls.Contains(b.Path)))
            problems.Add($"{call.Path} is in the background section{(call.ExpiredFrom is null ? "" : $", expired from {call.ExpiredFrom}")}");

        Assert.True(problems.Count == 0, $"{run.Variant}:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }
}
