using Reqnroll;
using Kronikol;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.ReqNRoll;

internal static class ScenarioInfoEnumerableExtensions
{
    public static Feature[] ToFeatures(this IEnumerable<ReqNRollScenarioInfo> scenarios)
    {
        return scenarios
            .GroupBy(x => x.FeatureTitle)
            .OrderBy(x => x.Key)
            .Select(featureGroup =>
            {
                var firstScenario = featureGroup.First();
                // The feature's own tags, FeatureInfo.Tags (F9: 4.x took the first scenario's combined tags less its own,
                // which lost a feature tag it repeated and lifted its rule's to every scenario), as every file of the feature
                // has them: two files with one title are one feature, and until 5.2.7 it took the first scenario's file's.
                // The endpoint is the feature's, else the first scenario's that has one.
                var feature = ScenarioTags.Classify(firstScenario.FeatureTags);
                var featureTags = ScenarioTags.Shared(featureGroup.Select(s => ScenarioTags.Classify(s.FeatureTags).Tags));
                var endpoint = feature.Endpoint
                    ?? featureGroup.Select(s => ScenarioTags.Classify(s.CombinedTags).Endpoint).FirstOrDefault(e => e is not null);

                var scenarios = featureGroup
                        .DistinctBy(x => x.ScenarioId)
                        .OrderByDescending(x => ScenarioTags.Classify(x.ScenarioTags).IsHappyPath)
                        .ThenBy(x => x.ScenarioTitle)
                        // Rows must group by their Examples: block before the value ordering, or
                        // rows from different blocks interleave. Blockless rows sort last (never
                        // mixed with indexed ones) and keep today's ordering among themselves.
                        .ThenBy(x => x.ExamplesBlockIndex ?? int.MaxValue)
                        .ThenBy(x => x.ExampleValues is { Count: > 0 }
                            ? string.Join("|", x.ExampleValues.Values)
                            : "")
                        .Select(x =>
                        {
                            // its combined tags (its own, its rule's and its Examples block's among them) less its
                            // feature's (plans/TAGS_PLAN.md §4.5); the happy path from its own and its Examples block's
                            var tags = ScenarioTags.Classify(x.CombinedTags).Tags.Except(featureTags, ScenarioTags.Comparer).ToArray();

                            // The row's name as Gherkin names its pickle (#101). A row whose name differs from its outline's
                            // title keys its id and its place on the title, as it did when it was named by it.
                            var displayName = x.RowName ?? x.ScenarioTitle;

                            // Every step is this scenario's own, as it ran; those a Background: gave it come first (#222).
                            var rows = x.Steps.Where(step => !step.RanByAStep).ToList();
                            var ran = rows.Count > 0 ? MapSteps(rows, StepCollector.GetSteps(x.ScenarioId)) : [];
                            var backgroundCount = CountBackgroundSteps(ran, x.WrittenBackground ?? []);

                            return new Scenario
                            {
                                Id = x.ScenarioId,
                                DisplayName = displayName,
                                // a Gherkin name, shown as written (5.9.1, plans/REPORT_UI_FIXES_PLAN.md N4, N5)
                                NameAsWritten = true,
                                KeyedByOutline = x.OutlineId is not null && !string.Equals(displayName, x.OutlineId, StringComparison.Ordinal),
                                Description = GherkinText.NullIfBlank(GherkinText.Dedent(x.ScenarioDescription)),
                                IsHappyPath = ScenarioTags.Classify(x.ScenarioTags).IsHappyPath,
                                Result = x.ExecutionStatus.ToExecutionResult(),
                                ErrorMessage = x.TestError?.Message,
                                ErrorStackTrace = x.TestError?.StackTrace,
                                Duration = x.Duration,
                                EndedAt = x.EndedAt,
                                BackgroundSteps = backgroundCount > 0 ? ran[..backgroundCount] : null,
                                Steps = ran.Length > backgroundCount ? ran[backgroundCount..] : null,
                                Tags = tags.Length > 0 ? tags : null,
                                Rule = x.Rule,
                                SourceFile = x.SourceFile,
                                SourceLine = x.SourceLine,
                                OutlineId = x.OutlineId,
                                ExampleValues = x.ExampleValues,
                                ExampleRawValues = x.ExampleRawValues,
                                ExampleFlatValues = x.ExampleFlatValues,
                                ExamplesBlockName = x.ExamplesBlockName,
                                ExamplesBlockDescription = x.ExamplesBlockDescription,
                                ExamplesBlockIndex = x.ExamplesBlockIndex,
                                Attachments = StepCollector.GetScenarioAttachments(x.ScenarioId),
                            };
                        }).ToArray();

                // Scenarios built by hand have no document to read, so what they share is still only guessed at.
                if (featureGroup.All(x => x.WrittenBackground is null))
                    BackgroundStepsDetector.DetectAndExtract(scenarios);

                return new Feature
                {
                    // as written, as the Cucumber lane shows it (5.9.1, plans/REPORT_UI_FIXES_PLAN.md N1; until then a
                    // class-name humaniser made "Month's reports via gRPC" "Month S Reports Via G RPC"), its scenarios
                    // keeping the ids 5.9.0 computed from that name (N3)
                    DisplayName = featureGroup.Key,
                    StableIdName = StringCasing.TitleizeAsOf590(featureGroup.Key),
                    Endpoint = endpoint,
                    Description = GherkinText.NullIfBlank(GherkinText.Dedent(firstScenario.FeatureDescription)),
                    // Scenarios are grouped by feature title, so any member's path is the feature's -
                    // and the first that has one wins, matching the Cucumber lane.
                    SourceFile = featureGroup.Select(x => x.SourceFile).FirstOrDefault(f => f is not null),
                    Tags = featureTags.Length > 0 ? featureTags : null,
                    Scenarios = scenarios
                };
            }).ToArray();
    }

