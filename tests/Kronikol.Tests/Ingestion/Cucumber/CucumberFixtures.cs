using System.Text.Json;
using Kronikol.Ingestion.Cucumber;

namespace Kronikol.Tests.Ingestion.Cucumber;

/// <summary>
/// The checked-in golden fixtures under <c>TestData/Cucumber/</c>, captured from a real
/// <c>playwright-bdd</c> 9.2.0 / <c>@playwright/test</c> 1.62.1 run (Cucumber Messages protocol 32.2.0).
/// The feature files that produced them are checked in beside the messages, so the fixture can be
/// regenerated: <c>npx bddgen &amp;&amp; npx playwright test</c> with
/// <c>cucumberReporter('message', { outputFile: 'messages.ndjson' })</c>. Absolute paths in stack traces
/// were rewritten to <c>C:\fixture</c>.
/// </summary>
internal static class CucumberFixtures
{
    public static string Directory => Path.Combine(AppContext.BaseDirectory, "TestData", "Cucumber");

    /// <summary>The golden Cucumber Messages NDJSON.</summary>
    public static string MessagesPath => Path.Combine(Directory, "playwright-bdd-9.2-messages.ndjson");

    /// <summary>A generated spec, kept so the <c>// bdd-data-start</c> block's shape stays documented.</summary>
    public static string GeneratedSpecPath => Path.Combine(Directory, "playwright-bdd-9.2-generated-spec.js");

    /// <summary>Reads the golden messages file.</summary>
    public static CucumberMessages Read() => CucumberMessagesReader.ReadFile(MessagesPath);

    /// <summary>Reads and synthesises the golden messages file.</summary>
    public static CucumberSynthesisResult Build(CucumberSynthesisOptions? options = null) =>
        CucumberFeatureSynthesizer.Build(Read(), options);

    // Scenario titles as they appear in the fixture.
    public const string SimpleScenario = "A simple passing scenario";
    public const string TableScenario = "A scenario with a data table and a doc string";
    public const string FailingScenario = "A failing scenario";
    public const string OutlineScenario = "An outline over pages";
    public const string FlakyScenario = "A flaky scenario that passes on the second attempt";
    public const string DemoFeature = "Kronikol demo feature";
    public const string RetryFeature = "Retry demo";
    public const string Rule = "Orders must be validated";

    /// <summary>
    /// Writes a copy of the golden messages file keeping only the named scenarios (and everything that
    /// belongs to them). Used to build an all-passing run — <c>Specifications.html</c> is deliberately
    /// blanked when any scenario failed, so the living-document assertions need a green subset.
    /// </summary>
    public static string WriteSubset(string path, params string[] scenarioNames)
    {
        var lines = File.ReadAllLines(MessagesPath).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        var keep = new HashSet<string>(scenarioNames, StringComparer.Ordinal);
        var pickleIds = new HashSet<string>(StringComparer.Ordinal);
        var testCaseIds = new HashSet<string>(StringComparer.Ordinal);
        var attemptIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in lines)
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.TryGetProperty("pickle", out var pickle)
                && keep.Contains(pickle.GetProperty("name").GetString()!))
                pickleIds.Add(pickle.GetProperty("id").GetString()!);
        }

        foreach (var line in lines)
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("testCase", out var testCase)
                && pickleIds.Contains(testCase.GetProperty("pickleId").GetString()!))
                testCaseIds.Add(testCase.GetProperty("id").GetString()!);
        }

        foreach (var line in lines)
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("testCaseStarted", out var started)
                && testCaseIds.Contains(started.GetProperty("testCaseId").GetString()!))
                attemptIds.Add(started.GetProperty("id").GetString()!);
        }

        var kept = lines.Where(line =>
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var name = root.EnumerateObject().First().Name;
            var value = root.EnumerateObject().First().Value;
            return name switch
            {
                "pickle" => pickleIds.Contains(value.GetProperty("id").GetString()!),
                "testCase" => testCaseIds.Contains(value.GetProperty("id").GetString()!),
                "testCaseStarted" => attemptIds.Contains(value.GetProperty("id").GetString()!),
                "testCaseFinished" or "testStepStarted" or "testStepFinished" or "attachment" =>
                    value.TryGetProperty("testCaseStartedId", out var id) && attemptIds.Contains(id.GetString()!),
                _ => true,
            };
        });

        File.WriteAllLines(path, kept);
        return path;
    }
}
