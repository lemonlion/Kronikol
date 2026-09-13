using System.Text.Json;
using Kronikol.Reports;
using Kronikol.Reports.Merge;

namespace Kronikol.Tests.Reports;

/// <summary>
/// <c>environment</c> says what the run executed on, and there are two lanes where the writing process
/// is not that run: <c>kronikol merge</c>, which runs on whichever machine collects the shards, and
/// <c>kronikol ingest</c>, which may be reading a suite that never touched .NET at all.
///
/// <para>Both wrote <see cref="RunEnvironment.Current"/> — the tool's own operating system and runtime —
/// because every writer read that static directly rather than being told. <see cref="MergeableReport"/>
/// carried no environment at all, so a shard's was discarded at parse and a new one synthesised at
/// write: two shards from ubuntu and windows, merged on a third machine, produced a file naming the
/// third and no sign the other two had ever disagreed.</para>
///
/// <para>Ingest was the clearer case, because the truth was in the file. The Cucumber Messages
/// <c>meta</c> envelope carries <c>os</c> and <c>runtime</c>; the repo's own fixture reports
/// <c>node.js 25.9.0</c> on <c>win32</c>, and the report Kronikol wrote from it claimed the .NET
/// version of the tool doing the reading.</para>
/// </summary>
public class RunEnvironmentProvenanceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-env-" + Guid.NewGuid().ToString("N"));

    public RunEnvironmentProvenanceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private string At(string name) => Path.Combine(_dir, name);

    // ─── A real run still records the machine it ran on ─────────

    [Fact]
    public void A_live_run_records_the_machine_it_ran_on()
    {
        var path = ReportGenerator.GenerateTestRunReportData(
            Features(), Start, End, $"Env_live_{Guid.NewGuid():N}.json", DataFormat.Json);

        var environment = JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("environment");

        Assert.Equal(RunEnvironment.Current.Os, environment.GetProperty("os").GetString());
        Assert.Equal(RunEnvironment.Current.Runtime, environment.GetProperty("runtime").GetString());
    }

    /// <summary>
    /// The marker a lane uses when it has nothing true to write. Omitting the key is the honest answer;
    /// the schema does not require it, and every reader of it is nullable.
    /// </summary>
    [Fact]
    public void A_run_with_no_recorded_environment_omits_the_key()
    {
        var path = ReportGenerator.GenerateTestRunReportData(
            Features(), Start, End, $"Env_none_{Guid.NewGuid():N}.json", DataFormat.Json,
            environment: RunEnvironment.Unrecorded);

        var root = JsonDocument.Parse(File.ReadAllText(path)).RootElement;

        Assert.False(root.TryGetProperty("environment", out _));
    }

    [Theory]
    [InlineData(DataFormat.Xml)]
    [InlineData(DataFormat.Yaml)]
    public void The_other_two_formats_omit_it_too(DataFormat format)
    {
        // All three writers read the static directly, so all three had to be told instead.
        var extension = format == DataFormat.Xml ? "xml" : "yml";
        var path = ReportGenerator.GenerateTestRunReportData(
            Features(), Start, End, $"Env_none_{Guid.NewGuid():N}.{extension}", format,
            environment: RunEnvironment.Unrecorded);

        Assert.DoesNotContain("Environment", File.ReadAllText(path), StringComparison.Ordinal);
    }

    // ─── Merge ──────────────────────────────────────────────────

    [Fact]
    public void A_merge_carries_the_environment_when_every_shard_agrees()
    {
        var merged = MergeableReportMerger.Merge(
        [
            Shard("s1", new RunEnvironment("Linux 6.8.0-1017-azure", ".NET 10.0.0")),
            Shard("s2", new RunEnvironment("Linux 6.8.0-1017-azure", ".NET 10.0.0"))
        ]);

        Assert.Equal(new RunEnvironment("Linux 6.8.0-1017-azure", ".NET 10.0.0"), merged.Environment);
    }

    [Fact]
    public void A_merge_of_shards_that_disagree_records_no_environment()
    {
        var merged = MergeableReportMerger.Merge(
        [
            Shard("s1", new RunEnvironment("Linux 6.8.0-1017-azure", ".NET 10.0.0")),
            Shard("s2", new RunEnvironment("Microsoft Windows 10.0.26200", ".NET 10.0.0"))
        ]);

        Assert.Null(merged.Environment);
    }

    /// <summary>
    /// Saying nothing is only honest if the reader can find out why nothing was said. The distinct
    /// environments go into the diagnostic, which keeps <c>environment</c>'s declared shape — an object
    /// of exactly os and runtime — so this needs no format version bump.
    /// </summary>
    [Fact]
    public void A_merge_that_drops_the_environment_says_so_and_names_what_it_saw()
    {
        var merged = MergeableReportMerger.Merge(
        [
            Shard("s1", new RunEnvironment("Linux 6.8.0-1017-azure", ".NET 10.0.0")),
            Shard("s2", new RunEnvironment("Microsoft Windows 10.0.26200", ".NET 8.0.14"))
        ]);

        var entry = Assert.Single(merged.Diagnostics.Where(d => d.Message.Contains("environment", StringComparison.OrdinalIgnoreCase)));

        Assert.Null(entry.ScenarioId);
        Assert.Contains("Linux 6.8.0-1017-azure", entry.Message, StringComparison.Ordinal);
        Assert.Contains("Microsoft Windows 10.0.26200", entry.Message, StringComparison.Ordinal);
        Assert.Contains(".NET 8.0.14", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shard_that_recorded_no_environment_is_a_disagreement_not_a_match()
    {
        // A shard written before this existed, merged with one written after: the merged file must not
        // claim the one environment it happens to have seen applies to both.
        var merged = MergeableReportMerger.Merge(
        [
            Shard("s1", new RunEnvironment("Linux 6.8.0-1017-azure", ".NET 10.0.0")),
            Shard("s2", environment: null)
        ]);

        Assert.Null(merged.Environment);
    }

    [Fact]
    public void A_shards_environment_survives_a_round_trip_through_the_file()
    {
        var written = ReportGenerator.GenerateMergeableReportJson(
            Features(), Start, End, EmptyDiagrams(), componentRelationships: [],
            internalFlowSegmentData: null, wholeTestFlow: null, WholeTestFlowVisualization.None,
            ciMetadata: null, environment: new RunEnvironment("Linux 6.8.0-1017-azure", ".NET 10.0.0"));

        var read = MergeableReportReader.Parse(written);

        Assert.Equal(new RunEnvironment("Linux 6.8.0-1017-azure", ".NET 10.0.0"), read.Environment);
    }

    /// <summary>
    /// The whole path, file to file: two shards that ran on different machines, merged, and the merged
    /// file read back. This is the one that would have caught the defect, because every step before it
    /// can be right while the writer still fills the key in from the merging process.
    /// </summary>
    [Fact]
    public void A_merged_file_states_no_environment_when_its_shards_did_not_agree()
    {
        var first = At("shard1.json");
        var second = At("shard2.json");
        File.WriteAllText(first, ReportGenerator.GenerateMergeableReportJson(
            Features(), Start, End, EmptyDiagrams(), componentRelationships: [],
            internalFlowSegmentData: null, wholeTestFlow: null, WholeTestFlowVisualization.None,
            ciMetadata: null, environment: new RunEnvironment("Linux 6.8.0-1017-azure", ".NET 10.0.0")));
        File.WriteAllText(second, ReportGenerator.GenerateMergeableReportJson(
            [Feature2()], Start, End, EmptyDiagrams(), componentRelationships: [],
            internalFlowSegmentData: null, wholeTestFlow: null, WholeTestFlowVisualization.None,
            ciMetadata: null, environment: new RunEnvironment("Microsoft Windows 10.0.26200", ".NET 8.0.14")));

        var merged = JsonDocument.Parse(
            MergeableReportRenderer.Serialize(MergeableReportRenderer.MergeFiles([first, second]))).RootElement;

        Assert.False(merged.TryGetProperty("environment", out _));

        var messages = merged.GetProperty("diagnostics").EnumerateArray()
            .Select(d => d.GetProperty("message").GetString() ?? "")
            .ToArray();
        Assert.Contains(messages, m => m.Contains("Linux 6.8.0-1017-azure", StringComparison.Ordinal)
                                    && m.Contains("Microsoft Windows 10.0.26200", StringComparison.Ordinal));
    }

    /// <summary>
    /// And the opposite: shards that agree keep the answer, so the fix does not simply delete the field.
    /// </summary>
    [Fact]
    public void A_merged_file_keeps_the_environment_its_shards_shared()
    {
        var shared = new RunEnvironment("Linux 6.8.0-1017-azure", ".NET 10.0.0");
        var first = At("agree1.json");
        var second = At("agree2.json");
        File.WriteAllText(first, ReportGenerator.GenerateMergeableReportJson(
            Features(), Start, End, EmptyDiagrams(), componentRelationships: [],
            internalFlowSegmentData: null, wholeTestFlow: null, WholeTestFlowVisualization.None,
            ciMetadata: null, environment: shared));
        File.WriteAllText(second, ReportGenerator.GenerateMergeableReportJson(
            [Feature2()], Start, End, EmptyDiagrams(), componentRelationships: [],
            internalFlowSegmentData: null, wholeTestFlow: null, WholeTestFlowVisualization.None,
            ciMetadata: null, environment: shared));

        var merged = JsonDocument.Parse(
            MergeableReportRenderer.Serialize(MergeableReportRenderer.MergeFiles([first, second]))).RootElement;

        var environment = merged.GetProperty("environment");
        Assert.Equal("Linux 6.8.0-1017-azure", environment.GetProperty("os").GetString());
        Assert.Equal(".NET 10.0.0", environment.GetProperty("runtime").GetString());
    }

    // ─── Ingest ─────────────────────────────────────────────────

    /// <summary>
    /// The repo's own Cucumber fixture is a playwright-bdd run: <c>"os":{"name":"win32",...}</c> and
    /// <c>"runtime":{"name":"node.js","version":"25.9.0"}</c>. Kronikol's <c>meta</c> model read the
    /// protocol version and the implementation and stopped, so the truth was two lines above the first
    /// thing it did read.
    /// </summary>
    [Fact]
    public void An_ingested_run_records_the_environment_its_source_reported()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "TestData", "Cucumber", "playwright-bdd-9.2-messages.ndjson");

        var parsed = Kronikol.Ingestion.Cucumber.CucumberMessagesReader.ReadFile(source);

        Assert.Equal("node.js", parsed.Meta!.Runtime!.Name);
        Assert.Equal("25.9.0", parsed.Meta.Runtime.Version);
        Assert.Equal("win32", parsed.Meta.Os!.Name);
        Assert.Equal(new RunEnvironment("win32 10.0.26200", "node.js 25.9.0"), parsed.Meta.ToRunEnvironment());
    }

    [Fact]
    public void An_ingested_run_whose_source_said_nothing_records_nothing()
    {
        Assert.Same(RunEnvironment.Unrecorded, new Kronikol.Ingestion.Cucumber.CucumberMeta().ToRunEnvironment());
    }

    // ─── Fixtures ───────────────────────────────────────────────

    private static readonly DateTime Start = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc);

    private static Feature[] Features() =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios = [new Scenario { Id = "s1", DisplayName = "Place an order", Result = ExecutionResult.Passed }]
        }
    ];

    private static Feature Feature2() => new()
    {
        DisplayName = "Inventory",
        Scenarios = [new Scenario { Id = "s2", DisplayName = "Adjust stock", Result = ExecutionResult.Passed }]
    };

    private static ILookup<string, string> EmptyDiagrams() =>
        Array.Empty<DefaultDiagramsFetcher.DiagramAsCode>().ToLookup(d => d.TestRuntimeId, d => d.CodeBehind);

    private static MergeableReport Shard(string scenarioId, RunEnvironment? environment) => new()
    {
        KronikolVersion = "3.3.0",
        StartTime = Start,
        EndTime = End,
        Environment = environment,
        Features =
        [
            new Feature
            {
                DisplayName = "Orders",
                Scenarios = [new Scenario { Id = scenarioId, DisplayName = "Scenario " + scenarioId, Result = ExecutionResult.Passed }]
            }
        ]
    };
}
