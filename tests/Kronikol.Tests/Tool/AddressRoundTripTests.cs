using Kronikol.Tool;

namespace Kronikol.Tests.Tool;

/// <summary>
/// Every address the tool prints, the tool accepts — and means the same thing by it.
///
/// <para>The tool's contract with an agent is that a listing hands back addresses so the next command
/// can be aimed, and the emitted <c>Reports/CLAUDE.md</c> says so. Three kinds of break in that contract
/// shipped together. A step address — the <c>s3/1.1</c> form that <c>failures</c>, <c>assertions</c>,
/// <c>http</c> and <c>grep</c> all print, and that <c>assertions --json</c> emits under a field literally
/// named <c>address</c> — was parsed, its step path discarded, and the whole scenario answered instead:
/// <c>steps s0/99</c> was byte-identical to <c>steps s0</c>, so a nonsense path and a real one produced
/// the same confident answer. A <c>stableId</c> — the one identifier the reference documents tell you to
/// use across runs, printed by <c>steps</c> and handed back by <c>diff</c>'s own refusal message — was
/// an address no verb would take. And three listings took an address at exit 0 and threw it away, so an
/// agent narrowing a search got the unnarrowed answer and nothing said otherwise.</para>
///
/// <para>A step path here always means <b>that step and everything under it</b>. The addresses
/// <c>failures</c> prints are frequently parents (a failing <c>1</c> above the assertion <c>1.1</c> that
/// actually failed), so exact-match would be the wrong answer in the common case rather than the corner.
/// <c>--step</c> follows the same rule, so the flag and the address cannot disagree.</para>
/// </summary>
public class AddressRoundTripTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-address-" + Guid.NewGuid().ToString("N"));

    public AddressRoundTripTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    // ─── A step path scopes the answer ──────────────────────────

    /// <summary>
    /// The sharpest demonstration there was: <c>assertions</c> prints one address per assertion, and
    /// feeding one back returned every assertion in the scenario, byte-identical to passing the bare
    /// scenario. The verb that printed the address could not use it, and answered with a strictly wider
    /// set at exit 0.
    /// </summary>
    [Fact]
    public void An_assertion_address_answers_for_that_assertion()
    {
        var report = Report();

        // Sub-steps are zero-indexed, so the assertion under step 1 is 1.0 - which is also the
        // stepPath the call under it carries.
        var scoped = Run("assertions", report, "s0/1.0");

        Assert.Contains("payment is captured", scoped, StringComparison.Ordinal);
        Assert.DoesNotContain("basket is empty", scoped, StringComparison.Ordinal);
        Assert.NotEqual(Run("assertions", report, "s0"), scoped);
    }

    [Fact]
    public void A_step_address_scopes_the_step_tree_to_that_step_and_its_substeps()
    {
        var report = Report();

        var scoped = Run("steps", report, "s0/1");

        Assert.Contains("the order is placed", scoped, StringComparison.Ordinal);
        Assert.Contains("payment is captured", scoped, StringComparison.Ordinal);
        Assert.DoesNotContain("a basket", scoped, StringComparison.Ordinal);
    }

    /// <summary>
    /// The defect in its original form: <c>TryScenario</c> never looked at the address kind, so a path
    /// that exists nowhere in the scenario produced the scenario's whole tree at exit 0.
    /// </summary>
    [Fact]
    public void A_step_path_that_is_not_in_the_scenario_is_refused_rather_than_widened()
    {
        var (output, error, exit) = RunFull("steps", Report(), "s0/99");

        Assert.Equal(2, exit);
        Assert.DoesNotContain("a basket", output, StringComparison.Ordinal);
        Assert.Contains("99", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_step_address_scopes_the_calls_a_flow_shows()
    {
        var report = Report();

        var scoped = Run("flow", report, "s0/1");

        Assert.Contains("/payments", scoped, StringComparison.Ordinal);
        Assert.DoesNotContain("/basket", scoped, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>--step</c> was exact string equality while an address would have to cover the subtree, which
    /// would have given one address form two meanings depending on which door it came through. Both are
    /// descendants-inclusive, so <c>flow s0/1</c> and <c>flow s0 --step 1</c> are the same question.
    /// </summary>
    [Fact]
    public void The_step_flag_and_the_step_address_are_the_same_question()
    {
        var report = Report();

        Assert.Equal(Run("flow", report, "s0", "--step", "1"), Run("flow", report, "s0/1"));
    }

    // ─── A stableId is an address ───────────────────────────────

    [Fact]
    public void A_stableId_the_tool_printed_is_an_address_the_tool_takes()
    {
        var report = Report();

        var line = Run("steps", report, "s0").Split('\n').First(l => l.StartsWith("stableId ", StringComparison.Ordinal));
        var stableId = line["stableId ".Length..].Trim();

        Assert.Equal(Run("steps", report, "s0"), Run("steps", report, "sid:" + stableId));
    }

    /// <summary>
    /// A stableId is not unique — a repeated <c>[Theory]</c> row, the same <c>Examples:</c> row in two
    /// blocks, or a retry give several scenarios one id. Picking the first would be a confident wrong
    /// answer, so the ambiguity is reported with the ordinals that resolve it.
    /// </summary>
    [Fact]
    public void A_stableId_that_several_scenarios_share_names_them_rather_than_picking_one()
    {
        var (_, error, exit) = RunFull("steps", ReportWithARetry(), "sid:aaaabbbbccccdddd");

        Assert.Equal(2, exit);
        Assert.Contains("s0", error, StringComparison.Ordinal);
        Assert.Contains("s1", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stableId_no_scenario_carries_says_so()
    {
        var (_, error, exit) = RunFull("steps", Report(), "sid:0000000000000000");

        Assert.Equal(2, exit);
        Assert.Contains("0000000000000000", error, StringComparison.Ordinal);
    }

    // ─── The other two identifiers the tool prints ────────

    /// <summary>
    /// <c>trace</c> heads its own answer <c>trace c90a1912… — 2 calls</c> and then refused that header
    /// as "not a trace id". The ellipsis is the tool's punctuation, not part of the id — a verb whose
    /// own first line is not re-feedable to itself.
    /// </summary>
    [Theory]
    [InlineData("c90a1912…")]
    [InlineData("c90a1912...")]
    [InlineData("c90a1912")]
    public void The_trace_header_the_tool_prints_is_a_trace_id_the_tool_takes(string given)
    {
        Assert.Contains("2 calls", Run("trace", TracedReport(), given), StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>http</c> prints <c>trace &lt;32 hex&gt; span &lt;16 hex&gt;</c>, and the span half was accepted
    /// by nothing at all — the refusal appeared one line below the place the id had come from. There is
    /// no per-span view to give, so it resolves to the trace the span belongs to and says so.
    /// </summary>
    [Fact]
    public void A_span_id_the_tool_printed_resolves_to_the_trace_it_belongs_to()
    {
        var output = Run("trace", TracedReport(), "44b7e97965d1957e");

        Assert.Contains("span id", output, StringComparison.Ordinal);
        Assert.Contains("2 calls", output, StringComparison.Ordinal);
    }

    // ─── Nothing is accepted and thrown away ────────────────────

    /// <summary>
    /// <c>failures</c>, <c>scenarios</c> and <c>summary</c> took a positional at exit 0 and never looked
    /// at it, so an agent that had narrowed to one scenario got the whole run back and read it as the
    /// narrowed answer. Two of the three have an obvious scoped meaning; <c>summary</c> does not, and
    /// says which verb does.
    /// </summary>
    [Fact]
    public void Failures_scoped_to_a_scenario_answers_for_that_scenario()
    {
        var report = Report();

        var scoped = Run("failures", report, "s1");

        Assert.Contains("Refund", scoped, StringComparison.Ordinal);
        Assert.DoesNotContain("Checkout", scoped, StringComparison.Ordinal);
    }

    [Fact]
    public void Scenarios_scoped_to_a_scenario_answers_for_that_scenario()
    {
        var scoped = Run("scenarios", Report(), "s1");

        Assert.Contains("Refund", scoped, StringComparison.Ordinal);
        Assert.DoesNotContain("Checkout", scoped, StringComparison.Ordinal);
    }

    [Fact]
    public void Summary_refuses_an_address_and_names_the_verb_that_takes_one()
    {
        var (_, error, exit) = RunFull("summary", Report(), "s0");

        Assert.Equal(2, exit);
        Assert.Contains("steps", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>grep</c>'s positional is a search term, so an address pasted into it was silently reinterpreted
    /// as a needle and answered "not found" at exit 0 — a confident negative about a scenario that is in
    /// the report. The grammar stays as it is; the miss says what happened.
    /// </summary>
    [Fact]
    public void Grep_told_to_search_for_an_address_says_that_is_what_it_did()
    {
        var (output, _, exit) = RunFull("grep", Report(), "s0/1");

        Assert.Equal(0, exit);
        Assert.Contains("address", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("steps s0/1", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>--body</c> took its value only when it parsed as an interaction or body address. Anything else
    /// fell through, was never consumed, landed in the positionals unread, and the diff ran unfiltered at
    /// exit 0 — a flag's value going the way flag NAMES used to before the per-verb validator.
    /// </summary>
    [Fact]
    public void A_body_address_that_does_not_parse_is_refused_rather_than_dropped()
    {
        var report = Report();
        var second = ReportWithARetry("Second.json");

        var (_, error, exit) = RunFull("diff", report, second, "--body", "not-an-address");

        Assert.Equal(2, exit);
        Assert.Contains("--body", error, StringComparison.Ordinal);
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
    /// Two scenarios. The first has two steps, each with a tracked assertion beneath it and a call of its
    /// own — the shape every one of these questions needs in order to tell a scoped answer from a
    /// whole-scenario one. A tracked assertion is a step with no <c>keyword</c>, which is how the report
    /// shape distinguishes one, so the sub-steps here carry none.
    /// </summary>
    private string Report(string fileName = "TestRunReport.json")
    {
        var path = Path.Combine(_dir, fileName);
        File.WriteAllText(path, """
            {
              "kronikolVersion": "3.4.1",
              "formatVersion": 1,
              "suite": "Widgets.Tests",
              "startTime": "2026-01-01T10:00:00Z",
              "endTime": "2026-01-01T10:05:00Z",
              "features": [
                { "name": "Orders", "labels": [], "scenarios": [
                  {
                    "id": "t0", "stableId": "1111222233334444", "name": "Checkout", "result": "Failed",
                    "durationSeconds": 1.0, "labels": [], "categories": [],
                    "steps": [
                      { "keyword": "Given", "text": "a basket", "status": "Passed", "durationSeconds": 0.1,
                        "subSteps": [
                          { "text": "the basket is empty", "status": "Passed",
                            "subSteps": [], "attachments": [] }
                        ], "attachments": [] },
                      { "keyword": "When", "text": "the order is placed", "status": "Failed", "durationSeconds": 0.4,
                        "failureMessage": "Expected 200 but got 500",
                        "subSteps": [
                          { "text": "the payment is captured", "status": "Failed",
                            "failureMessage": "Expected captured but was declined",
                            "subSteps": [], "attachments": [] }
                        ], "attachments": [] }
                    ],
                    "httpInteractions": [
                      { "type": "Request", "method": "GET", "uri": "https://shop.test/basket", "stepPath": "0",
                        "serviceName": "Shop", "callerName": "Tests", "statusCode": 200 },
                      { "type": "Request", "method": "POST", "uri": "https://shop.test/payments", "stepPath": "1.0",
                        "serviceName": "Payments", "callerName": "Tests", "statusCode": 500 }
                    ],
                    "attachments": []
                  },
                  {
                    "id": "t1", "stableId": "5555666677778888", "name": "Refund", "result": "Failed",
                    "durationSeconds": 0.5, "errorMessage": "Refund was not issued", "labels": [], "categories": [],
                    "steps": [
                      { "keyword": "When", "text": "a refund is requested", "status": "Failed", "durationSeconds": 0.2,
                        "failureMessage": "Refund was not issued", "subSteps": [], "attachments": [] }
                    ],
                    "httpInteractions": [], "attachments": []
                  }
                ] }
              ]
            }
            """);
        return path;
    }

    /// <summary>Two calls under one W3C trace, each carrying a span id of its own.</summary>
    private string TracedReport(string fileName = "Traced.json")
    {
        var path = Path.Combine(_dir, fileName);
        File.WriteAllText(path, """
            {
              "kronikolVersion": "3.4.1",
              "formatVersion": 1,
              "startTime": "2026-01-01T10:00:00Z",
              "endTime": "2026-01-01T10:05:00Z",
              "features": [
                { "name": "Orders", "labels": [], "scenarios": [
                  { "id": "t0", "stableId": "1111222233334444", "name": "Checkout", "result": "Passed",
                    "durationSeconds": 1.0, "labels": [], "categories": [], "steps": [],
                    "httpInteractions": [
                      { "type": "Request", "method": "GET", "uri": "https://shop.test/basket", "stepPath": null,
                        "serviceName": "Shop", "callerName": "Tests", "statusCode": 200,
                        "activityTraceId": "c90a19120a5d91d3130bd58cae64e945", "activitySpanId": "44b7e97965d1957e",
                        "timestamp": "2026-01-01T10:00:01Z" },
                      { "type": "Request", "method": "POST", "uri": "https://shop.test/orders", "stepPath": null,
                        "serviceName": "Shop", "callerName": "Tests", "statusCode": 201,
                        "activityTraceId": "c90a19120a5d91d3130bd58cae64e945", "activitySpanId": "9f1c2d3e4a5b6c7d",
                        "timestamp": "2026-01-01T10:00:02Z" }
                    ],
                    "attachments": [] }
                ] }
              ]
            }
            """);
        return path;
    }

    /// <summary>Two scenarios carrying one stableId — a retry, or a repeated Theory row.</summary>
    private string ReportWithARetry(string fileName = "Retried.json")
    {
        var path = Path.Combine(_dir, fileName);
        File.WriteAllText(path, """
            {
              "kronikolVersion": "3.4.1",
              "formatVersion": 1,
              "startTime": "2026-01-01T10:00:00Z",
              "endTime": "2026-01-01T10:05:00Z",
              "features": [
                { "name": "Orders", "labels": [], "scenarios": [
                  { "id": "t0", "stableId": "aaaabbbbccccdddd", "name": "Checkout", "result": "Failed",
                    "durationSeconds": 1.0, "labels": [], "categories": [], "steps": [], "httpInteractions": [] },
                  { "id": "t1", "stableId": "aaaabbbbccccdddd", "name": "Checkout", "result": "Passed",
                    "durationSeconds": 1.0, "labels": [], "categories": [], "steps": [], "httpInteractions": [] }
                ] }
              ]
            }
            """);
        return path;
    }
}
