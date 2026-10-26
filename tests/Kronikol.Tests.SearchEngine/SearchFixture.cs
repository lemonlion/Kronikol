namespace Kronikol.Tests.SearchEngine;

/// <summary>
/// One report for the facts that run the page's search (<see cref="SearchPage"/>): three features and eleven scenarios
/// holding every status (Passed, Failed, Skipped, Bypassed and SkippedAfterFailure), tags of a feature and of a
/// scenario (Happy Path, a tag with a space, one with brackets), dependencies in three alphabets, own times with and
/// without a first-call warm-up and one with none, history verdicts, and step text that holds what the wiki's examples
/// look for (<c>status:400</c> and <c>milk:</c> as text among them).
/// </summary>
public static class SearchFixture
{
    public static readonly PageFeature[] Features =
    [
        new("Order Feature", "/api/orders", ["api"],
        [
            new("Create order successfully", "Passed", ["api", "smoke", "Happy Path"], ["Order Service", "Database"],
                ["the system is running", "i create an order", "the order is created"],
                Happy: true, DurationMs: 2000, Verdicts: ["stable"]),
            new("Delete order fails gracefully", "Failed", ["api", "regression"], ["Order Service"],
                ["i delete a non-existent order", "an error is returned"],
                DurationMs: 5000, Error: "expected result 404 but the call ended in a timeout", Verdicts: ["broke"]),
            new("List orders returns paginated results", "Passed", ["api", "smoke", "slow", "Happy Path"], ["Order Service", "Database"],
                ["i list the orders", "each page holds ten orders"],
                Happy: true, DurationMs: 1000, Verdicts: ["flaky"]),
            new("Order history after a payment failure", "SkippedAfterFailure", ["api", "regression"], ["Payment Gateway", "Order Service"],
                ["a payment has failed", "i read the order history"],
                DurationMs: 300, Verdicts: ["failing"]),
        ]),
        new("Payment Feature", null, ["payments"],
        [
            new("Process payment", "Passed", ["payments", "ui", "Happy Path"], ["Payment Gateway"],
                ["i pay by card", "the payment is accepted"],
                Happy: true, DurationMs: 500, Verdicts: ["stable"]),
            new("Refund payment", "Skipped", ["payments", "slow"], ["Payment Gateway", "Ödeme Servisi"],
                ["a payment was taken", "i refund it"],
                DurationMs: 100),
            new("Payment times out", "Failed", ["payments", "api", "smoke"], ["Payment Gateway"],
                ["the gateway is slow", "i pay by card"],
                DurationMs: 3000, WarmUpMs: 1500, Error: "timeout waiting for the payment gateway: expected result 200", Verdicts: ["flaky", "broke"]),
            new("Audit payment ledger", "Bypassed", ["payments", "ui", "regression"], ["Audit Service"],
                ["the ledger is read", "the audit step is bypassed"],
                DurationMs: 50),
        ]),
        new("Pancakes Creation", null, ["kitchen"],
        [
            new("Create a pancake batch", "Passed", ["kitchen", "issue(12)", "two words"], ["支付服务"],
                ["the batter is mixed", "the api answers status:400 for a bad batch", "milk: 200ml is added"],
                DurationMs: 1500, Verdicts: ["new"]),
            new("Flip pancakes without a timer", "Passed", ["kitchen", "ui"], [],
                ["the pan is hot", "the pancakes are flipped"]),
            new("Burnt pancake is reported", "Failed", ["kitchen", "slow", "smoke"], ["Order Service"],
                ["the pan is too hot", "the pancake burns"],
                DurationMs: 2500, Error: "expected result golden but was burnt", Verdicts: ["always-failing"]),
        ]),
    ];

    /// <summary>This report's percentiles of own time, as the Duration row's buttons carry them.</summary>
    public static readonly IReadOnlyDictionary<string, double> Percentiles =
        new Dictionary<string, double> { ["p50"] = 1000, ["p90"] = 3000, ["p95"] = 4000, ["p99"] = 5000 };

    public static SearchPage Page(bool verdicts = true) => new(Features, verdicts, Percentiles);

    /// <summary>The scenarios of the fixture, in page order.</summary>
    public static IEnumerable<(PageFeature Feature, PageScenario Scenario)> Scenarios() =>
        Features.SelectMany(f => f.Scenarios.Select(s => (f, s)));

    /// <summary>The scenario skipped after a failure (S4), which <c>$skipped</c> finds since 5.9.1 (E3, followed by B4).</summary>
    public const string SkippedAfterFailure = "Order history after a payment failure";

    /// <summary>The names of the scenarios a table writes by their place, <c>S1,S4</c>, in page order; empty for none.</summary>
    public static string[] Named(string ids)
    {
        var all = Scenarios().Select(p => p.Scenario.Name).ToArray();
        return ids.Length == 0 ? [] : ids.Split(',').Select(id => all[int.Parse(id.Trim()[1..], System.Globalization.CultureInfo.InvariantCulture) - 1]).ToArray();
    }
}
