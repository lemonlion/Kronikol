using Kronikol.Reports;
using Kronikol.Tracking;
using Xunit;
using Xunit.v3;

namespace Kronikol.xUnit3;

internal static class TestContextEnumerableExtensions
{
    public static Feature[] ToFeatures(this IEnumerable<ITestContext> contexts)
    {
        return contexts
            .Where(x => x.Test is not null && x.TestClass is not null)
            .OrderBy(x => x.TestClass!.TestClassSimpleName)
            .GroupBy(x => x.TestClass!.TestClassSimpleName)
            .Select(scenariosForFeature =>
            {
                var featureClass = scenariosForFeature.First().TestClass!;
                return new Feature
                {
                    DisplayName = ScenarioTitleResolver.FormatFeatureName(scenariosForFeature.Key),
                    Endpoint = featureClass.Traits.SingleOrDefault(y => y.Key == EndpointAttribute.EndpointTraitKey).Value?.FirstOrDefault(),
                    Scenarios = scenariosForFeature
                        .DistinctBy(x => x.Test!.UniqueID)
                        .OrderByDescending(x => x.TestMethod!.Traits.ContainsKey(HappyPathAttribute.HappyPathTraitKey))
                        .ThenBy(x => x.Test?.TestDisplayName)
                        .Select(x =>
                        {
                            var displayName = ScenarioTitleResolver.FormatScenarioDisplayName(x.Test!.TestDisplayName);

                            // Try structured extraction from TestMethodArguments first
                            var structuredResult = TryExtractStructuredParametersWithRaw(x);
                            var structuredParams = structuredResult?.StringValues;
                            Dictionary<string, object?>? rawValues = structuredResult?.RawValues;
                            var parsed = structuredParams ?? ParameterParser.Parse(displayName);

                            return new Scenario
                            {
                                Id = x.Test!.UniqueID,
                                Result = x.TestState!.Result.ToExecutionResult(),
                                DisplayName = displayName,
                                IsHappyPath = x.Test!.Traits.ContainsKey(HappyPathAttribute.HappyPathTraitKey),
                                // The message is what was thrown, and nothing else. Until 3.1.0 the
                                // FailureCause enum was joined onto the front of it, which made the first
                                // line of every failure in the run the same word - and the first line is
                                // what the failures digest and the HTML cluster panel group on, so a run
                                // of unrelated assertion failures read as one cause with one worked
                                // example. It is carried in its own field below. The join also ran for
                                // PASSING tests, where it produced a bare line separator: measured on
                                // Example.Api.Tests.Component.xUnit3, three of three passing scenarios
                                // shipped `"errorMessage": "\r\n"`.
                                ErrorMessage = FailureText.Join(x.TestState!.ExceptionMessages),
                                ErrorStackTrace = FailureText.Join(x.TestState!.ExceptionStackTraces),
                                FailureCause = x.TestState!.Result.ToExecutionResult() == ExecutionResult.Failed
                                    ? FailureText.OrNull(x.TestState!.FailureCause?.ToString())
                                    : null,
                                Duration = x.TestState!.ExecutionTime is > 0 ? TimeSpan.FromMilliseconds((double)x.TestState.ExecutionTime.Value) : null,
                                EndedAt = DiagrammedTestRun.TestEnds.TryGetValue(x.Test!.UniqueID, out var endedAt) ? endedAt : null,
                                OutlineId = parsed is { Count: > 0 } ? (structuredParams is not null ? GetStructuredOutlineId(x) : ParameterParser.ExtractBaseName(displayName)) : null,
                                ExampleValues = parsed is { Count: > 0 } ? parsed : null,
                                ExampleRawValues = rawValues,
                                Steps = StepCollector.GetSteps(x.Test!.UniqueID) is { Length: > 0 } steps ? steps : null,
                                Attachments = StepCollector.GetScenarioAttachments(x.Test!.UniqueID)
                            };
                        }).ToArray()
                };
            }).ToArray();
    }

    private static (Dictionary<string, string> StringValues, Dictionary<string, object?> RawValues)? TryExtractStructuredParametersWithRaw(ITestContext context)
    {
        try
        {
            if (context.TestCase is not XunitTestCase testCase)
                return null;

            // TestMethodArguments are cleared by xUnit3 after test execution.
            // Read from the static store populated by DiagrammedComponentTest constructor.
            object?[]? args = null;
            var testId = context.Test?.UniqueID;
            if (testId is not null)
            {
                DiagrammedComponentTest.CapturedTestMethodArguments.TryRemove(testId, out var captured);
                args = captured;
            }

            // Fallback to direct access (works if called during test execution)
            args ??= testCase.TestMethodArguments;

            var parameters = testCase.TestMethod.Parameters;
            if (args is not { Length: > 0 } || parameters is not { Count: > 0 })
                return null;

            var paramNames = parameters.Select(p => p.Name).ToArray();
            return ParameterParser.ExtractStructuredParametersWithRaw(args, paramNames);
        }
        catch
        {
            return null;
        }
    }

    private static string? GetStructuredOutlineId(ITestContext context)
    {
        if (context.TestCase is XunitTestCase testCase)
            return testCase.TestMethod.Method?.Name;
        return null;
    }
}