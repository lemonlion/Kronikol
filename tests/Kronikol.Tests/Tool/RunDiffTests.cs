using System.Text.Json;
using Kronikol.Tool;

namespace Kronikol.Tests.Tool;

/// <summary>
/// <c>kronikol query diff &lt;old&gt; &lt;new&gt;</c> compares two runs scenario by scenario, and says so
/// when it cannot.
///
/// <para>Two things it used to do instead. The Tracking section compared whole-file per-service totals
/// across scenario sets that did not match, so one added test hid a total capture loss and one removed
/// test invented one - and a run that captured nothing at all was skipped as "absent", after which the
/// diff printed <c>no change in results, timings or tracked calls</c>. And a pair of runs whose
/// <c>stableId</c>s could not be matched - one side written before the ids existed, or the two sides'
/// ids computed under different suites - was "matched by position" and reported every scenario as both
/// new and gone, at exit 0.</para>
/// </summary>
public class RunDiffTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-run-diff").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    // ─── Tracking over matched scenarios ──────────────────────

    [Fact]
    public void A_removed_scenario_does_not_invent_a_tracking_loss()
    {
        var old = Write("old.json", [Pay("OrdersApi", "OrdersApi"), Refund("PricingApi", "PricingApi", "PricingApi")]);
        var @new = Write("new.json", [Pay("OrdersApi", "OrdersApi")]);

        var (output, error, exit) = Diff(old, @new);

        Assert.True(exit == 0, error);
        Assert.Contains("Gone (1)", output);
        Assert.DoesNotContain("Tracking", output);
        Assert.DoesNotContain("PricingApi", output.Split("Gone (1)")[0]);
    }

    [Fact]
    public void An_added_scenario_does_not_hide_a_tracking_loss()
    {
        var old = Write("old.json", [Pay("OrdersApi", "OrdersApi", "OrdersApi")]);
        var @new = Write("new.json", [Pay(), Refund("OrdersApi", "OrdersApi", "OrdersApi")]);

        var (output, error, exit) = Diff(old, @new);

        Assert.True(exit == 0, error);
        Assert.Contains("New (1)", output);
        Assert.Contains("Tracking", output);
        Assert.Contains("OrdersApi  3 → 0 calls  — no longer tracked", output);
        Assert.Contains("not compared: 3 calls in 1 new scenario(s)", output);
    }

    [Fact]
    public void A_run_that_captured_nothing_is_the_total_loss()
    {
        var old = Write("old.json", [Pay("OrdersApi", "PricingApi")]);
        var @new = Write("new.json", [Pay()]);

        var (output, error, exit) = Diff(old, @new);

        Assert.True(exit == 0, error);
        Assert.Contains("total  2 → 0 calls  — nothing tracked", output);
        Assert.DoesNotContain("no change in results, timings or tracked calls", output);
    }

    [Fact]
    public void A_side_that_cannot_carry_traffic_is_said_and_not_compared()
    {
        // A mergeable file written before 3.1.0 has no httpInteractions by construction. That is not a
        // run that lost all of them, and the version is what tells the two apart.
        var old = Write("old.json", [Pay()], version: "3.0.40", mergeable: true);
        var @new = Write("new.json", [Pay("OrdersApi")]);

        var (output, error, exit) = Diff(old, @new);

        Assert.True(exit == 0, error);
        Assert.Contains("! old: a mergeable file written before 3.1.0 carries no interactions", output);
        Assert.DoesNotContain("Tracking", output);
        Assert.Contains("no change in results or timings", output);
        Assert.DoesNotContain("no change in results, timings or tracked calls", output);
    }

    [Fact]
    public void A_service_one_scenario_stopped_seeing_is_reported_even_when_the_total_held()
    {
        // Five PricingApi calls moved from one scenario to another. The totals agree; the scenario that
        // stopped seeing the service does not, and that is the correlation-regression signature.
        var old = Write("old.json", [Pay("PricingApi", "PricingApi", "PricingApi", "PricingApi", "PricingApi"), Refund()]);
        var @new = Write("new.json", [Pay(), Refund("PricingApi", "PricingApi", "PricingApi", "PricingApi", "PricingApi")]);

        var (output, error, exit) = Diff(old, @new);

        Assert.True(exit == 0, error);
        Assert.Contains("PricingApi  5 → 0 calls in s0 Pay by card  — still 5 elsewhere", output);
        Assert.DoesNotContain("no longer tracked", output);
    }

    [Fact]
    public void Removed_scenarios_calls_are_stated_beside_a_real_loss_not_compared_into_it()
    {
        var old = Write("old.json", [Pay("OrdersApi", "OrdersApi", "OrdersApi"), Refund("PricingApi", "PricingApi", "PricingApi", "PricingApi")]);
        var @new = Write("new.json", [Pay("OrdersApi")]);

        var (output, error, exit) = Diff(old, @new);

        Assert.True(exit == 0, error);
        Assert.Contains("OrdersApi  3 → 1 calls  (-2)", output);
        Assert.Contains("not compared: 4 calls in 1 scenario(s) only in the old run", output);
        Assert.DoesNotContain("PricingApi  4", output);
    }

    // ─── Runs that cannot be matched ──────────────────────────

    [Fact]
    public void A_diff_where_only_the_new_side_has_stableIds_is_refused_naming_the_old()
    {
        var old = Write("old.json", [Pay() with { StableId = null }]);
        var @new = Write("new.json", [Pay()]);

        var (output, error, exit) = Diff(old, @new);

        Assert.Equal(2, exit);
        Assert.Contains("the old report (old.json) has no stableIds", error);
        Assert.Contains("3.0.47", error);
        Assert.DoesNotContain("matched by position", output);
        Assert.Empty(output.Trim());
    }

    [Fact]
    public void A_diff_where_only_the_old_side_has_stableIds_is_refused_naming_the_new()
    {
        var old = Write("old.json", [Pay()]);
        var @new = Write("new.json", [Pay() with { StableId = null }]);

        var (_, error, exit) = Diff(old, @new);

        Assert.Equal(2, exit);
        Assert.Contains("the new report (new.json) has no stableIds", error);
    }

    [Fact]
    public void Two_reports_without_stableIds_are_still_matched_by_position()
    {
        var old = Write("old.json", [Pay() with { StableId = null }]);
        var @new = Write("new.json", [Pay() with { StableId = null, Result = "Failed" }]);

        var (output, error, exit) = Diff(old, @new);

        Assert.True(exit == 0, error);
        Assert.Contains("matched by position", output);
        Assert.Contains("BROKE", output);
    }

    [Fact]
    public void Ids_computed_under_different_suites_are_refused_with_both_suites_named()
    {
        // Same tests, same names, and not one stableId in common: since 3.1.0 the suite is in the hash.
        // A renamed SuiteName, a merge across suites, or a Kronikol4J run beside a .NET one all look
        // like this, and "everything is new and everything is gone" is not an answer to any of them.
        var old = Write("old.json", [Pay(), Refund()], suite: "Orders.Tests");
        var @new = Write("new.json", [Pay() with { StableId = "1111111111111111" }, Refund() with { StableId = "2222222222222222" }]);

        var (output, error, exit) = Diff(old, @new);

        Assert.Equal(2, exit);
        Assert.Contains("no stableId is shared by the two reports, yet 2 scenario name(s) are", error);
        Assert.Contains("old: Orders.Tests, new: (none recorded)", error);
        Assert.Contains("SuiteName", error);
        Assert.Empty(output.Trim());
    }

    [Fact]
    public void Two_unrelated_suites_are_compared_as_all_gone_and_all_new()
    {
        var old = Write("old.json", [Pay()]);
        var @new = Write("new.json", [Refund() with { StableId = "2222222222222222" }]);

        var (output, error, exit) = Diff(old, @new);

        Assert.True(exit == 0, error);
        Assert.Contains("Gone (1)", output);
        Assert.Contains("New (1)", output);
    }

    [Fact]
    public void An_empty_old_run_is_not_a_mismatch()
    {
        var old = Write("old.json", []);
        var @new = Write("new.json", [Pay()]);

        var (output, error, exit) = Diff(old, @new);

        Assert.True(exit == 0, error);
        Assert.Contains("New (1)", output);
    }

    // ─── Sections ─────────────────────────────────────────────

    [Fact]
    public void A_new_scenario_is_listed_as_new_not_broken()
    {
        var old = Write("old.json", [Pay()]);
        var @new = Write("new.json", [Pay(), Refund()]);

        var (output, error, exit) = Diff(old, @new);

        Assert.True(exit == 0, error);
        Assert.Contains("New (1):", output);
        Assert.Contains("s1 Refund a card [Passed]", output);
        Assert.DoesNotContain("Broken", output);
        Assert.DoesNotContain("no change in results", output);
    }

    [Fact]
    public void A_new_scenario_is_new_in_the_json_too()
    {
        var old = Write("old.json", [Pay()]);
        var @new = Write("new.json", [Pay(), Refund()]);

        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(["diff", old, @new, "--json"], output, error);

        Assert.True(exit == 0, error.ToString());
        var items = JsonDocument.Parse(output.ToString()).RootElement.GetProperty("items");
        var item = Assert.Single(items.EnumerateArray());
        Assert.Equal("new", item.GetProperty("kind").GetString());
    }

    // ─── Fixtures ─────────────────────────────────────────────

    private sealed record Sc(string Name, string? StableId, string[] Services, string Result = "Passed");

    private static Sc Pay(params string[] services) => new("Pay by card", "aaaabbbbccccdddd", services);

    private static Sc Refund(params string[] services) => new("Refund a card", "eeeeffff00001111", services);

    private (string Output, string Error, int Exit) Diff(string old, string @new)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(["diff", old, @new], output, error);
        return (output.ToString(), error.ToString(), exit);
    }

    private string Write(string name, Sc[] scenarios, string version = "3.5.1", bool mergeable = false, string? suite = null)
    {
        var body = string.Join(",\n", scenarios.Select((s, i) =>
        {
            var interactions = string.Join(",\n", s.Services.Select(service =>
                $$"""{ "type": "Request", "method": "GET", "uri": "https://{{service}}/x", "serviceName": "{{service}}", "callerName": "Test", "headers": [] }"""));
            var stableId = s.StableId is null ? "" : $"\"stableId\": \"{s.StableId}\",";
            return $$"""
                { "id": "t{{i}}", {{stableId}} "name": {{JsonSerializer.Serialize(s.Name)}}, "result": "{{s.Result}}",
                  "durationSeconds": 1.0, "labels": [], "categories": [], "steps": [],
                  "httpInteractions": [ {{interactions}} ] }
                """;
        }));

        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, $$"""
            {
              "kronikolVersion": "{{version}}",
              {{(mergeable ? "\"mergeableFormatVersion\": 1," : "")}}
              {{(suite is null ? "" : $"\"suite\": {JsonSerializer.Serialize(suite)},")}}
              "startTime": "2026-01-01T10:00:00Z",
              "endTime": "2026-01-01T10:05:00Z",
              "features": [ { "name": "Checkout", "labels": [], "scenarios": [ {{body}} ] } ]
            }
            """);
        return path;
    }
}
