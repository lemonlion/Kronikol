using System.Text.Json;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// <c>ctrf-report.json</c> is the one output Kronikol writes for somebody else's tooling: a GitHub
/// annotation action, a flaky-test dashboard, a PR comment bot. Everything asserted here is therefore a
/// contract with a consumer this repo cannot see — the key names, the five statuses, the unit of
/// <c>duration</c>, and the <c>specVersion</c> that says which CTRF the document was written against.
/// A field emitted under a name the schema does not declare is worse than no field: it is silently
/// dropped by every reader and looks like a Kronikol bug to whoever expected it.
/// </summary>
public class CtrfReportGeneratorTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 1, 1, 10, 0, 30, DateTimeKind.Utc);

    private static JsonElement Generate(Feature[] features, CiMetadata? ci = null) =>
        JsonDocument.Parse(CtrfReportGenerator.Generate(features, Start, End, ci, "3.1.0")).RootElement;

    private static JsonElement Results(Feature[] features, CiMetadata? ci = null) =>
        Generate(features, ci).GetProperty("results");

    private static JsonElement[] Tests(Feature[] features) =>
        [.. Results(features).GetProperty("tests").EnumerateArray()];

    private static Feature[] OneOfEach() =>
    [
        new Feature
        {
            DisplayName = "Checkout",
            SourceFile = "Features/Checkout.feature",
            Scenarios =
            [
                new Scenario
                {
                    Id = "s-pass", DisplayName = "Pay with a valid card", Result = ExecutionResult.Passed,
                    Duration = TimeSpan.FromMilliseconds(1500), Labels = ["smoke"], Categories = ["payments"],
                    SourceFile = "Features/Checkout.feature", SourceLine = 12
                },
                new Scenario
                {
                    Id = "s-fail", DisplayName = "Pay with an expired card", Result = ExecutionResult.Failed,
                    Duration = TimeSpan.FromMilliseconds(400),
                    ErrorMessage = "Assert.Equal() Failure", ErrorStackTrace = "at Checkout.Pay()"
                },
                new Scenario { Id = "s-skip", DisplayName = "Pay in yen", Result = ExecutionResult.Skipped },
                new Scenario { Id = "s-bypass", DisplayName = "Pay offline", Result = ExecutionResult.Bypassed },
                new Scenario { Id = "s-after", DisplayName = "Refund", Result = ExecutionResult.SkippedAfterFailure }
            ]
        }
    ];

    // ─── The envelope ──────────────────────────────────────────

    [Fact]
    public void The_document_names_the_format_and_the_spec_version_it_was_written_against()
    {
        var root = Generate(OneOfEach());

        Assert.Equal("CTRF", root.GetProperty("reportFormat").GetString());
        // Pinned, not computed: a consumer reads this to decide how to parse the rest, so it moves only
        // when the writer has actually been re-checked against a newer CTRF schema.
        Assert.Equal("0.0.0", root.GetProperty("specVersion").GetString());
        Assert.Equal("Kronikol", root.GetProperty("generatedBy").GetString());
        Assert.Equal("Kronikol", root.GetProperty("results").GetProperty("tool").GetProperty("name").GetString());
        Assert.Equal("3.1.0", root.GetProperty("results").GetProperty("tool").GetProperty("version").GetString());
    }

    [Fact]
    public void The_spec_version_the_writer_declares_is_the_one_the_constant_pins()
    {
        Assert.Equal(CtrfReportGenerator.SpecVersion,
            Generate(OneOfEach()).GetProperty("specVersion").GetString());
        Assert.Equal("ctrf-report.json", CtrfReportGenerator.FileName);
    }

    // ─── Tests ─────────────────────────────────────────────────

    [Fact]
    public void Every_scenario_becomes_a_test_carrying_the_three_fields_the_schema_requires()
    {
        var tests = Tests(OneOfEach());

        Assert.Equal(5, tests.Length);
        foreach (var test in tests)
        {
            Assert.False(string.IsNullOrEmpty(test.GetProperty("name").GetString()));
            Assert.False(string.IsNullOrEmpty(test.GetProperty("status").GetString()));
            Assert.True(test.TryGetProperty("duration", out _));
        }

        Assert.Equal("Pay with a valid card", tests[0].GetProperty("name").GetString());
        Assert.Equal("Checkout", tests[0].GetProperty("suite").GetString());
    }

    [Theory]
    [InlineData(ExecutionResult.Passed, "passed")]
    [InlineData(ExecutionResult.Failed, "failed")]
    [InlineData(ExecutionResult.Skipped, "skipped")]
    [InlineData(ExecutionResult.SkippedAfterFailure, "skipped")]
    [InlineData(ExecutionResult.Bypassed, "other")]
    public void Every_result_maps_onto_one_of_the_five_statuses_the_schema_allows(ExecutionResult result, string expected)
    {
        Assert.Equal(expected, CtrfReportGenerator.MapStatus(result));
    }

    [Fact]
    public void The_kronikol_word_survives_in_rawStatus_where_two_results_share_a_status()
    {
        // "skipped" is the honest CTRF answer for both Skipped and SkippedAfterFailure, and "other" is the
        // only one Bypassed fits. rawStatus is where the distinction the report makes goes, so nothing the
        // run knew is lost by the translation.
        var tests = Tests(OneOfEach());

        Assert.Equal("Skipped", tests[2].GetProperty("rawStatus").GetString());
        Assert.Equal("Bypassed", tests[3].GetProperty("rawStatus").GetString());
        Assert.Equal("SkippedAfterFailure", tests[4].GetProperty("rawStatus").GetString());
    }

    [Fact]
    public void Durations_are_whole_milliseconds_and_a_scenario_that_was_never_timed_reports_zero()
    {
        var tests = Tests(OneOfEach());

        // Every other Kronikol writer emits durationSeconds; CTRF's unit is milliseconds.
        Assert.Equal(1500, tests[0].GetProperty("duration").GetInt64());
        Assert.Equal(400, tests[1].GetProperty("duration").GetInt64());
        // The schema requires the field, so an untimed scenario is 0 rather than absent.
        Assert.Equal(0, tests[2].GetProperty("duration").GetInt64());
    }

    [Fact]
    public void A_failure_carries_its_message_and_its_trace_and_a_pass_carries_neither()
    {
        var tests = Tests(OneOfEach());

        Assert.Equal("Assert.Equal() Failure", tests[1].GetProperty("message").GetString());
        Assert.Equal("at Checkout.Pay()", tests[1].GetProperty("trace").GetString());
        Assert.False(tests[0].TryGetProperty("message", out _));
        Assert.False(tests[0].TryGetProperty("trace", out _));
    }

    // ─── Source locations ──────────────────────────────────────

    [Fact]
    public void A_source_location_is_emitted_only_where_the_runner_supplied_a_real_path()
    {
        // CTRF consumers place GitHub annotations with filePath, so a bare file name would annotate the
        // wrong file or nothing at all. Only Scenario.SourceFile and Feature.SourceFile qualify — both are
        // project-relative by contract; a step's SourceFile is deliberately a bare name and never used.
        var tests = Tests(OneOfEach());

        Assert.Equal("Features/Checkout.feature", tests[0].GetProperty("filePath").GetString());
        Assert.Equal(12, tests[0].GetProperty("line").GetInt32());
        // The failing scenario has no source of its own, so it falls back to the feature's file and has
        // no line to give.
        Assert.Equal("Features/Checkout.feature", tests[1].GetProperty("filePath").GetString());
        Assert.False(tests[1].TryGetProperty("line", out _));
    }

    [Fact]
    public void A_lane_that_knows_no_source_location_emits_no_filePath_at_all()
    {
        Feature[] features =
        [
            new Feature { DisplayName = "Unit", Scenarios = [new Scenario { Id = "u1", DisplayName = "Adds", Result = ExecutionResult.Passed }] }
        ];

        Assert.False(Tests(features)[0].TryGetProperty("filePath", out _));
    }

    // ─── Retries ───────────────────────────────────────────────

    [Fact]
    public void A_retried_scenario_reports_its_retries_and_a_retried_pass_reads_as_flaky()
    {
        var tests = Tests(Flaky());

        // Attempt is 1-based, so the third attempt is two retries.
        Assert.Equal(2, tests[0].GetProperty("retries").GetInt32());
        Assert.True(tests[0].GetProperty("flaky").GetBoolean());
        // Retried and still failing is not flaky — it is broken.
        Assert.Equal(1, tests[1].GetProperty("retries").GetInt32());
        Assert.False(tests[1].GetProperty("flaky").GetBoolean());
        // A runner that says nothing about attempts means zero, never a guess.
        Assert.Equal(0, tests[2].GetProperty("retries").GetInt32());
        Assert.False(tests[2].GetProperty("flaky").GetBoolean());
    }

    [Fact]
    public void The_retry_labels_kronikol_generates_are_not_offered_as_tags()
    {
        // Cucumber ingest folds earlier attempts into one scenario and records them as "retry N" labels.
        // They are a rendering device, not something an author tagged, and as CTRF tags they would grow a
        // new junk tag on every retry. retries already carries the fact.
        var tags = Tests(Flaky())[0].GetProperty("tags").EnumerateArray().Select(t => t.GetString()!).ToArray();

        Assert.Equal(["slow"], tags);
    }

    private static Feature[] Flaky() =>
    [
        new Feature
        {
            DisplayName = "Flaky",
            Scenarios =
            [
                new Scenario { Id = "f1", DisplayName = "Eventually passes", Result = ExecutionResult.Passed, Attempt = 3, Labels = ["retry 1", "retry 2", "slow"] },
                new Scenario { Id = "f2", DisplayName = "Never passes", Result = ExecutionResult.Failed, Attempt = 2, Labels = ["retry 1"] },
                new Scenario { Id = "f3", DisplayName = "Ran once", Result = ExecutionResult.Passed }
            ]
        }
    ];

    // ─── Tags and categories ───────────────────────────────────

    [Fact]
    public void Categories_travel_in_extra_rather_than_pretending_to_be_tags()
    {
        // A Kronikol category comes from an @category: tag and is a different axis from a label. Flattening
        // the two into CTRF tags would make "payments" indistinguishable from a label somebody wrote.
        var tests = Tests(OneOfEach());

        Assert.Equal(["smoke"], tests[0].GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal(["payments"], tests[0].GetProperty("extra").GetProperty("categories").EnumerateArray().Select(t => t.GetString()));
        // Nothing empty is emitted: a scenario with neither carries neither.
        Assert.False(tests[2].TryGetProperty("tags", out _));
    }

    // ─── The way back into the report ──────────────────────────

    [Fact]
    public void Every_test_carries_the_address_and_the_stable_id_that_lead_back_to_the_report()
    {
        var tests = Tests(OneOfEach());

        Assert.Equal("s0", tests[0].GetProperty("extra").GetProperty("kronikolAddress").GetString());
        Assert.Equal("s1", tests[1].GetProperty("extra").GetProperty("kronikolAddress").GetString());
        Assert.Equal(
            ScenarioStableId.Compute(null, "Checkout", "Pay with an expired card"),
            tests[1].GetProperty("extra").GetProperty("stableId").GetString());
    }

    [Fact]
    public void The_addresses_are_numbered_the_way_the_report_file_numbers_them()
    {
        // Same rule, same comparer, as FailuresDigestGenerator.Enumerate and BuildFeaturesJsonModel:
        // features by display name under the default culture comparer, scenarios in file order. Ordinal
        // would put "Order API" first and every address after it would point at the wrong scenario.
        Feature[] features =
        [
            new Feature { DisplayName = "Order API", Scenarios = [new Scenario { Id = "a1", DisplayName = "Place", Result = ExecutionResult.Passed }] },
            new Feature { DisplayName = "Order api", Scenarios = [new Scenario { Id = "b1", DisplayName = "Cancel", Result = ExecutionResult.Passed }] }
        ];

        var tests = Tests(features);

        Assert.Equal("Cancel", tests[0].GetProperty("name").GetString());
        Assert.Equal("s0", tests[0].GetProperty("extra").GetProperty("kronikolAddress").GetString());
        Assert.Equal("Place", tests[1].GetProperty("name").GetString());
        Assert.Equal("s1", tests[1].GetProperty("extra").GetProperty("kronikolAddress").GetString());
    }

    // ─── Summary ───────────────────────────────────────────────

    [Fact]
    public void The_summary_counts_are_the_tests_array_counted()
    {
        var summary = Results(OneOfEach()).GetProperty("summary");

        Assert.Equal(5, summary.GetProperty("tests").GetInt32());
        Assert.Equal(1, summary.GetProperty("passed").GetInt32());
        Assert.Equal(1, summary.GetProperty("failed").GetInt32());
        Assert.Equal(2, summary.GetProperty("skipped").GetInt32());
        Assert.Equal(1, summary.GetProperty("other").GetInt32());
        Assert.Equal(0, summary.GetProperty("pending").GetInt32());
    }

    [Fact]
    public void The_run_window_is_epoch_milliseconds_rounded_to_the_second_the_report_records()
    {
        // CTRF start/stop are numbers and duration is milliseconds, so these are milliseconds too. They
        // are truncated to whole seconds because that is the precision TestRunReport.json's own startTime
        // carries — a CTRF converted from that file has to equal one written during the run.
        var summary = Results(OneOfEach()).GetProperty("summary");

        Assert.Equal(1767261600000, summary.GetProperty("start").GetInt64());
        Assert.Equal(1767261630000, summary.GetProperty("stop").GetInt64());
    }

    // ─── Environment ───────────────────────────────────────────

    [Fact]
    public void Off_ci_there_is_no_environment_block_to_be_wrong_about()
    {
        Assert.False(Results(OneOfEach()).TryGetProperty("environment", out _));
    }

    [Fact]
    public void On_ci_the_run_identity_travels_in_environment_including_which_try_this_is()
    {
        var environment = Results(OneOfEach(), OnGitHub).GetProperty("environment");

        Assert.Equal("GitHubActions", environment.GetProperty("buildName").GetString());
        Assert.Equal("42", environment.GetProperty("buildNumber").GetString());
        Assert.Equal("main", environment.GetProperty("branchName").GetString());
        Assert.Equal("abc1234", environment.GetProperty("commit").GetString());
        Assert.Equal("https://github.com/o/r/actions/runs/9", environment.GetProperty("buildUrl").GetString());
        Assert.Equal("o/r", environment.GetProperty("repositoryName").GetString());
        // The run id folds a re-run onto the run it retried; the attempt is the only thing that separates
        // them, and CTRF has nowhere standard to put either.
        Assert.Equal("9", environment.GetProperty("extra").GetProperty("runId").GetString());
        Assert.Equal("2", environment.GetProperty("extra").GetProperty("runAttempt").GetString());
    }

    private static readonly CiMetadata OnGitHub = new(CiEnvironment.GitHubActions, "42", "main", "abc1234",
        "https://github.com/o/r/actions/runs/9", "o/r", "9", "2");

    // ─── The schema pin ────────────────────────────────────────

    /// <summary>
    /// Every key the writer may emit, transcribed from the CTRF schema at
    /// <see cref="CtrfReportGenerator.SpecVersion"/>. This list IS the pin the plan asks for: the schema
    /// itself is published rather than bundled, and the repo's own convention is a hand-written walker
    /// rather than a validator dependency — see <c>TestRunReportSchemaContractTests</c>. A key that
    /// appears in the document and not here fails, which is what stops a field being invented under a
    /// name no consumer reads.
    /// </summary>
    private static readonly HashSet<string> SchemaKeys =
    [
        // Root
        "reportFormat", "specVersion", "generatedBy", "results",
        // results
        "tool", "summary", "tests", "environment",
        // tool
        "name", "version",
        // summary
        "passed", "failed", "pending", "skipped", "other", "start", "stop",
        // test
        "status", "duration", "suite", "message", "trace", "line", "rawStatus", "tags",
        "filePath", "retries", "flaky", "extra",
        // environment
        "buildName", "buildNumber", "buildUrl", "repositoryName", "commit", "branchName"
    ];

    [Fact]
    public void Nothing_is_emitted_under_a_key_the_ctrf_schema_does_not_declare()
    {
        var unknown = new List<string>();
        Walk(Generate(OneOfEach(), OnGitHub), "", unknown);

        Assert.Empty(unknown);
    }

    private static void Walk(JsonElement element, string path, List<string> unknown)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            // extra is the schema's own escape hatch: whatever it holds is by definition allowed.
            if (path.EndsWith("extra", StringComparison.Ordinal)) return;
            foreach (var property in element.EnumerateObject())
            {
                if (!SchemaKeys.Contains(property.Name)) unknown.Add($"{path}/{property.Name}");
                Walk(property.Value, $"{path}/{property.Name}", unknown);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                Walk(item, path, unknown);
        }
    }
}
