using Kronikol;
using Kronikol.Reports;
using Kronikol.Tracking;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

namespace Kronikol.NUnit4;

internal static class TestContextEnumerableExtensions
{
    public static Feature[] ToFeatures(this IEnumerable<TestContext> contexts)
    {
        return contexts
            .GroupBy(x => x.Test.DisplayName)
            .OrderBy(x => x.Key)
            .Select(scenariosForFeature =>
            {
                var firstScenarioForFeature = scenariosForFeature.First();
                // The feature's are the categories all its tests have at its level, not the first test's (5.2.7): the
                // fixtures of a class with two [TestFixture]s, each with its own Category, are one feature, and so are two
                // classes of one name. A test keeps the rest of its fixture's on its own.
                var featureTags = ScenarioTags.Shared(scenariosForFeature.Select(x => ScenarioTags.Classify(Levels(x.Test).Feature).Tags));

                return new Feature
                {
                    DisplayName = firstScenarioForFeature.Test.DisplayName!.Titleize(),
                    // the ids key on 5.9.0's name, which Titleize can now differ from (plans/REPORT_UI_FIXES_PLAN.md N3)
                    StableIdName = StringCasing.TitleizeAsOf590(firstScenarioForFeature.Test.DisplayName!),
                    Endpoint = EndpointOf(firstScenarioForFeature.Test),
                    Tags = featureTags.Length > 0 ? featureTags : null,
                    Scenarios = scenariosForFeature
                        .DistinctBy(x => x.Test.ID)
                        .OrderByDescending(x => IsHappyPath(x.Test))
                        .ThenBy(x => x.Test.MethodName)
                        .Select(x =>
                        {
                            var name = ScenarioTitleResolver.FormatLaneScenarioName(x.Test.Name!, x.Test.MethodName ?? "");

                            var structuredResult = TryExtractStructuredParametersWithRaw(x);
                            var structuredParams = structuredResult?.StringValues;
                            Dictionary<string, object?>? rawValues = structuredResult?.RawValues;
                            // a name the test wrote whole holds no arguments, whatever its brackets say
                            var parsed = structuredParams ?? (name.ReadArguments ? ParameterParser.Parse(name.Shown) : null);
                            var outlineId = parsed is { Count: > 0 } ? (structuredParams is not null ? GetStructuredOutlineId(x) : ParameterParser.ExtractBaseName(name.Shown)) : null;

                            // 5.16.4's reading of the same test, which the id keys on where this one differs (plans/TITLES_AS_WRITTEN_PLAN.md 4.4)
                            var frozenName = ScenarioTitleResolver.FormatScenarioDisplayNameAsOf5164(x.Test.Name!);
                            var frozenParsed = structuredParams ?? ParameterParser.Parse(frozenName);
                            var frozenKey = new ScenarioIdKey(frozenName,
                                frozenParsed is { Count: > 0 } ? (structuredParams is not null ? GetStructuredOutlineId(x) : ParameterParser.ExtractBaseName(frozenName)) : null,
                                frozenParsed is { Count: > 0 } ? frozenParsed : null);
                            var levels = Levels(x.Test);
                            var tags = ScenarioTags.Merge(ScenarioTags.Classify(levels.Feature).Tags, ScenarioTags.Classify(levels.Scenario).Tags)
                                .Except(featureTags, ScenarioTags.Comparer).ToArray();

                            return new Scenario
                            {
                                Id = x.Test.ID,
                                Result = x.Result.Outcome.Status.ToExecutionResult(),
                                DisplayName = name.Shown,
                                NameAsWritten = name.Written,
                                StableIdKey = ScenarioStableId.KeyWhereChanged(frozenKey, name.Shown, outlineId, parsed is { Count: > 0 } ? parsed : null),
                                IsHappyPath = IsHappyPath(x.Test),
                                Tags = tags.Length > 0 ? tags : null,
                                ErrorMessage = ScenarioOutcome.Message(x.Result.Outcome.Status, x.Result.Message),
                                ErrorStackTrace = ScenarioOutcome.StackTrace(x.Result.Outcome.Status, x.Result.StackTrace),
                                Duration = DiagrammedTestRun.TestDurations.TryGetValue(x.Test.ID, out var dur) ? dur : null,
                                EndedAt = DiagrammedTestRun.TestEnds.TryGetValue(x.Test.ID, out var endedAt) ? endedAt : null,
                                OutlineId = outlineId,
                                OutlineTitle = outlineId is null ? null : ScenarioTitleResolver.FormatScenarioDisplayName(x.Test.MethodName ?? x.Test.Name!),
                                ExampleValues = parsed is { Count: > 0 } ? parsed : null,
                                ExampleRawValues = rawValues,
                                Steps = StepCollector.GetSteps(x.Test.ID) is { Length: > 0 } steps ? steps : null,
                                Attachments = StepCollector.GetScenarioAttachments(x.Test.ID)
                            };
                        }).ToArray()
                };
            }).ToArray();
    }

