namespace Kronikol.Tests.xUnit2.Lane;

/// <summary>
/// Kronikol's reports under another test framework, through <c>WithKronikolReporting()</c>
/// (plans/XUNIT2_FRAMEWORK_COMPOSITION_PLAN.md R2, T10 and T15 to T18): Xunit.Extensions.AssemblyFixture's framework
/// subclassed, Xunit.DependencyInjection's sealed one composed, and a framework that keeps xUnit's asynchronous bus.
/// </summary>
[Collection(FixtureLaneCollection.Name)]
public class OtherFrameworksLaneTests
{
    /// <summary>The AssemblyFixture fixture per adapter: AssemblyFixture is 2.8.2, AssemblyFixture.Vs3 is 3.1.5.</summary>
    public static TheoryData<string> AssemblyFixtureAdapters => new() { "AssemblyFixture", "AssemblyFixture.Vs3" };

    // T15
    [Theory]
    [MemberData(nameof(AssemblyFixtureAdapters))]
    public void Under_AssemblyFixture_every_tracked_test_is_its_own_scenario(string fixture) =>
        Expected.EveryTrackedTestIsItsOwnScenario(FixtureLane.Run(new FixtureVariant(fixture)), "AssemblyFixtureUsers");

    // T15
    [Theory]
    [MemberData(nameof(AssemblyFixtureAdapters))]
    public void Under_AssemblyFixture_the_assembly_fixture_is_made_once_and_handed_to_each_test(string fixture)
    {
        var users = FixtureLane.Run(new FixtureVariant(fixture)).Trx!.Tests.Where(t => t.ClassSimpleName == "AssemblyFixtureUsers").ToArray();

        Assert.Equal(2, users.Length);
        Assert.All(users, u => Assert.Equal("Passed", u.Outcome));
        var lines = users.Select(u => u.Output.Split('\n').Single(l => l.StartsWith("shared-thing: ", StringComparison.Ordinal)).Trim()).ToArray();
        Assert.Single(lines.Distinct());
        Assert.EndsWith("created=1", lines[0]);
    }

    // T10: an executor whose RunTests returns before its tests have run, as AssemblyFixture's does, so only the
    // order of the reports and the runner's last message keeps them whole.
    [Theory]
    [MemberData(nameof(AssemblyFixtureAdapters))]
    public void Under_AssemblyFixture_the_reports_are_complete_when_dotnet_test_returns_even_when_slow_to_write(string fixture)
    {
        var run = FixtureLane.Run(new FixtureVariant(fixture, "slow-report", Environment: new Dictionary<string, string>
        {
            ["KRONIKOL_FIXTURE_REPORT_DELAY_MS"] = "100",
            ["KRONIKOL_FIXTURE_REPORT_DELAY_LOG"] = "{results}/delays.txt",
        }));

        var delays = Path.Combine(run.ResultsDirectory, "delays.txt");
        var waits = File.Exists(delays) ? File.ReadAllLines(delays).Length : 0;
        Assert.True(waits >= 10, $"The report waited {waits} times; a slow report needs at least 10.{Environment.NewLine}{run.Describe()}");

        foreach (var file in new[] { "TestRunReport.json", "TestRunReport.html", "Failures.md", "Run.json" })
            Assert.True(run.ReadReportFile(file) is { Length: > 0 }, $"{file} was not written.{Environment.NewLine}{run.Describe()}");
        Expected.EveryTrackedTestIsItsOwnScenario(run, "AssemblyFixtureUsers");
    }

    // T16
    [Fact]
    public void Under_DependencyInjection_by_composition_injection_works_and_every_tracked_test_is_its_own_scenario()
    {
        var run = FixtureLane.Run(new FixtureVariant("DependencyInjection"));

        var injected = Assert.Single(run.Trx!.Tests, t => t.ClassSimpleName == "Injected");
        Assert.Equal("Passed", injected.Outcome);
        Assert.Contains("injected: hello fixture", injected.Output);
        Expected.EveryTrackedTestIsItsOwnScenario(run, "Injected");

        var failing = Assert.Single(run.Report.Named("Facts", "A failing fact with a display name"));
        Assert.Equal("Failed", failing.Result);
        Assert.Equal(["/facts/displayname-fails"], failing.Calls);
    }

    // T18
    [Fact]
    public void Under_a_framework_that_keeps_the_asynchronous_bus_results_are_paired_by_test_method_and_the_run_says_so()
    {
        var run = FixtureLane.Run(new FixtureVariant("AsyncBus"));
        var report = run.Report;

        // A fact without a display name, with its result and its call, and a skipped test.
        var fails = Assert.Single(report.Named("Facts", "Fails"));
        Assert.Equal("Failed", fails.Result);
        Assert.Equal(["/facts/fails"], fails.Calls);
        Assert.Equal("Skipped", Assert.Single(report.Named("Facts", "Skipped")).Result);

        var paired = Assert.Single(report.Diagnostics, d => d.Kind == "Other" && d.Message.Contains("paired with their results by test method"));
        Assert.StartsWith("16 scenario(s)", paired.Message);

        // Run one after another, a theory's rows pair in order too, and so does a DisplayName fact.
        Expected.EveryTrackedTestIsItsOwnScenario(run);
    }
}
