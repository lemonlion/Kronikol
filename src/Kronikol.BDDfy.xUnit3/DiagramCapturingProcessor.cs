using System.Reflection;
using TestStack.BDDfy;
using TestStack.BDDfy.Configuration;
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

    /// <summary>
    /// A story scenario's tags, then the running xUnit test's <c>Category</c> traits (Q11): a BDDfy test is an xUnit test,
    /// and those are the tags the other xUnit lanes read. No other trait key is a tag (Q7).
    /// </summary>
    internal static string[] TagsOf(IEnumerable<string>? scenarioTags, Xunit.Sdk.ITest? test) =>
        [.. scenarioTags ?? [], .. (test is not null && test.Traits.TryGetValue("Category", out var categories) ? categories : [])];

    /// <summary>
    /// Whether a scenario's title is BDDfy's own (plans/TITLES_AS_WRITTEN_PLAN.md 4.5, Q11): its humaniser's reading of the
    /// method's or the class's name, or the class's name, which Kronikol reads the method's in place of. A title the test
    /// passed (<c>BDDfy("...")</c>) is none of these, and the word list leaves it as written.
    /// </summary>
    internal static bool IsOwnTitle(string title, string? testClassSimpleName, string? testMethodName) =>
        title == testClassSimpleName
        || (testMethodName is not null && title == Configurator.Scanners.Humanize(testMethodName))
        || (testClassSimpleName is not null && title == Configurator.Scanners.Humanize(testClassSimpleName));

    /// <summary>
    /// Whether a story's title is BDDfy's own: none written (Kronikol names the story from its namespace) or its
    /// humaniser's reading of the story's class's name, which is what a <c>[Story]</c> with no <c>Title</c> gives.
    /// </summary>
    internal static bool IsOwnStoryTitle(StoryMetadata? metadata) =>
        metadata is null || metadata.Title == Configurator.Scanners.Humanize(metadata.Type.Name);

    public void Process(Story story)
    {
        var testContext = Xunit.TestContext.Current;
        var testId = testContext.Test?.UniqueID;
        if (testId == null) return;

        var storyTitle = story.Metadata?.Title
            ?? story.Namespace?.Split('.').LastOrDefault()
            ?? "Unknown Feature";
        var storyTitleGenerated = IsOwnStoryTitle(story.Metadata);

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
            var titleGenerated = IsOwnTitle(scenario.Title, testClassSimpleName, testMethodName);
            // 5.16.4's title, which the id keys on where this one differs (plans/TITLES_AS_WRITTEN_PLAN.md 4.4)
            var frozenTitle = ScenarioTitleResolver.AppendTestParameters(
                ScenarioTitleResolver.ResolveScenarioTitleAsOf5164(scenario.Title, testClassSimpleName, testMethodName),
                testContext.Test?.TestDisplayName);

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
                StableIdTitle = frozenTitle == resolvedTitle ? null : frozenTitle,
                TitleGenerated = titleGenerated,
                StoryTitleGenerated = storyTitleGenerated,
                Tags = TagsOf(scenario.Tags, testContext.Test),
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