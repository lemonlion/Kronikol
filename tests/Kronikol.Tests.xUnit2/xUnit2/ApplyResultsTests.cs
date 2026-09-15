using Kronikol.Reports;
using Kronikol.xUnit2;
using Xunit.Abstractions;

namespace Kronikol.Tests.xUnit2;

[Collection("CollectedScenarios")]
public class ApplyResultsTests : IDisposable
{
    public ApplyResultsTests()
    {
        XUnit2TestTrackingContext.CollectedScenarios.Clear();
    }

    public void Dispose()
    {
        XUnit2TestTrackingContext.CollectedScenarios.Clear();
    }

    [Fact]
    public void InlineData_scenarios_get_unique_display_names_with_parameters()
    {
        // Arrange: simulate TestTrackingAttribute.Before() creating two scenarios
        // for the same [Theory] method with different [InlineData] — the attribute
        // only has MethodInfo so both scenarios initially get the same name.
        XUnit2TestTrackingContext.CollectedScenarios["id1"] = new ScenarioInfo
        {
            Id = "id1",
            FeatureName = "Delta File",
            ScenarioName = "Loads successfully", // no parameters — the bug
            MethodMatchKey = "Tests.DeltaFileTests.LoadsSuccessfully",
            IsHappyPath = true
        };
        XUnit2TestTrackingContext.CollectedScenarios["id2"] = new ScenarioInfo
        {
            Id = "id2",
            FeatureName = "Delta File",
            ScenarioName = "Loads successfully", // same name — the bug
            MethodMatchKey = "Tests.DeltaFileTests.LoadsSuccessfully",
            IsHappyPath = true
        };

        var sink = new TestResultCapturingSink(new StubMessageSink());
        sink.Outcomes.Add(new TestOutcome
        {
            DisplayName = "Tests.DeltaFileTests.LoadsSuccessfully(balanceMovementType: \"Purchase\")",
            Result = ExecutionResult.Passed
        });
        sink.Outcomes.Add(new TestOutcome
        {
            DisplayName = "Tests.DeltaFileTests.LoadsSuccessfully(balanceMovementType: \"Refund\")",
            Result = ExecutionResult.Passed
        });

        // Act
        sink.ApplyResults();

        // Assert: scenario names should now include the InlineData parameters
        var names = XUnit2TestTrackingContext.CollectedScenarios.Values
            .Select(s => s.ScenarioName)
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(2, names.Distinct().Count());
        Assert.Contains(names, n => n.Contains("Purchase"));
        Assert.Contains(names, n => n.Contains("Refund"));
    }

    [Fact]
    public void The_scenario_takes_the_finish_stamp_of_its_result()
    {
        XUnit2TestTrackingContext.CollectedScenarios["id1"] = new ScenarioInfo
        {
            Id = "id1",
            FeatureName = "Delta File",
            ScenarioName = "Loads successfully",
            MethodMatchKey = "Tests.DeltaFileTests.LoadsSuccessfully",
        };
        var finished = new DateTimeOffset(2026, 1, 1, 10, 0, 3, 250, TimeSpan.Zero);
        var sink = new TestResultCapturingSink(new StubMessageSink());
        sink.Outcomes.Add(new TestOutcome
        {
            DisplayName = "Tests.DeltaFileTests.LoadsSuccessfully",
            Result = ExecutionResult.Passed,
            FinishedAt = finished,
        });

        sink.ApplyResults();

        Assert.Equal(finished, XUnit2TestTrackingContext.CollectedScenarios["id1"].EndedAt);
    }

    [Fact]
    public void Non_parameterised_scenario_keeps_original_name()
    {
        XUnit2TestTrackingContext.CollectedScenarios["id1"] = new ScenarioInfo
        {
            Id = "id1",
            FeatureName = "Orders",
            ScenarioName = "Creates an order",
            MethodMatchKey = "Tests.OrderTests.CreatesAnOrder",
            IsHappyPath = true
        };

        var sink = new TestResultCapturingSink(new StubMessageSink());
        sink.Outcomes.Add(new TestOutcome
        {
            DisplayName = "Tests.OrderTests.CreatesAnOrder",
            Result = ExecutionResult.Passed
        });

        sink.ApplyResults();

        Assert.Equal("Creates an order", XUnit2TestTrackingContext.CollectedScenarios["id1"].ScenarioName);
    }

    #pragma warning disable xUnit3000 // Test double, not used in xUnit infrastructure
    private sealed class StubMessageSink : IMessageSink
    {
        public bool OnMessage(IMessageSinkMessage message) => true;
    }
    #pragma warning restore xUnit3000
}
