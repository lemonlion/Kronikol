using System.Reflection;
using TestStack.BDDfy;
using Kronikol.xUnit3;

namespace Kronikol.BDDfy.xUnit3;

/// <summary>
/// BDDfy processor that intercepts scenario execution to capture diagram data for test tracking.
/// </summary>
public class DiagramCapturingProcessor : IProcessor
{
    private static readonly FieldInfo? ScenarioTitleField =
        typeof(Scenario).GetField("<Title>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly PropertyInfo? ScenarioTitleProperty =
        typeof(Scenario).GetProperty(nameof(Scenario.Title));

    public ProcessType ProcessType => ProcessType.Report;

    public void Process(Story story)
    {
        var testContext = Xunit.TestContext.Current;
        var testId = testContext.Test?.UniqueID;
        if (testId == null) return;

        var storyTitle = story.Metadata?.Title
            ?? story.Namespace?.Split('.').LastOrDefault()
            ?? "Unknown Feature";

        var storyDescription = BuildStoryDescription(story.Metadata);

        var testClassSimpleName = testContext.TestClass?.TestClassSimpleName;
        var testMethodName = testContext.TestMethod?.MethodName;

        // Capture raw test method arguments from xUnit3's TestContext — same mechanism
        // as the non-BDDfy xUnit3 adapter for consistent processing of framework-level
        // parameterized data (MemberData, ClassData, InlineData, etc.)
        var (rawArgs, paramNames) = XUnit3ArgumentExtractor.Extract(testContext);

        foreach (var scenario in story.Scenarios)
        {
            var resolvedTitle = ScenarioTitleResolver.ResolveScenarioTitle(
                scenario.Title, testClassSimpleName, testMethodName);

            // For parameterised tests (e.g. [Theory] / [InlineData]), append the
            // parameter values from the xUnit display name so each iteration gets a
            // unique, descriptive title instead of a plain numeric suffix like "(1)".
            resolvedTitle = ScenarioTitleResolver.AppendTestParameters(
                resolvedTitle, testContext.Test?.TestDisplayName);

            // Fix the title in-place so BDDfy's own reporters see unique titles
            if (ScenarioTitleField != null)
                ScenarioTitleField.SetValue(scenario, resolvedTitle);
            else
                ScenarioTitleProperty?.SetValue(scenario, resolvedTitle);

            var steps = scenario.Steps
                .Where(s => s.ShouldReport)
                .OrderBy(s => s.ExecutionOrder)
                .Select(s => s.ToBDDfyStepInfo())
                .ToList();

            var failedException = scenario.Steps
                .FirstOrDefault(s => s.Result == TestStack.BDDfy.Result.Failed && s.Exception != null)
                ?.Exception;

            BDDfyScenarioCollector.Collect(new BDDfyScenarioInfo
            {
                TestId = testId,
                BDDfyScenarioId = scenario.Id,
                StoryTitle = storyTitle,
                StoryDescription = storyDescription,
                ScenarioTitle = resolvedTitle,
                Tags = scenario.Tags?.ToArray() ?? [],
                Steps = steps,
                Result = scenario.Result,
                Duration = scenario.Duration,
                EndedAt = DateTimeOffset.UtcNow,
                ErrorMessage = failedException?.Message,
                ErrorStackTrace = failedException?.StackTrace,
                RawArguments = rawArgs,
                ParameterNames = paramNames,
                TestMethodName = testMethodName,
            });
        }
    }

    private static string? BuildStoryDescription(StoryMetadata? metadata)
    {
        if (metadata == null) return null;

        var parts = new[] { metadata.Narrative1, metadata.Narrative2, metadata.Narrative3 }
            .Where(n => !string.IsNullOrWhiteSpace(n));

        var description = string.Join("\n", parts);
        return string.IsNullOrWhiteSpace(description) ? null : description;
    }
}