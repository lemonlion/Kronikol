using Kronikol.Tool;

namespace Kronikol.Tests.Tool;

/// <summary>
/// <c>kronikol query diff --baseline</c>: the agent knows where the current report is and should not
/// have to be told where last-green lives too. The baseline is found by convention beside the report,
/// or named once in the environment - and the direction is fixed, because a diff that silently runs
/// backwards reports every regression as a fix and still exits 0.
/// </summary>
public class BaselineDiffTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-baseline").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    // ─── Resolution order ──────────────────────────────────────

    [Fact]
    public void The_baseline_folder_beside_the_report_is_found_without_being_named()
    {
        var current = WriteReport(_directory, "TestRunReport.json", "Failed");
        WriteReport(Path.Combine(_directory, "baseline"), "TestRunReport.json", "Passed");

        var (output, error, exit) = Run(current, "--baseline");

        Assert.True(exit == 0, error);
        Assert.Contains("BROKE", output);
    }

    [Fact]
    public void The_environment_variable_names_the_baseline_when_there_is_no_folder()
    {
        var current = WriteReport(_directory, "TestRunReport.json", "Failed");
        var elsewhere = WriteReport(Path.Combine(_directory, "last-green"), "TestRunReport.json", "Passed");

        var (output, error, exit) = Run(current, "--baseline", env: name => name == "KRONIKOL_BASELINE" ? elsewhere : null);

        Assert.True(exit == 0, error);
        Assert.Contains("BROKE", output);
    }

    [Fact]
    public void The_environment_variable_may_name_a_directory()
    {
        var current = WriteReport(_directory, "TestRunReport.json", "Failed");
        var folder = Path.Combine(_directory, "last-green");
        WriteReport(folder, "TestRunReport.json", "Passed");

        var (output, error, exit) = Run(current, "--baseline", env: name => name == "KRONIKOL_BASELINE" ? folder : null);

        Assert.True(exit == 0, error);
        Assert.Contains("BROKE", output);
    }

    [Fact]
    public void The_folder_beside_the_report_wins_over_the_environment_variable()
    {
        // Convention first: a stale exported variable must not quietly override what is on disk here.
        var current = WriteReport(_directory, "TestRunReport.json", "Failed");
        WriteReport(Path.Combine(_directory, "baseline"), "TestRunReport.json", "Passed");
        var other = WriteReport(Path.Combine(_directory, "elsewhere"), "TestRunReport.json", "Failed");

        var (output, error, exit) = Run(current, "--baseline", env: _ => other);

        Assert.True(exit == 0, error);
        Assert.Contains("BROKE", output);
        Assert.Contains(Path.Combine("baseline", "TestRunReport.json"), output);
    }

    [Fact]
    public void An_unresolvable_baseline_names_both_places_it_looked()
    {
        var current = WriteReport(_directory, "TestRunReport.json", "Failed");

        var (_, error, exit) = Run(current, "--baseline");

        Assert.Equal(2, exit);
        Assert.Contains("baseline", error);
        Assert.Contains("KRONIKOL_BASELINE", error);
    }

    // ─── Direction ─────────────────────────────────────────────

    [Fact]
    public void The_baseline_is_the_old_side_of_the_diff()
    {
        // The trap this flag sets: `diff <old> <new>` names the old report first, but `--baseline` names
        // the CURRENT one. Swap the wrong way and a regression reads as a fix with exit 0.
        var current = WriteReport(_directory, "TestRunReport.json", "Failed");
        WriteReport(Path.Combine(_directory, "baseline"), "TestRunReport.json", "Passed");

        var output = Run(current, "--baseline").Output;

        Assert.Contains("BROKE", output);
        Assert.DoesNotContain("fixed", output);
    }

    [Fact]
    public void A_scenario_repaired_since_the_baseline_reads_as_fixed()
    {
        var current = WriteReport(_directory, "TestRunReport.json", "Passed");
        WriteReport(Path.Combine(_directory, "baseline"), "TestRunReport.json", "Failed");

        var output = Run(current, "--baseline").Output;

        Assert.Contains("fixed", output);
        Assert.DoesNotContain("BROKE", output);
    }

    [Fact]
    public void The_two_header_lines_say_which_file_is_which()
    {
        // Both are called TestRunReport.json, so a bare file name labels the diff with the same word twice.
        var current = WriteReport(_directory, "TestRunReport.json", "Failed");
        WriteReport(Path.Combine(_directory, "baseline"), "TestRunReport.json", "Passed");

        var output = Run(current, "--baseline").Output;

        Assert.Contains("- " + Path.Combine("baseline", "TestRunReport.json"), output);
    }

    // ─── What must not change ──────────────────────────────────

    [Fact]
    public void Diff_without_the_flag_still_takes_two_reports()
    {
        var current = WriteReport(_directory, "TestRunReport.json", "Failed");

        var (_, error, exit) = Run(current);

        Assert.Equal(2, exit);
        Assert.Contains("Diff takes two reports", error);
    }

    [Fact]
    public void A_positional_report_still_wins_over_the_convention()
    {
        // `diff old.json new.json` keeps meaning exactly what it meant, baseline folder present or not.
        var older = WriteReport(Path.Combine(_directory, "yesterday"), "TestRunReport.json", "Passed");
        var current = WriteReport(_directory, "TestRunReport.json", "Failed");
        WriteReport(Path.Combine(_directory, "baseline"), "TestRunReport.json", "Failed");

        var output = Run(older, current).Output;

        Assert.Contains("BROKE", output);
    }

    [Fact]
    public void The_baseline_folder_is_not_mistaken_for_the_report()
    {
        // `query summary <dir>` resolves a directory to the one report under it. The convention drops a
        // second TestRunReport.json in a subfolder, which must not turn that into an ambiguity.
        WriteReport(_directory, "TestRunReport.json", "Failed");
        WriteReport(Path.Combine(_directory, "baseline"), "TestRunReport.json", "Passed");

        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(["summary", _directory], output, error);

        Assert.True(exit == 0, error.ToString());
        Assert.Contains("1 failed", output.ToString());
    }

    // ─── Tracking fidelity ────────────────────────

    [Fact]
    public void A_service_that_stopped_being_tracked_is_reported()
    {
        // The regression no test failure catches: an upgrade or a config change drops a client from
        // tracking, every test still passes, and the diagrams quietly lose a participant.
        WriteReport(Path.Combine(_directory, "baseline"), "TestRunReport.json", "Passed", services: ["OrdersApi", "PricingApi"]);
        var current = WriteReport(_directory, "TestRunReport.json", "Passed", services: ["OrdersApi"]);

        var output = Run(current, "--baseline").Output;

        Assert.Contains("Tracking", output);
        Assert.Contains("PricingApi", output);
        Assert.Contains("no longer tracked", output);
    }

    [Fact]
    public void A_partial_drop_reports_the_counts()
    {
        WriteReport(Path.Combine(_directory, "baseline"), "TestRunReport.json", "Passed", services: ["OrdersApi", "OrdersApi", "OrdersApi"]);
        var current = WriteReport(_directory, "TestRunReport.json", "Passed", services: ["OrdersApi"]);

        var output = Run(current, "--baseline").Output;

        Assert.Contains("OrdersApi", output);
        Assert.Contains("3 \u2192 1 calls", output);
    }

    [Fact]
    public void Capturing_more_than_last_time_is_not_a_warning()
    {
        WriteReport(Path.Combine(_directory, "baseline"), "TestRunReport.json", "Passed", services: ["OrdersApi"]);
        var current = WriteReport(_directory, "TestRunReport.json", "Passed", services: ["OrdersApi", "PricingApi"]);

        var output = Run(current, "--baseline").Output;

        Assert.DoesNotContain("Tracking", output);
        Assert.Contains("no change in results, timings or tracked calls", output);
    }

    [Fact]
    public void A_report_with_no_interactions_is_not_reported_as_a_total_loss()
    {
        // Tracking off, or a mergeable file written before 3.1.0: absent is not the same as lost.
        WriteReport(Path.Combine(_directory, "baseline"), "TestRunReport.json", "Passed", services: ["OrdersApi"]);
        var current = WriteReport(_directory, "TestRunReport.json", "Passed");

        var output = Run(current, "--baseline").Output;

        Assert.DoesNotContain("Tracking", output);
    }

    // ─── Fixtures ──────────────────────────────────────────────

    private (string Output, string Error, int Exit) Run(string report, params string[] args)
        => Run(report, args, env: null);

    private (string Output, string Error, int Exit) Run(string report, string arg, Func<string, string?> env)
        => Run(report, [arg], env);

    private static (string Output, string Error, int Exit) Run(string report, string[] args, Func<string, string?>? env)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(["diff", report, .. args], output, error, env);
        return (output.ToString(), error.ToString(), exit);
    }

    /// <summary>One scenario, one stableId, one result - enough for the diff to have an opinion.</summary>
    private static string WriteReport(string directory, string name, string result, string[]? services = null)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        var interactions = string.Join(",\n                      ", (services ?? [])
            .Select(service => $$"""
                { "type": "Request", "method": "GET", "uri": "https://{{service}}/x", "serviceName": "{{service}}", "callerName": "Test", "headers": [] }
                """));
        File.WriteAllText(path, $$"""
            {
              "kronikolVersion": "3.1.0",
              "startTime": "2026-01-01T10:00:00Z",
              "endTime": "2026-01-01T10:05:00Z",
              "features": [
                {
                  "name": "Checkout",
                  "labels": [],
                  "scenarios": [
                    { "id": "t0", "stableId": "aaaabbbbccccdddd", "name": "Pay by card", "result": "{{result}}", "durationSeconds": 1.0, "labels": [], "categories": [], "steps": [], "httpInteractions": [{{interactions}}] }
                  ]
                }
              ]
            }
            """);
        return path;
    }
}
