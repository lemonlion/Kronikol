using System.Net;
using System.Text.Json;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// "Calls in the failing step" is the digest's answer to what the run actually did when it broke, and it
/// was empty far more often than the file let on. Two causes, one visible symptom.
///
/// <para>Interactions are attributed to TOP-LEVEL steps — <c>ReportGenerator.OrderedStepPaths</c> emits
/// <c>b0..bN</c> and <c>0..N</c> and nothing else — while the digest walks the whole tree and asks for the
/// failing step by its nested path, <c>1.0</c>. Those two sets never intersect, so a scenario whose
/// failure is inside a composite step could not populate <c>calls</c> at all, whatever it had done.</para>
///
/// <para>And when the failing step genuinely made no calls, an empty list said nothing about whether the
/// scenario had made any. "We did not look there" and "there was nothing there" are different facts, and a
/// consumer could not tell them apart — cheap to fix with two consumers, expensive once anything reads it
/// as truth.</para>
/// </summary>
public class DigestCallScopeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

    private static RequestResponseLog Marker(string testId, string text) =>
        new(testId, testId, "", "", new Uri("http://override.com"), [], "", "",
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        {
            IsOverrideStart = true,
            MarkerKind = DiagramMarkerKind.Step,
            PlantUml = $"hnote across <<stepDelimiter>> #black:<color:white>{text}"
        };

    private static RequestResponseLog[] Pair(string testId, string service, string uri, HttpStatusCode status)
    {
        var pairId = Guid.NewGuid();
        var traceId = Guid.NewGuid();
        return
        [
            new RequestResponseLog(testId, testId, HttpMethod.Post, "{}", new Uri(uri), [], service, "test",
                RequestResponseType.Request, traceId, pairId, false) { Timestamp = T0 },
            new RequestResponseLog(testId, testId, HttpMethod.Post, "{}", new Uri(uri), [], service, "test",
                RequestResponseType.Response, traceId, pairId, false, status) { Timestamp = T0.AddMilliseconds(5) }
        ];
    }

    private static Feature[] WithNestedFailure() =>
    [
        new Feature
        {
            DisplayName = "Checkout",
            Scenarios =
            [
                new Scenario
                {
                    Id = "t0", DisplayName = "Pay", Result = ExecutionResult.Failed,
                    ErrorMessage = "Assert.Equal() Failure",
                    Steps =
                    [
                        new ScenarioStep { Keyword = "Given", Text = "a basket", Status = ExecutionResult.Passed },
                        // The composite step itself is NOT marked failed — only the child is. That is the
                        // shape a composite-step producer writes, and it is what made the paths disjoint.
                        new ScenarioStep
                        {
                            Keyword = "When", Text = "the order is placed", Status = ExecutionResult.Passed,
                            SubSteps =
                            [
                                new ScenarioStep
                                {
                                    Keyword = "And", Text = "the card is charged", Status = ExecutionResult.Failed,
                                    FailureMessage = "declined"
                                }
                            ]
                        }
                    ]
                }
            ]
        }
    ];

    private static FailuresDigest Generate(Feature[] features, RequestResponseLog[]? logs) =>
        FailuresDigestGenerator.Generate(features, logs, "TestRunReport", "3.1.0");

    private static JsonElement Record(FailuresDigest digest) =>
        FailuresJsonl.Failures(digest.Jsonl).Single();

    [Fact]
    public void A_failure_inside_a_composite_step_still_shows_the_calls_made_in_it()
    {
        // Two markers, so the pair is attributed to top-level step 1 — the composite whose child failed.
        var digest = Generate(WithNestedFailure(),
        [
            Marker("t0", "a basket"),
            Marker("t0", "the order is placed"),
            .. Pair("t0", "payments", "http://payments/charge", HttpStatusCode.InternalServerError)
        ]);

        Assert.Contains("payments", digest.Markdown, StringComparison.Ordinal);
        Assert.Contains("s0/i0", digest.Markdown, StringComparison.Ordinal);

        var record = Record(digest);
        Assert.Equal("failingStep", record.GetProperty("callsScope").GetString());
        Assert.Single(record.GetProperty("calls").EnumerateArray());
    }

    [Fact]
    public void A_failing_step_with_no_calls_of_its_own_falls_back_to_the_scenario_and_says_so()
    {
        // The call belongs to step 0, which passed. The failing step made none of its own.
        var digest = Generate(WithNestedFailure(),
        [
            Marker("t0", "a basket"),
            .. Pair("t0", "catalogue", "http://catalogue/items", HttpStatusCode.OK),
            Marker("t0", "the order is placed")
        ]);

        var record = Record(digest);
        Assert.Equal("scenario", record.GetProperty("callsScope").GetString());
        Assert.Single(record.GetProperty("calls").EnumerateArray());

        // The heading has to say what the list is, or a reader takes calls from a step that passed as the
        // calls that broke it — a worse answer than the empty list it replaces.
        Assert.Contains("catalogue", digest.Markdown, StringComparison.Ordinal);
        Assert.Contains("none were attributed to the failing step", digest.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void A_scenario_that_made_no_calls_at_all_says_none_rather_than_nothing()
    {
        var digest = Generate(WithNestedFailure(), null);

        var record = Record(digest);
        Assert.Equal("none", record.GetProperty("callsScope").GetString());
        Assert.Empty(record.GetProperty("calls").EnumerateArray());
        Assert.DoesNotContain("Calls in", digest.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void The_scenario_fallback_puts_the_failures_first()
    {
        // Nine successes and one error, none of them in the failing step. The error is the one a reader
        // needs, and it is the one a naive "last N" would push out.
        var logs = new List<RequestResponseLog> { Marker("t0", "a basket") };
        logs.AddRange(Pair("t0", "broken", "http://broken/thing", HttpStatusCode.BadGateway));
        for (var i = 0; i < 9; i++)
            logs.AddRange(Pair("t0", $"fine{i}", $"http://fine{i}/thing", HttpStatusCode.OK));
        logs.Add(Marker("t0", "the order is placed"));

        var record = Record(Generate(WithNestedFailure(), logs.ToArray()));
        var calls = record.GetProperty("calls").EnumerateArray().ToArray();

        Assert.Equal("scenario", record.GetProperty("callsScope").GetString());
        Assert.Equal("broken", calls[0].GetProperty("service").GetString());
    }

    [Fact]
    public void The_call_cap_is_what_its_name_says_it_is()
    {
        var logs = new List<RequestResponseLog> { Marker("t0", "a basket"), Marker("t0", "the order is placed") };
        for (var i = 0; i < 30; i++)
            logs.AddRange(Pair("t0", $"svc{i}", $"http://svc{i}/thing", HttpStatusCode.OK));

        var record = Record(Generate(WithNestedFailure(), logs.ToArray()));
        var calls = record.GetProperty("calls").EnumerateArray().ToArray();

        Assert.Equal("failingStep", record.GetProperty("callsScope").GetString());
        Assert.Equal(FailuresDigestGenerator.MaxCallsPerFailure, calls.Length);
    }

    [Fact]
    public void A_call_with_no_operation_label_is_named_by_its_target_not_its_payload()
    {
        // Measured on a real run, and the method is the reason the first guess at this was wrong: a
        // MessageQueue send carries the method "SEND (EVENT PROTOCOL)", which is neither empty nor an HTTP
        // verb, so "anything that is not an HTTP verb is a statement" called a business payload a statement
        // and the digest printed 120 characters of message body in the Call column, beside calls shown as
        // `GET /milk`. The category is the only thing that actually knows which it is.
        var pairId = Guid.NewGuid();
        var traceId = Guid.NewGuid();
        var body = "{\"BatchId\":\"7175f68b-7685-4c17-93b7-d8128d1e3e2a\",\"Ingredients\":[\"Some_Eggs\"]}";

        var digest = Generate(WithNestedFailure(),
        [
            Marker("t0", "a basket"),
            Marker("t0", "the order is placed"),
            new RequestResponseLog("t0", "t0", "SEND (EVENT PROTOCOL)", body, new Uri("broker://events/cake-batches"), [],
                "Event broker", "test", RequestResponseType.Request, traceId, pairId, false,
                DependencyCategory: Kronikol.Constants.DependencyCategories.MessageQueue) { Timestamp = T0 },
            new RequestResponseLog("t0", "t0", "SEND (EVENT PROTOCOL)", "", new Uri("broker://events/cake-batches"), [],
                "Event broker", "test", RequestResponseType.Response, traceId, pairId, false, "Responded",
                DependencyCategory: Kronikol.Constants.DependencyCategories.MessageQueue) { Timestamp = T0.AddMilliseconds(1) }
        ]);

        Assert.DoesNotContain("BatchId", digest.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Some_Eggs", digest.Markdown, StringComparison.Ordinal);
        // Named by where it went, and addressable for anyone who wants the payload.
        Assert.Contains("cake-batches", digest.Markdown, StringComparison.Ordinal);
        Assert.Contains("s0/i0", digest.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void A_statement_with_a_real_operation_label_is_still_shown()
    {
        // Non-vacuity: the statement IS the identity of a database call, and this must not have turned
        // every SQL row into a synthetic sql:// URI.
        var pairId = Guid.NewGuid();
        var traceId = Guid.NewGuid();

        var digest = Generate(WithNestedFailure(),
        [
            Marker("t0", "a basket"),
            Marker("t0", "the order is placed"),
            new RequestResponseLog("t0", "t0", "INSERT", "INSERT INTO Orders (Item, Qty)\nVALUES ('Widget', 2)",
                new Uri("sql://OrdersDb/Orders"), [], "OrdersDb", "test",
                RequestResponseType.Request, traceId, pairId, false) { Timestamp = T0 },
            new RequestResponseLog("t0", "t0", "INSERT", "", new Uri("sql://OrdersDb/Orders"), [], "OrdersDb", "test",
                RequestResponseType.Response, traceId, pairId, false, HttpStatusCode.OK) { Timestamp = T0.AddMilliseconds(1) }
        ]);

        Assert.Contains("INSERT INTO Orders", digest.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("VALUES", digest.Markdown, StringComparison.Ordinal);
    }
}
