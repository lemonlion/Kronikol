namespace Kronikol.Tests.xUnit2.Lane;

/// <summary>
/// The wiki's alternative to the test framework: a collection fixture whose Dispose writes the reports through
/// the public overload that takes no scenarios. Nothing on that path sees a result.
/// </summary>
[Collection(FixtureLaneCollection.Name)]
public class NoFrameworkLaneTests
{
    private static FixtureRun Run() => FixtureLane.Run(new FixtureVariant("NoFramework"));

    // T14
    [Fact]
    public void Every_scenario_is_reported_as_a_default_not_a_verdict()
    {
        var run = Run();
        var report = run.Report;
        Assert.NotEmpty(report.Scenarios);

        var defaulted = Assert.Single(report.Diagnostics, d => d.Kind == "ResultDefaulted");
        Assert.Contains($"{report.Scenarios.Count} scenario(s)", defaulted.Message);

        var failures = run.ReadReportFile("Failures.md") ?? throw new Xunit.Sdk.XunitException("No Failures.md");
        Assert.Contains(defaulted.Message, failures);
    }

    // T14
    [Fact]
    public void The_specifications_are_not_written_as_for_a_passing_run()
    {
        var specifications = Run().ReadReportFile("Specifications.html");

        Assert.NotNull(specifications);
        Assert.Equal("", specifications);
    }
}
