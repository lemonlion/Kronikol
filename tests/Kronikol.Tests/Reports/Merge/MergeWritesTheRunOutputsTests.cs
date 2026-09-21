using System.Text.Json;
using Kronikol.Reports;
using Kronikol.Tool;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// <c>kronikol merge</c> writes beside its report everything a run writes beside its own.
///
/// <para>A run ends in a tail of outputs that are not the report - <c>Failures.md</c> and
/// <c>Failures.jsonl</c>, <c>CLAUDE.md</c> and <c>AGENTS.md</c>, the schema, the CI job summary, the
/// console pointer, the artifact publish. A merge rendered the HTML and the data file and stopped. So the
/// one shape of run that is always on CI - a sharded suite - had none of the things this library builds
/// for CI: nothing to read first, nothing for an agent to load, no line in the job log saying where the
/// merged report went or how big its data file is.</para>
/// </summary>
public class MergeWritesTheRunOutputsTests : IDisposable
{
    private readonly string _in = Path.Combine(Path.GetTempPath(), "kronikol-merge-tail-in-" + Guid.NewGuid().ToString("N"));
    private readonly string _out = Path.Combine(Path.GetTempPath(), "kronikol-merge-tail-out-" + Guid.NewGuid().ToString("N"));

    public MergeWritesTheRunOutputsTests()
    {
        Directory.CreateDirectory(_in);
        Directory.CreateDirectory(_out);
    }

    public void Dispose()
    {
        try { Directory.Delete(_in, true); } catch { /* best effort */ }
        try { Directory.Delete(_out, true); } catch { /* best effort */ }
    }

    // ─── The files ─────────────────────────────────────────────

    [Fact]
    public void Merge_writes_the_failures_digest_beside_the_report()
    {
        WriteShard("runner1.json", "0-1002", "Cart is priced", "Passed");
        WriteShard("runner2.json", "0-1002", "Discount is applied", "Failed", error: "expected 10 but was 12");

        var (exit, _, error) = Merge();

        Assert.True(exit == 0, error);
        var digest = File.ReadAllText(Out("Failures.md"));
        Assert.Contains("Discount is applied", digest);
        Assert.Contains("expected 10 but was 12", digest);
        // The digest links into the report that was actually written, not into TestRunReport.html.
        Assert.Contains("Combined.html#sid-", digest);
        Assert.NotEmpty(File.ReadAllText(Out("Failures.jsonl")));
    }

    /// <summary>
    /// The prerequisite the digest had for a merge: it derived which step each call happened under by
    /// walking diagram markers, and a shard carries none - it carries <c>stepPath</c> instead. Derived from
    /// nothing, every failure in a merged digest read <c>callsScope: "scenario"</c>.
    /// </summary>
    [Fact]
    public void The_digest_attributes_calls_to_the_failing_step_from_the_carried_step_paths()
    {
        WriteShard("runner1.json", "0-1002", "Discount is applied", "Failed", error: "boom", withSteps: true, callsInFailingStep: 2);

        var (exit, _, error) = Merge();

        Assert.True(exit == 0, error);
        var record = JsonDocument.Parse(File.ReadLines(Out("Failures.jsonl")).First(l => l.Contains("\"scenario\"", StringComparison.Ordinal))).RootElement;
        Assert.Equal("failingStep", record.GetProperty("callsScope").GetString());
        Assert.Equal(2, record.GetProperty("calls").GetArrayLength());
    }

    [Fact]
    public void Merge_writes_the_agent_instruction_files_naming_the_merged_report()
    {
        WriteShard("runner1.json", "0-1002", "Cart is priced", "Passed");

        var (exit, _, error) = Merge();

        Assert.True(exit == 0, error);
        foreach (var name in new[] { "CLAUDE.md", "AGENTS.md" })
        {
            var text = File.ReadAllText(Out(name));
            Assert.Contains(AgentInstructionsBlock.BeginMarker, text);
            Assert.Contains("Combined.json", text);
            Assert.DoesNotContain("TestRunReport.json", text);
        }
    }

    [Fact]
    public void An_existing_instruction_file_beside_the_output_keeps_its_own_text()
    {
        WriteShard("runner1.json", "0-1002", "Cart is priced", "Passed");
        File.WriteAllText(Out("CLAUDE.md"), "# Team notes\n\nKeep this paragraph.\n");

        var (exit, _, error) = Merge();

        Assert.True(exit == 0, error);
        var text = File.ReadAllText(Out("CLAUDE.md"));
        Assert.Contains("Keep this paragraph.", text);
        Assert.Contains(AgentInstructionsBlock.BeginMarker, text);
    }

