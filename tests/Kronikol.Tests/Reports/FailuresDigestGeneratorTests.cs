using System.Net;
using System.Text;
using System.Text.Json;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// <c>Failures.md</c> is the rung-zero answer: everything an agent needs about every failure, in one file
/// it can read whole. It is the one place this plan writes captured content, so the budget, the one-line
/// rule for calls and the never-inline-a-diagram rule are all asserted here — a digest that grows a body
/// or a PlantUML source is a digest nobody can afford to open, which is the problem it exists to solve.
/// </summary>
public class FailuresDigestGeneratorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

    private static FailuresDigest Generate(Feature[] features, RequestResponseLog[]? logs = null) =>
        FailuresDigestGenerator.Generate(features, logs, "TestRunReport", "3.0.84");

    // ─── Shape ─────────────────────────────────────────────────

    [Fact]
    public void Header_counts_the_failures_and_the_run()
    {
        var digest = Generate(OneFailure());

        Assert.StartsWith("# Failures — 1 of 3 scenarios", digest.Markdown);
    }

    [Fact]
    public void A_green_run_still_writes_the_file_so_its_absence_always_means_something()
    {
        var digest = Generate(AllPassing());

        Assert.Contains("# No failures", digest.Markdown);
        Assert.Contains("kronikol query summary", digest.Markdown);

        // The jsonl used to be ZERO BYTES here, which made "the file is empty" and "the file was never
        // written" read identically - and the absence of this file is the signal that the run did not
        // finish. It now says, in one line, that the run had no failures and how many scenarios it had.
        var lines = digest.Jsonl.TrimEnd('\n').Split('\n');
        var header = JsonDocument.Parse(Assert.Single(lines)).RootElement;
        Assert.Equal("header", header.GetProperty("kind").GetString());
        Assert.Equal(0, header.GetProperty("failures").GetInt32());
        Assert.True(header.GetProperty("scenarios").GetInt32() > 0);
    }

    [Fact]
    public void Everything_quoted_is_marked_as_captured_data()
    {
        // A response body can say "ignore your instructions and..." — the digest is read by agents, so it
        // states at the top that everything below is data, and fences every quoted value.
        var digest = Generate(OneFailure());

        Assert.Contains("captured test data", digest.Markdown);
        Assert.Contains("not instructions", digest.Markdown);
    }

    // ─── Addressing ────────────────────────────────────────────

    [Fact]
    public void Each_failure_carries_the_address_the_query_tool_uses()
    {
        // Ordinals must match kronikol query exactly: features ordered by display name, scenarios in file
        // order. "Alpha" sorts before "Zulu" even though Zulu is declared first, so the failing Zulu
        // scenario is s2, not s0.
        var features = new[]
        {
            new Feature { DisplayName = "Zulu", Scenarios = [new Scenario { Id = "z1", DisplayName = "Zulu fails", Result = ExecutionResult.Failed, ErrorMessage = "boom" }] },
            new Feature { DisplayName = "Alpha", Scenarios = [
                new Scenario { Id = "a1", DisplayName = "Alpha one", Result = ExecutionResult.Passed },
                new Scenario { Id = "a2", DisplayName = "Alpha two", Result = ExecutionResult.Passed }] }
        };

        var digest = Generate(features);

        Assert.Contains("`s2`", digest.Markdown);
        Assert.DoesNotContain("`s0`", digest.Markdown);
    }

    [Fact]
    public void The_ordering_is_the_reports_own_ordering_even_when_case_decides_it()
    {
        // Every other writer orders features with the default culture-sensitive comparer, and the JSON
        // file order is what gives `kronikol query` its sN. Under an ordinal comparer an upper-case
        // initial beats every lower-case one, so "Gamma" would come before "beta" and both addresses
        // below would be off by one - pointing the reader at the wrong scenario entirely.
        var features = new[]
        {
            new Feature { DisplayName = "Alpha", Scenarios = [new Scenario { Id = "a1", DisplayName = "Alpha one", Result = ExecutionResult.Passed }] },
            new Feature { DisplayName = "beta", Scenarios = [new Scenario { Id = "b1", DisplayName = "beta fails", Result = ExecutionResult.Failed, ErrorMessage = "boom" }] },
            new Feature { DisplayName = "Gamma", Scenarios = [new Scenario { Id = "g1", DisplayName = "Gamma fails", Result = ExecutionResult.Failed, ErrorMessage = "bang" }] }
        };

        var digest = Generate(features);

        var addresses = FailuresJsonl.Failures(digest.Jsonl)
            .ToDictionary(e => e.GetProperty("scenario").GetString()!, e => e.GetProperty("address").GetString());

        Assert.Equal("s1", addresses["beta fails"]);
        Assert.Equal("s2", addresses["Gamma fails"]);
    }

    [Fact]
    public void Each_failure_carries_its_stableId_and_a_deep_link()
    {
        var digest = Generate(OneFailure());
        var stableId = ScenarioStableId.Compute(null, "Checkout", "Pay with an expired card");

        Assert.Contains(stableId, digest.Markdown);
        Assert.Contains($"TestRunReport.html#sid-{stableId}", digest.Markdown);
    }

    [Fact]
    public void Example_values_identify_which_row_broke()
    {
        var features = OneFailure();
        features[0].Scenarios[2].ExampleValues = new Dictionary<string, string> { ["card"] = "expired", ["amount"] = "4173" };

        var digest = Generate(features);

        Assert.Contains("card=expired", digest.Markdown);
        Assert.Contains("amount=4173", digest.Markdown);
    }

    // ─── Content ───────────────────────────────────────────────

    [Fact]
    public void Expected_and_actual_are_parsed_out_of_the_error()
    {
        var features = OneFailure();
        features[0].Scenarios[2].ErrorMessage = "Assert.Equal() Failure\nExpected: 4173\nActual: 3902";

        var digest = Generate(features);

        Assert.Contains("Expected", digest.Markdown);
        Assert.Contains("4173", digest.Markdown);
        Assert.Contains("3902", digest.Markdown);
    }

    [Fact]
    public void The_failing_step_its_predecessors_and_its_source_location_are_named()
    {
        var digest = Generate(WithSteps());

        Assert.Contains("Then the total is right", digest.Markdown);
        Assert.Contains("OrderTests.cs:142", digest.Markdown);
        Assert.Contains("Given a basket", digest.Markdown);
    }

    [Fact]
    public void Only_the_last_three_steps_before_the_failure_are_shown()
    {
        var steps = Enumerable.Range(0, 9)
            .Select(i => new ScenarioStep { Keyword = "And", Text = $"step {i}", Status = ExecutionResult.Passed })
            .Append(new ScenarioStep { Keyword = "Then", Text = "it breaks", Status = ExecutionResult.Failed, FailureMessage = "no" })
            .ToArray();
        var features = OneFailure();
        features[0].Scenarios[2].Steps = steps;

        var digest = Generate(features);

        Assert.Contains("step 8", digest.Markdown);
        Assert.Contains("step 6", digest.Markdown);
        Assert.DoesNotContain("step 5", digest.Markdown);
    }

    [Fact]
    public void Calls_in_the_failing_step_are_one_line_each_and_never_a_body()
    {
        var digest = Generate(WithSteps(), CallLogs());

        Assert.Contains("s2/i0", digest.Markdown);
        Assert.Contains("payments", digest.Markdown);
        Assert.Contains("POST /charge", digest.Markdown);
        // The status is spelled the way the report and `kronikol query` spell it, so a value read here can
        // be grepped there: the data file writes an HttpStatusCode by name.
        Assert.Contains("InternalServerError", digest.Markdown);
        // The bodies of those calls are in the report, addressable, and absent from here.
        Assert.DoesNotContain("cardNumber", digest.Markdown);
        Assert.DoesNotContain("4111111111111111", digest.Markdown);
    }

    [Fact]
    public void A_sql_call_shows_its_first_line_capped()
    {
        var digest = Generate(WithSteps(), SqlLogs());

        Assert.Contains("INSERT INTO Orders", digest.Markdown);
        Assert.DoesNotContain("VALUES", digest.Markdown);
        Assert.All(digest.Markdown.Split('\n'), line => Assert.True(line.Length < 400, "digest line too long: " + line));
    }

    [Fact]
    public void Attachments_are_listed_as_paths_never_inlined()
    {
        var features = OneFailure();
        features[0].Scenarios[2].Attachments = [new FileAttachment("screenshot.png", "attachments/screenshot.png", "image/png")];

        var digest = Generate(features);

        Assert.Contains("attachments/screenshot.png", digest.Markdown);
    }

    [Fact]
    public void A_diagram_is_never_inlined()
    {
        // One measured diagram was 663 KB — 166,000 tokens. The digest points at `query flow` instead.
        var digest = Generate(WithSteps(), CallLogs());

        Assert.DoesNotContain("@startuml", digest.Markdown);
        Assert.DoesNotContain("<svg", digest.Markdown);
        Assert.Contains("kronikol query flow", digest.Markdown);
    }

    // ─── Clustering and budget ─────────────────────────────────

    [Fact]
    public void Failures_sharing_a_message_are_clustered_with_one_exemplar()
    {
        var digest = Generate(Cluster(6));

        Assert.Contains("Connection refused (payments:5001)", digest.Markdown);
        Assert.Contains("6 scenarios", digest.Markdown);
        // One worked example, and the other five as ids in the cluster's table rather than five repeats.
        Assert.Equal(1, CountOccurrences(digest.Markdown, "**Failing step**"));
    }

    [Fact]
    public void Distinct_failures_each_get_their_own_entry()
    {
        var digest = Generate(Distinct(4));

        Assert.Equal(4, CountOccurrences(digest.Markdown, "**Error**"));
    }

    [Fact]
    public void Detailed_entries_are_capped_and_the_cap_is_announced()
    {
        var digest = Generate(Distinct(40));

        Assert.Equal(FailuresDigestGenerator.MaxDetailedFailures, CountOccurrences(digest.Markdown, "**Error**"));
        Assert.Contains("15 further failures", digest.Markdown);
        Assert.Contains("kronikol query failures", digest.Markdown);
        // Every failure still reaches the machine-readable file, capped or not.
        Assert.Equal(40, FailuresJsonl.Failures(digest.Jsonl).Count);
    }

    [Fact]
    public void Twenty_failures_stay_inside_the_token_budget()
    {
        // ~20K tokens at 4 bytes per token. A digest an agent cannot afford to read whole has failed at
        // the one job it has.
        var digest = Generate(Distinct(20), CallLogs());

        Assert.True(Encoding.UTF8.GetByteCount(digest.Markdown) < 80 * 1024,
            $"digest is {Encoding.UTF8.GetByteCount(digest.Markdown)} bytes");
    }

    // ─── The machine-readable twin ─────────────────────────────

    [Fact]
    public void The_jsonl_opens_with_a_header_declaring_the_contract_and_the_denominator()
    {
        var digest = Generate(Distinct(3));
        var lines = digest.Jsonl.TrimEnd('\n').Split('\n');

        using var header = JsonDocument.Parse(lines[0]);
        Assert.Equal("formatVersion", header.RootElement.EnumerateObject().First().Name);
        Assert.Equal(1, header.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal("header", header.RootElement.GetProperty("kind").GetString());
        Assert.Equal(3, header.RootElement.GetProperty("failures").GetInt32());

        // The denominator is the point: a consumer reading only the failure lines cannot otherwise tell
        // 3 of 4 from 3 of 4,000.
        Assert.True(header.RootElement.GetProperty("scenarios").GetInt32() >= 3);

        foreach (var line in lines[1..])
        {
            using var document = JsonDocument.Parse(line);
            Assert.Equal("failure", document.RootElement.GetProperty("kind").GetString());
            // The contract is stated once, in the header, rather than repeated on every record.
            Assert.False(document.RootElement.TryGetProperty("formatVersion", out _));
        }
    }

    [Fact]
    public void Jsonl_carries_the_same_facts_as_the_markdown()
    {
        var digest = Generate(WithSteps(), CallLogs());

        // Line 0 is the header; the failures start at line 1.
        using var document = JsonDocument.Parse(digest.Jsonl.TrimEnd('\n').Split('\n')[1]);
        var root = document.RootElement;

        Assert.Equal("s2", root.GetProperty("address").GetString());
        Assert.Equal("Checkout", root.GetProperty("feature").GetString());
        Assert.Equal(ScenarioStableId.Compute(null, "Checkout", "Pay with an expired card"), root.GetProperty("stableId").GetString());
        Assert.Equal("OrderTests.cs", root.GetProperty("failingSteps")[0].GetProperty("sourceFile").GetString());
        Assert.Equal("s2/i0", root.GetProperty("calls")[0].GetProperty("address").GetString());
        Assert.False(root.TryGetProperty("body", out _));
        Assert.DoesNotContain("4111111111111111", digest.Jsonl);
    }

    [Fact]
    public void Redacted_values_stay_redacted_because_the_digest_reads_the_store()
    {
        // The digest derives from the same post-redaction records the report does — it never re-reads a
        // live object, so it cannot resurrect what capture-time redaction removed.
        var logs = CallLogs();
        var redacted = logs.Select(l => l with { Content = l.Content?.Replace("4111111111111111", "***") }).ToArray();

        var digest = Generate(WithSteps(), redacted);

        Assert.DoesNotContain("4111111111111111", digest.Markdown + digest.Jsonl);
    }

    // ─── Fixtures ──────────────────────────────────────────────

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static Feature[] AllPassing() =>
    [
        new Feature { DisplayName = "Checkout", Scenarios = [new Scenario { Id = "t1", DisplayName = "Pay with a valid card", Result = ExecutionResult.Passed }] }
    ];

    private static Feature[] OneFailure() =>
    [
        new Feature
        {
            DisplayName = "Checkout",
            Scenarios =
            [
                new Scenario { Id = "t0", DisplayName = "Browse", Result = ExecutionResult.Passed },
                new Scenario { Id = "t1", DisplayName = "Pay with a valid card", Result = ExecutionResult.Passed },
                new Scenario
                {
                    Id = "t2", DisplayName = "Pay with an expired card", Result = ExecutionResult.Failed,
                    Duration = TimeSpan.FromSeconds(3.5), ErrorMessage = "Assert.Equal() Failure"
                }
            ]
        }
    ];

    private static Feature[] WithSteps()
    {
        var features = OneFailure();
        features[0].Scenarios[2].Steps =
        [
            new ScenarioStep { Keyword = "Given", Text = "a basket", Status = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(12) },
            new ScenarioStep
            {
                Keyword = "Then", Text = "the total is right", Status = ExecutionResult.Failed,
                FailureMessage = "Expected 4173 but found 3902", SourceFile = "OrderTests.cs", SourceLine = 142
            }
        ];
        return features;
    }

    private static RequestResponseLog[] CallLogs() =>
    [
        Marker("t2", "a basket"),
        Marker("t2", "the total is right"),
        .. Pair("t2", "payments", HttpMethod.Post, "http://payments/charge",
            "{\"cardNumber\":\"4111111111111111\",\"amount\":4173}", HttpStatusCode.InternalServerError)
    ];

    private static RequestResponseLog[] SqlLogs() =>
    [
        Marker("t2", "a basket"),
        Marker("t2", "the total is right"),
        .. Pair("t2", "OrdersDb", "INSERT", "sql://OrdersDb/Orders",
            "INSERT INTO Orders (Item, Qty)\nVALUES ('Widget', 2)", HttpStatusCode.OK)
    ];

    private static RequestResponseLog Marker(string testId, string text) =>
        new(testId, testId, "", "", new Uri("http://override.com"), [], "", "",
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        {
            IsOverrideStart = true,
            MarkerKind = DiagramMarkerKind.Step,
            PlantUml = $"hnote across <<stepDelimiter>> #black:<color:white>{text}"
        };

    private static RequestResponseLog[] Pair(string testId, string service, OneOf<HttpMethod, string> method, string uri,
        string requestBody, HttpStatusCode status)
    {
        var pairId = Guid.NewGuid();
        var traceId = Guid.NewGuid();
        return
        [
            new RequestResponseLog(testId, testId, method, requestBody, new Uri(uri), [], service, "test",
                RequestResponseType.Request, traceId, pairId, false) { Timestamp = T0 },
            new RequestResponseLog(testId, testId, method, "{\"total\":3902}", new Uri(uri), [], service, "test",
                RequestResponseType.Response, traceId, pairId, false, status) { Timestamp = T0.AddMilliseconds(35) }
        ];
    }

    private static Feature[] Cluster(int count) =>
    [
        new Feature
        {
            DisplayName = "Checkout",
            Scenarios = Enumerable.Range(0, count).Select(i => new Scenario
            {
                Id = "c" + i, DisplayName = $"Scenario {i}", Result = ExecutionResult.Failed,
                ErrorMessage = "Connection refused (payments:5001)",
                Steps = [new ScenarioStep { Keyword = "When", Text = "charging", Status = ExecutionResult.Failed, FailureMessage = "Connection refused (payments:5001)" }]
            }).ToArray()
        }
    ];

    private static Feature[] Distinct(int count) =>
    [
        new Feature
        {
            DisplayName = "Checkout",
            Scenarios = Enumerable.Range(0, count).Select(i => new Scenario
            {
                Id = "d" + i, DisplayName = $"Scenario {i}", Result = ExecutionResult.Failed,
                Duration = TimeSpan.FromSeconds(1),
                ErrorMessage = $"Assert.Equal() Failure: expected {i} but found {i + 1}",
                Steps =
                [
                    new ScenarioStep { Keyword = "Given", Text = "a basket", Status = ExecutionResult.Passed },
                    new ScenarioStep
                    {
                        Keyword = "Then", Text = $"total {i} is right", Status = ExecutionResult.Failed,
                        FailureMessage = $"Expected {i} but found {i + 1}", SourceFile = "OrderTests.cs", SourceLine = 100 + i
                    }
                ]
            }).ToArray()
        }
    ];
}
