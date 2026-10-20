using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class ScenarioStepModelTests
{
    [Fact]
    public void ScenarioStep_can_be_created_with_keyword_and_text()
    {
        var step = new ScenarioStep { Keyword = "Given", Text = "a valid request" };
        Assert.Equal("Given", step.Keyword);
        Assert.Equal("a valid request", step.Text);
    }

    [Fact]
    public void ScenarioStep_keyword_is_optional()
    {
        var step = new ScenarioStep { Text = "some step without keyword" };
        Assert.Null(step.Keyword);
    }

    [Fact]
    public void ScenarioStep_supports_status_and_duration()
    {
        var step = new ScenarioStep
        {
            Keyword = "When",
            Text = "the request is sent",
            Status = ExecutionResult.Passed,
            Duration = TimeSpan.FromMilliseconds(500)
        };
        Assert.Equal(ExecutionResult.Passed, step.Status);
        Assert.Equal(TimeSpan.FromMilliseconds(500), step.Duration);
    }

    [Fact]
    public void ScenarioStep_supports_substeps()
    {
        var step = new ScenarioStep
        {
            Keyword = "Given",
            Text = "a valid request body",
            SubSteps =
            [
                new ScenarioStep { Keyword = "And", Text = "the body specifies milk" },
                new ScenarioStep { Keyword = "And", Text = "the body specifies eggs" }
            ]
        };
        Assert.Equal(2, step.SubSteps!.Length);
        Assert.Equal("And", step.SubSteps[0].Keyword);
    }

    [Fact]
    public void ScenarioStep_supports_comments()
    {
        var step = new ScenarioStep
        {
            Keyword = "Then",
            Text = "the response should be successful",
            Comments = ["Verifying 200 OK", "Also checking response body"]
        };
        Assert.Equal(2, step.Comments!.Length);
    }

    [Fact]
    public void ScenarioStep_supports_attachments()
    {
        var step = new ScenarioStep
        {
            Keyword = "Then",
            Text = "the page renders correctly",
            Attachments = [new FileAttachment("screenshot.png", "Reports/screenshot.png")]
        };
        Assert.Single(step.Attachments!);
        Assert.Equal("screenshot.png", step.Attachments[0].Name);
        Assert.Equal("Reports/screenshot.png", step.Attachments[0].RelativePath);
    }

    [Fact]
    public void Scenario_supports_steps_property()
    {
        var scenario = new Scenario
        {
            Id = "test-1",
            DisplayName = "Creating a cake",
            Steps =
            [
                new ScenarioStep { Keyword = "Given", Text = "a valid request" },
                new ScenarioStep { Keyword = "When", Text = "the request is sent" },
                new ScenarioStep { Keyword = "Then", Text = "the response is successful" }
            ]
        };
        Assert.Equal(3, scenario.Steps!.Length);
    }

    [Fact]
    public void Scenario_steps_is_null_by_default()
    {
        var scenario = new Scenario { Id = "test-1", DisplayName = "Simple test" };
        Assert.Null(scenario.Steps);
    }

    [Fact]
    public void Scenario_supports_tags()
    {
        var scenario = new Scenario
        {
            Id = "test-1",
            DisplayName = "Test",
            Tags = ["smoke", "Orders"]
        };
        Assert.Equal(["smoke", "Orders"], scenario.Tags);
    }

    [Fact]
    public void Feature_supports_description()
    {
        var feature = new Feature
        {
            DisplayName = "Cake",
            Description = "As a dessert provider, I want to create cakes"
        };
        Assert.Equal("As a dessert provider, I want to create cakes", feature.Description);
    }

    [Fact]
    public void Feature_supports_tags()
    {
        var feature = new Feature
        {
            DisplayName = "Cake",
            Tags = ["api", "v2"]
        };
        Assert.Equal(["api", "v2"], feature.Tags);
    }

    [Fact]
    public void ExecutionResult_has_bypassed_and_skipped_after_failure_values()
    {
        Assert.Equal(3, (int)ExecutionResult.Bypassed);
        Assert.Equal(4, (int)ExecutionResult.SkippedAfterFailure);
    }

    [Fact]
    public void ExecutionResult_original_values_unchanged()
    {
        Assert.Equal(0, (int)ExecutionResult.Passed);
        Assert.Equal(1, (int)ExecutionResult.Failed);
        Assert.Equal(2, (int)ExecutionResult.Skipped);
    }
}