    /// <summary>
    /// A test's categories by level (plans/TAGS_PLAN.md §4.7): the test's own and, for a <c>[TestCase]</c>, its method
    /// suite's (which holds the method's <c>[Category]</c>) are the scenario's, nearest first as NUnit's
    /// <c>AllCategories()</c> lists them; the fixture's and every suite above it, a set-up fixture and the assembly (Q12),
    /// are the feature's.
    /// </summary>
    private static (List<string> Scenario, List<string> Feature) Levels(TestContext.TestAdapter test)
    {
        var scenario = new List<string>(Categories(test.Properties));
        var feature = new List<string>();
        for (var suite = test.Parent; suite is not null; suite = suite.Parent)
            (suite.Method is not null ? scenario : feature).AddRange(Categories(suite.Properties));
        return (scenario, feature);
    }

    private static IEnumerable<string> Categories(IPropertyBag properties) =>
        properties.ContainsKey(PropertyNames.Category) ? properties[PropertyNames.Category].OfType<string>() : [];

    private static IEnumerable<string> Categories(TestContext.PropertyBagAdapter properties) =>
        properties.ContainsKey(PropertyNames.Category) ? properties[PropertyNames.Category].OfType<string>() : [];

    /// <summary>
    /// <c>[HappyPath]</c> on the test, or on a <c>[TestCase]</c> method, whose cases do not carry it (F14), or a happy-path
    /// category.
    /// </summary>
    private static bool IsHappyPath(TestContext.TestAdapter test) =>
        test.Properties.ContainsKey(HappyPathAttribute.HappyPathPropertyKey)
        || test.Parent is { Method: not null } suite && suite.Properties.ContainsKey(HappyPathAttribute.HappyPathPropertyKey)
        || ScenarioTags.Classify(Levels(test).Scenario).IsHappyPath;

    /// <summary>The nearest suite's <c>[Endpoint]</c>: a <c>[TestCase]</c>'s parent is its method suite, not its fixture (F14).</summary>
    private static string? EndpointOf(TestContext.TestAdapter test)
    {
        for (var suite = test.Parent; suite is not null; suite = suite.Parent)
            if (suite.Properties.ContainsKey(EndpointAttribute.EndpointPropertyKey))
                return suite.Properties[EndpointAttribute.EndpointPropertyKey][0]?.ToString();
        return null;
    }

    private static (Dictionary<string, string> StringValues, Dictionary<string, object?> RawValues)? TryExtractStructuredParametersWithRaw(TestContext context)
    {
        try
        {
            var args = context.Test.Arguments;
            var methodParams = context.Test.MethodInfo?.GetParameters()?.ToArray();
            if (args is not { Length: > 0 } || methodParams is not { Length: > 0 })
                return null;

            var paramNames = methodParams.Select(p => p.Name).ToArray();
            return ParameterParser.ExtractStructuredParametersWithRaw(args, paramNames);
        }
        catch
        {
            return null;
        }
    }

    private static string? GetStructuredOutlineId(TestContext context)
    {
        return context.Test.MethodName;
    }
}