    [Fact]
    public void Merge_writes_a_schema_the_merged_data_file_validates_against()
    {
        WriteShard("runner1.json", "0-1002", "Cart is priced", "Passed", callsInFailingStep: 1, withSteps: true);
        WriteShard("runner2.json", "0-1002", "Discount is applied", "Failed", error: "boom");

        var (exit, _, error) = Merge();

        Assert.True(exit == 0, error);
        Assert.True(File.Exists(Out("Combined.schema.json")), "the schema takes the report's name");
        var problems = SchemaValidationTests.Validate(Out("Combined.schema.json"), Out("Combined.json"));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void No_json_writes_the_html_alone()
    {
        WriteShard("runner1.json", "0-1002", "Cart is priced", "Failed", error: "boom");

        var (exit, _, error) = Merge("--no-json");

        Assert.True(exit == 0, error);
        Assert.True(File.Exists(Out("Combined.html")));
        Assert.Equal(["Combined.html"], Directory.GetFiles(_out).Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public void A_merge_is_not_a_run_it_keeps_nothing_and_writes_no_manifest()
    {
        // A merged report is derived: the shards are the evidence, and they are kept where they were
        // written. Merging twice into one directory overwrites, as it always did.
        WriteShard("runner1.json", "0-1002", "Cart is priced", "Failed", error: "boom");
        Assert.Equal(0, Merge().Exit);

        var (exit, _, error) = Merge();

        Assert.True(exit == 0, error);
        Assert.False(Directory.Exists(Out(ReportFolders.RunsFolderName)), "a merge rotated the previous merge away");
        Assert.False(File.Exists(Out(RunManifest.FileName)), "a merge wrote a run manifest");
    }

    // ─── The pointer ───────────────────────────────────────────

    [Fact]
    public void Merge_ends_with_the_pointer_naming_the_merged_file()
    {
        WriteShard("runner1.json", "0-1002", "Cart is priced", "Passed");
        WriteShard("runner2.json", "0-1002", "Discount is applied", "Failed", error: "boom");

        var (exit, output, error) = Merge();

        Assert.True(exit == 0, error);
        var lines = output.TrimEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var pointer = Array.FindIndex(lines, l => l.StartsWith("Kronikol: reports written to ", StringComparison.Ordinal));
        Assert.True(pointer >= 0, output);
        Assert.Contains("Combined.json", lines[pointer]);
        Assert.Contains("Failures.md", lines[pointer]);
        // A directory lookup finds TestRunReport.json and nothing else, so the command names the file.
        Assert.Contains($"1 failed — kronikol query failures {Out("Combined.json")}", lines[pointer + 1]);
        Assert.Contains("never open Combined.json", output);
    }

    [Fact]
    public void The_pointer_names_the_directory_when_the_merged_file_is_called_TestRunReport()
    {
        WriteShard("runner1.json", "0-1002", "Discount is applied", "Failed", error: "boom");

        var (exit, output, error) = MergeInto("TestRunReport.html");

        Assert.True(exit == 0, error);
        Assert.Contains($"1 failed — kronikol query failures {_out}", output);
    }

    [Fact]
    public void A_green_merge_off_ci_says_where_the_files_are_and_nothing_more()
    {
        WriteShard("runner1.json", "0-1002", "Cart is priced", "Passed");

        var (exit, output, error) = Merge();

        Assert.True(exit == 0, error);
        Assert.Contains("Kronikol: reports written to ", output);
        Assert.DoesNotContain("failed", output);
        Assert.DoesNotContain("## Debug this run", output);
        Assert.DoesNotContain("::notice", output);
    }

    // ─── CI ────────────────────────────────────────────────────

    [Fact]
    public void A_failing_merge_on_github_writes_the_debug_section_to_the_job_summary_and_the_log()
    {
        WriteShard("runner1.json", "0-1002", "Discount is applied", "Failed", error: "boom");
        var summaryFile = Out("step-summary.md");

        var (exit, output, error) = Merge(GitHub(("GITHUB_STEP_SUMMARY", summaryFile)));

        Assert.True(exit == 0, error);
        var summary = File.ReadAllText(summaryFile);
        Assert.Contains("## Debug this run", summary);
        Assert.Contains($"kronikol query failures {Out("Combined.json")}", summary);
        Assert.Contains("## Debug this run", output);
        Assert.Contains("::notice title=Kronikol::1 failed of 1 scenarios", output);
    }

    [Fact]
    public void Ci_summary_writes_the_diagrammed_summary_beside_the_report_and_posts_it()
    {
        WriteShard("runner1.json", "0-1002", "Discount is applied", "Failed", error: "boom");
        var summaryFile = Out("step-summary.md");

        var (exit, _, error) = Merge(GitHub(("GITHUB_STEP_SUMMARY", summaryFile)), "--ci-summary");

        Assert.True(exit == 0, error);
        var markdown = File.ReadAllText(Out("CiSummary.md"));
        Assert.Contains("# Diagrammed Test Run Summary", markdown);
        Assert.Contains("Discount is applied", markdown);
        Assert.Contains("## Debug this run", markdown);
        Assert.Equal(markdown, File.ReadAllText(summaryFile));
    }

    [Fact]
    public void Publish_artifacts_on_github_hands_the_output_directory_to_the_upload()
    {
        WriteShard("runner1.json", "0-1002", "Cart is priced", "Passed");
        var githubOutput = Out("github-output.txt");

        var (exit, _, error) = Merge(GitHub(("GITHUB_OUTPUT", githubOutput)), "--publish-artifacts");

        Assert.True(exit == 0, error);
        var text = File.ReadAllText(githubOutput);
        Assert.Contains($"reports-path={_out}", text);
        Assert.Contains("reports-retention-days=1", text);
    }

    // ─── Helpers ───────────────────────────────────────────────

    private string Out(string name) => Path.Combine(_out, name);

    private static Func<string, string?> GitHub(params (string Name, string Value)[] more) =>
        name => name == "GITHUB_ACTIONS" ? "true" : more.FirstOrDefault(v => v.Name == name).Value;

    // No single-string overload: one would capture a bare flag such as "--no-json" as an output name.
    private (int Exit, string Output, string Error) Merge(params string[] extra) => Merge(extra, null, "Combined.html");

    private (int Exit, string Output, string Error) Merge(Func<string, string?> env, params string[] extra) => Merge(extra, env, "Combined.html");

    private (int Exit, string Output, string Error) MergeInto(string outputName) => Merge([], null, outputName);

    private (int Exit, string Output, string Error) Merge(string[] extra, Func<string, string?>? env, string outputName)
    {
        var outWriter = new StringWriter();
        var errWriter = new StringWriter();
        var exit = MergeCommand.Run([_in, "-o", Out(outputName), .. extra], outWriter, errWriter, env ?? (_ => null));
        return (exit, outWriter.ToString(), errWriter.ToString());
    }

    /// <summary>
    /// One scenario. With <paramref name="withSteps"/>, two steps of which the second failed, and
    /// <paramref name="callsInFailingStep"/> requests attributed to it by their carried <c>stepPath</c>.
    /// </summary>
    private void WriteShard(string name, string id, string scenarioName, string result, string? error = null,
        bool withSteps = false, int callsInFailingStep = 0)
    {
        var steps = withSteps
            ? """
              "steps": [
                { "keyword": "Given", "text": "a cart", "status": "Passed" },
                { "keyword": "When", "text": "it is priced", "status": "Failed", "failureMessage": "boom" } ],
              """
            : "";

        var interactions = string.Join(",\n", Enumerable.Range(0, callsInFailingStep).Select(i => $$"""
                { "type": "Request", "metaType": "Default", "method": "GET", "uri": "https://pricing.test/quote/{{i}}",
                  "serviceName": "Pricing", "callerName": "Tests", "requestResponseId": "{{Guid.NewGuid()}}",
                  "timestamp": "2026-01-01T10:00:{{i:D2}}Z", "stepPath": "1" }
                """));

        File.WriteAllText(Path.Combine(_in, name), $$"""
            {
              "mergeableFormatVersion": 1,
              "kronikolVersion": "3.5.1",
              "startTime": "2026-01-01T10:00:00Z",
              "endTime": "2026-01-01T10:01:00Z",
              "features": [
                { "name": "Checkout", "scenarios": [
                  { "id": {{JsonSerializer.Serialize(id)}},
                    "name": {{JsonSerializer.Serialize(scenarioName)}},
                    "result": "{{result}}",
                    "durationSeconds": 1.0,
                    {{(error is null ? "" : $"\"errorMessage\": {JsonSerializer.Serialize(error)},")}}
                    {{steps}}
                    "diagrams": ["@startuml\nTests->Pricing\n@enduml"],
                    "httpInteractions": [ {{interactions}} ] }
                ] }
              ]
            }
            """);
    }
}