    private static ScenarioStep[] MapSteps(List<ReqNRollStepInfo> steps, ScenarioStep[]? collectedSteps = null)
    {
        var mapped = new ScenarioStep[steps.Count];
        var priorFailure = false;
        for (var i = 0; i < steps.Count; i++)
        {
            mapped[i] = new ScenarioStep
            {
                Keyword = steps[i].Keyword,
                Text = steps[i].Text,
                Status = steps[i].Status.ToStepResult(priorFailure),
                Duration = steps[i].Duration,
                DocString = steps[i].DocString,
                Parameters = ParseTableText(steps[i].TableText),
                TextSegments = BuildTextSegments(steps[i]),
                SubSteps = collectedSteps is not null && i < collectedSteps.Length
                    ? DescribeNested(collectedSteps[i].SubSteps, steps[i].Nested)
                    : null,
                Attachments = collectedSteps is not null && i < collectedSteps.Length
                    ? collectedSteps[i].Attachments
                    : null,
            };
            if (steps[i].Status == ScenarioExecutionStatus.TestError || steps[i].Status == ScenarioExecutionStatus.BindingError)
                priorFailure = true;
        }
        return mapped;
    }

    /// <summary>
    /// How many rows, from the first, are the Background the document writes: a failed step ends a scenario early, and a
    /// row that disagrees with the document is never counted as it.
    /// </summary>
    internal static int CountBackgroundSteps(IReadOnlyList<ScenarioStep> ran, IReadOnlyList<string> writtenBackground)
    {
        var count = 0;
        while (count < ran.Count && count < writtenBackground.Count
               && string.Equals(ran[count].Text, writtenBackground[count], StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    /// <summary>
    /// The steps a step definition ran, as the hooks saw them. The collector holds their place among the woven steps, but
    /// not their table, doc string or inline values. Each is found by what opened it, so a woven step that reads the same stays
    /// what it was, under any woven step between.
    /// </summary>
    private static ScenarioStep[]? DescribeNested(ScenarioStep[]? subSteps, IReadOnlyList<ReqNRollStepInfo>? nested)
    {
        if (subSteps is null || nested is not { Count: > 0 })
            return subSteps;

        foreach (var subStep in subSteps)
        {
            var step = subStep.OpenedBy is null ? null : nested.FirstOrDefault(s => ReferenceEquals(s.Nested, subStep.OpenedBy));
            if (step is null)
            {
                DescribeNested(subStep.SubSteps, nested);
                continue;
            }

            subStep.Status = step.Status.ToStepResult(false);
            subStep.DocString = step.DocString;
            subStep.Parameters = ParseTableText(step.TableText);
            subStep.TextSegments = BuildTextSegments(step);
            DescribeNested(subStep.SubSteps, step.Nested);
        }

        return subSteps;
    }

    private static StepTextSegment[]? BuildTextSegments(ReqNRollStepInfo step)
    {
        if (step.InlineParams is not { Length: > 0 })
            return null;

        var segments = new List<StepTextSegment>();
        var text = step.Text;
        var lastEnd = 0;

        // Sort by offset to process left-to-right
        var sortedParams = step.InlineParams.OrderBy(p => p.StartOffset).ToArray();

        foreach (var param in sortedParams)
        {
            // Add literal text before this parameter
            if (param.StartOffset > lastEnd)
            {
                segments.Add(StepTextSegment.Literal(text[lastEnd..param.StartOffset]));
            }

            var paramValue = new InlineParameterValue(
                param.Value,
                null,
                VerificationStatus.NotApplicable);
            segments.Add(StepTextSegment.Param(param.Name, paramValue));

            lastEnd = param.StartOffset + param.Length;
        }

        // Add remaining literal text
        if (lastEnd < text.Length)
        {
            segments.Add(StepTextSegment.Literal(text[lastEnd..]));
        }

        return segments.Count > 0 ? segments.ToArray() : null;
    }

    private static StepParameter[]? ParseTableText(string? tableText)
    {
        if (string.IsNullOrWhiteSpace(tableText))
            return null;

        var lines = tableText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) // Need at least header + one data row
            return null;

        var headers = ParseTableRow(lines[0]);
        if (headers.Length == 0)
            return null;

        var columns = headers.Select(h => new TabularColumn(h, false)).ToArray();
        var rows = new List<TabularRow>();

        for (var i = 1; i < lines.Length; i++)
        {
            var cells = ParseTableRow(lines[i]);
            if (cells.Length == 0)
                continue;

            // Pad or truncate to match header count
            var tabularCells = new TabularCell[columns.Length];
            for (var j = 0; j < columns.Length; j++)
            {
                var value = j < cells.Length ? cells[j] : "";
                tabularCells[j] = new TabularCell(value, null, VerificationStatus.NotApplicable);
            }
            rows.Add(new TabularRow(TableRowType.Matching, tabularCells));
        }

        if (rows.Count == 0)
            return null;

        return [new StepParameter
        {
            Name = "table",
            Kind = StepParameterKind.Tabular,
            TabularValue = new TabularParameterValue(columns, rows.ToArray())
        }];
    }

    private static string[] ParseTableRow(string line)
    {
        var trimmed = line.Trim();
        if (!trimmed.StartsWith('|') || !trimmed.EndsWith('|'))
            return [];

        return trimmed[1..^1]
            .Split('|')
            .Select(cell => cell.Trim())
            .ToArray();
    }
}
