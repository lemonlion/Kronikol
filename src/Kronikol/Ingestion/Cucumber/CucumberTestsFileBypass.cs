using Kronikol.Reports;

namespace Kronikol.Ingestion.Cucumber;

/// <summary>
/// A tests-file <c>step</c> record that says <c>bypassed</c>, to be applied to the Gherkin step it reports on when the
/// Cucumber messages own its scenario (#105). The reporter's step records are dropped for such a scenario, so without
/// this its bypass would be lost with them.
/// </summary>
/// <remarks>
/// The record finds its step by text: the k-th level-0 record of a test with a given text is the k-th Gherkin step of
/// the scenario with that text, background steps first, in the order they ran. A match by text survives a reporter that
/// writes steps of its own (a setup step, a hook) that the messages do not have, where a match by position would bypass
/// the wrong step. A nested record (<c>level</c> above 0) matches no Gherkin step.
/// </remarks>
/// <param name="TestId">The test the record belongs to: the scenario id the messages' <c>kronikol-test-id</c> carries.</param>
/// <param name="Text">The record's step text, trimmed.</param>
/// <param name="Occurrence">
/// Which of its test's level-0 step records with this text it is, counting from 1 and counting every status; 0 for a
/// nested record.
/// </param>
/// <param name="Reason">The record's <c>bypassReason</c>, or null.</param>
internal sealed record CucumberTestsFileBypass(string TestId, string Text, int Occurrence, string? Reason)
{
    /// <summary>
    /// The bypassed step records of every test in <paramref name="records"/>, in file order, each read from its test's
    /// last attempt: the step records after the test's last <c>start</c>, since the report shows a retried test's last
    /// attempt.
    /// </summary>
    public static IReadOnlyList<CucumberTestsFileBypass> Collect(IEnumerable<TestRunRecord> records)
    {
        var stepsByTest = new Dictionary<string, List<TestRunRecord>>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            if (string.IsNullOrWhiteSpace(record.TestId))
                continue;
            if (string.Equals(record.Event, "start", StringComparison.OrdinalIgnoreCase))
            {
                stepsByTest[record.TestId] = [];
            }
            else if (string.Equals(record.Event, "step", StringComparison.OrdinalIgnoreCase))
            {
                if (!stepsByTest.TryGetValue(record.TestId, out var steps))
                    stepsByTest[record.TestId] = steps = [];
                steps.Add(record);
            }
        }

        var bypasses = new List<CucumberTestsFileBypass>();
        foreach (var (testId, steps) in stepsByTest)
        {
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var step in steps)
            {
                var text = step.Text?.Trim() ?? "";
                var occurrence = step.Level is > 0 ? 0 : seen[text] = seen.GetValueOrDefault(text) + 1;
                if (FeatureSynthesizer.MapStatus(step.Status) == ExecutionResult.Bypassed)
                    bypasses.Add(new CucumberTestsFileBypass(testId, text, occurrence, CucumberFeatureSynthesizer.NullIfBlank(step.BypassReason)));
            }
        }

        return bypasses;
    }
}
