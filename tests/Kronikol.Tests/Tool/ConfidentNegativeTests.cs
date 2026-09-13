using Kronikol.Tool;

namespace Kronikol.Tests.Tool;

/// <summary>
/// Two answers that were confidently wrong about a report that contradicted them, both at exit 0.
///
/// <para><c>assertions --failed</c> printed <c>no assertions failed</c> on a run with fifteen failures,
/// and blamed <c>IncludeTrackedAssertionsInStepList</c> — an option that is frequently already on. Both
/// halves are wrong at once: the sentence asserts something about the run's failures that the report
/// denies on the line above, and the remedy names a switch the reader may have set already. The trigger
/// is per-failure, not per-project: a suite can track assertions perfectly and still have failures that
/// did not happen at one, because the test threw before reaching it.</para>
///
/// <para><c>grep</c> could not see a scenario's name, its <c>errorMessage</c> or its
/// <c>errorStackTrace</c>. On the verb whose entire job is "where did this value come from", a value
/// present in the report in three places came back as <c>"…" is not in bodies, uris, steps,
/// assertions</c> — the shape of a proof of absence.</para>
/// </summary>
public class ConfidentNegativeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-negative-" + Guid.NewGuid().ToString("N"));

    public ConfidentNegativeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    // ─── assertions --failed ────────────────────────────────────

    [Fact]
    public void A_run_that_tracks_no_assertions_says_that_rather_than_that_none_failed()
    {
        var output = Run("assertions", FailingReport(withTrackedAssertions: false), "--failed");

        Assert.DoesNotContain("no assertions failed", output, StringComparison.Ordinal);
        Assert.Contains("tracks no assertions", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The half that made the old sentence dangerous: the report says a scenario failed, and the answer
    /// said nothing failed. Whatever else it says, it has to carry that forward.
    /// </summary>
    [Fact]
    public void A_report_with_failures_and_no_failed_assertion_still_says_something_failed()
    {
        foreach (var tracked in new[] { true, false })
        {
            var output = Run("assertions", FailingReport(tracked, $"Failing{tracked}.json"), "--failed");

            Assert.Contains("1 scenario", output, StringComparison.Ordinal);
            Assert.Contains("failures", output, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The remedy was printed unconditionally, so a project that had already switched the option on was
    /// told to switch it on. It is only the answer when the report holds no tracked assertion at all.
    /// </summary>
    [Fact]
    public void The_option_is_only_blamed_when_it_could_be_the_cause()
    {
        Assert.DoesNotContain("IncludeTrackedAssertionsInStepList",
            Run("assertions", FailingReport(withTrackedAssertions: true), "--failed"), StringComparison.Ordinal);

        Assert.Contains("IncludeTrackedAssertionsInStepList",
            Run("assertions", FailingReport(withTrackedAssertions: false, "NoTracked.json"), "--failed"), StringComparison.Ordinal);
    }

    // ─── grep ───────────────────────────────────────────────────

    [Theory]
    [InlineData("Checkout fails on a wrong total")]
    [InlineData("Assert.Equal() Failure")]
    [InlineData("OrderTests.PlaceOrder")]
    public void Grep_finds_what_the_report_holds_about_a_failure(string needle)
    {
        Assert.Contains("s0", Run("grep", FailingReport(withTrackedAssertions: true), needle), StringComparison.Ordinal);
    }

    [Fact]
    public void The_new_targets_are_named_by_the_validator_like_every_other_one()
    {
        var (_, error, exit) = RunFull("grep", FailingReport(withTrackedAssertions: true), "x", "--in", "nope");

        Assert.Equal(2, exit);
        Assert.Contains("names", error, StringComparison.Ordinal);
        Assert.Contains("errors", error, StringComparison.Ordinal);
    }

    // ─── Fixtures ───────────────────────────────────────────────

    private string Run(string command, string report, params string[] args)
    {
        var (output, error, exit) = RunFull(command, report, args);
        Assert.True(exit == 0, $"exit {exit}: {error}");
        return output;
    }

    private (string Output, string Error, int Exit) RunFull(string command, string report, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run([command, report, .. args], output, error);
        return (output.ToString(), error.ToString(), exit);
    }

    /// <summary>
    /// One failing scenario whose failure did NOT happen at an assertion — the test threw first. With
    /// <paramref name="withTrackedAssertions"/> the scenario also carries a passing tracked assertion, so
    /// the two states the old message conflated can be told apart.
    /// </summary>
    private string FailingReport(bool withTrackedAssertions, string fileName = "TestRunReport.json")
    {
        var assertion = withTrackedAssertions
            ? """{ "text": "the basket is not empty", "status": "Passed", "subSteps": [], "attachments": [] }"""
            : "";

        var path = Path.Combine(_dir, fileName);
        File.WriteAllText(path, $$"""
            {
              "kronikolVersion": "3.4.1",
              "formatVersion": 1,
              "startTime": "2026-01-01T10:00:00Z",
              "endTime": "2026-01-01T10:05:00Z",
              "features": [
                { "name": "Orders", "labels": [], "scenarios": [
                  {
                    "id": "t0", "stableId": "1111222233334444", "name": "Checkout fails on a wrong total",
                    "result": "Failed", "durationSeconds": 1.0,
                    "errorMessage": "Assert.Equal() Failure: Expected 4173 but found 3902",
                    "errorStackTrace": "   at OrderTests.PlaceOrder() in OrderTests.cs:line 142",
                    "labels": [], "categories": [],
                    "steps": [
                      { "keyword": "When", "text": "the order is placed", "status": "Failed",
                        "durationSeconds": 0.4, "subSteps": [ {{assertion}} ], "attachments": [] }
                    ],
                    "httpInteractions": [], "attachments": []
                  }
                ] }
              ]
            }
            """);
        return path;
    }
}
